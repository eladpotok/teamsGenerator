using System.Security.Claims;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting.Internal;
using TeamsGeneratorWebAPI.Controllers;
using TeamsGeneratorWebAPI.Telemetry;

internal static class WebsiteReferralRegression
{
    public static void Run(Action<bool, string> check)
    {
        var visit = new WebsiteVisitRequest("de305d54-75b4-431b-adb2-eb6b9e546014",
            "f47ac10b-58cc-4372-a567-0e02b2c3d479", "producthunt", "referral", "launch",
            "www.producthunt.com", "app");
        var expected = WebsiteAttribution.Properties(visit)!;
        check(expected["visitor_key"] != visit.VisitorId && expected["session_key"] != visit.SessionId,
            "website identifiers use separate pseudonymous domains");
        check(expected["session_key"] == WebsiteAttribution.Properties(visit)!.GetValueOrDefault("session_key"),
            "retries have a stable cross-worker session deduplication key");
        check(expected["session_key"] != WebsiteAttribution.Properties(visit with
            { VisitorId = Guid.NewGuid().ToString("D") })!["session_key"],
            "different browsers cannot collide by supplying the same session");
        foreach (var invalid in new[]
        {
            visit with { VisitorId = "PRIVATE PERSON" }, visit with { SessionId = Guid.Empty.ToString() },
            visit with { Source = "private@example.com" }, visit with { Source = new string('a', 81) },
            visit with { Medium = null }, visit with { Campaign = "https://private/path" },
            visit with { ReferrerHost = "https://producthunt.com/private?token=SECRET" },
            visit with { ReferrerHost = "private@example.com" },
            visit with { ReferrerHost = new string('a', 129) },
            visit with { LandingPage = "/join-group/SECRET" }
        })
            check(WebsiteAttribution.Properties(invalid) == null, "unbounded/private referral input is rejected");
        check(WebsiteAttribution.Properties(null) == null, "missing visit is not accepted");
        check(WebsiteAttribution.Properties(visit with
            { Source = "direct", Medium = "none", Campaign = "none", ReferrerHost = "none" }) != null,
            "anonymous direct landings are counted");

        var context = new DefaultHttpContext();
        context.Request.Headers["client_version"] = "20.26";
        context.Request.Headers.UserAgent = "Mozilla/5.0 Chrome/140.0";
        using var channel = new MemoryChannel();
        using var configuration = new TelemetryConfiguration
        {
            ConnectionString = "InstrumentationKey=00000000-0000-0000-0000-000000000001",
            TelemetryChannel = channel
        };
        var telemetry = new UsageTelemetry(new TelemetryClient(configuration),
            new HostingEnvironment { EnvironmentName = "Production" },
            new HttpContextAccessor { HttpContext = context });
        using var limiter = new WebsiteVisitRateLimiter();
        var controller = new WebsiteVisitsController(telemetry, limiter)
            { ControllerContext = new ControllerContext { HttpContext = context } };
        check(controller.Post(visit) is OkObjectResult, "visit endpoint does not require a Firebase login");
        var emitted = channel.Events.Single();
        check(emitted.Name == "WebsiteVisitObserved" && emitted.Properties["actor_type"] == "visitor"
            && string.IsNullOrEmpty(emitted.Context.User.Id)
            && emitted.Properties["utm_source"] == "producthunt"
            && emitted.Properties["traffic_type"] == "browser",
            "visit event is anonymous and source-attributed in the existing pipeline");
        check(!System.Text.Json.JsonSerializer.Serialize(emitted.Properties).Contains(visit.VisitorId!),
            "raw random browser identifiers never enter event dimensions");
        context.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("user_id", "organizer") }, "test"));
        controller.Post(visit);
        check(string.IsNullOrEmpty(channel.Events.Last().Context.User.Id),
            "even an authenticated caller cannot turn a landing event into organizer activity");
        context.Request.Headers["X-Teamify-Visitor"] = visit.VisitorId;
        context.Request.Headers["X-Teamify-Session"] = visit.SessionId;
        context.Request.Headers["X-Teamify-Source"] = visit.Source;
        context.Request.Headers["X-Teamify-Medium"] = visit.Medium;
        context.Request.Headers["X-Teamify-Campaign"] = visit.Campaign;
        context.Request.Headers["X-Teamify-Referrer"] = visit.ReferrerHost;
        context.Request.Headers["X-Teamify-Landing"] = visit.LandingPage;
        foreach (var name in new[] { "AccountRegistrationObserved", "AppSetupLoaded", "TeamsGenerated" })
        {
            telemetry.Track(name);
            emitted = channel.Events.Last();
            check(emitted.Properties["session_key"] == expected["session_key"]
                && emitted.Properties["identity_source"] == "authenticated"
                && emitted.Context.User.Id == UsageTelemetry.CreateAnonymousUserId("organizer"),
                "existing authenticated event retains identity and joins the anonymous session: " + name);
        }
        context.Request.Headers["X-Teamify-Source"] = "PRIVATE@example.com";
        telemetry.Track("InvalidAttribution");
        check(!channel.Events.Last().Properties.ContainsKey("session_key")
            && channel.Events.Last().Context.User.Id == UsageTelemetry.CreateAnonymousUserId("organizer"),
            "invalid optional referral metadata is dropped without breaking existing actions");
        var count = channel.Events.Count;
        check(controller.Post(visit with { Source = "PRIVATE@example.com" }) is BadRequestObjectResult
            && channel.Events.Count == count, "invalid visit returns an explicit 400 without an event");
        context.Request.Headers["X-Teamify-Readiness"] = "1";
        controller.Post(visit);
        check(channel.Events.Count == count, "readiness probes are excluded");
        context.Request.Headers.Remove("X-Teamify-Readiness");
        context.Request.Headers["client_version"] = "metrics-verification-website";
        controller.Post(visit);
        check(channel.Events.Count == count, "verification clients are excluded");
        check(WebsiteVisitRateLimiter.IsAutomated("Googlebot")
            && WebsiteVisitRateLimiter.IsAutomated("HeadlessChrome")
            && !WebsiteVisitRateLimiter.IsAutomated("Mozilla/5.0 Chrome/140.0"),
            "known bot/automation traffic is separate from browser counts");
        for (var index = 0; index < 120; index++)
            check(limiter.Allow("192.0.2.1"), "bounded anonymous visit allowance");
        check(!limiter.Allow("192.0.2.1") && limiter.Allow("192.0.2.2"),
            "per-address visit throttling has an independent allowance");
        using var exhausted = new WebsiteVisitRateLimiter();
        for (var index = 0; index < 120; index++) exhausted.Allow(null);
        var limited = new WebsiteVisitsController(telemetry, exhausted)
            { ControllerContext = new ControllerContext { HttpContext = context } };
        check(limited.Post(visit) is ObjectResult { StatusCode: 429 }
            && context.Response.Headers.RetryAfter == "60", "rate rejection includes retry guidance");
    }

    private sealed class MemoryChannel : ITelemetryChannel
    {
        public readonly List<EventTelemetry> Events = new();
        public bool? DeveloperMode { get; set; }
        public string EndpointAddress { get; set; } = "";
        public void Send(ITelemetry item) { if (item is EventTelemetry value) Events.Add(value); }
        public void Flush() { }
        public void Dispose() { }
    }
}
