using System.Security.Claims;
using Azure;
using Azure.Data.Tables;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using TeamsGeneratorWebAPI.Controllers;
using TeamsGeneratorWebAPI.Telemetry;

internal static class RegistrationRegression
{
    public static async Task Run(Action<bool, string> check)
    {
        var table = new BillingStorageRegression.MemoryTable();
        var now = DateTimeOffset.Parse("2026-10-06T00:00:00Z");
        var telemetry = new RecordingTelemetry();
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("user_id", "account-one") }, "test"))
        };
        RegistrationsController Controller(IRegistrationStore? store = null) =>
            new(store ?? new RegistrationStore(table), telemetry, new Clock(now),
                NullLogger<RegistrationsController>.Instance)
            { ControllerContext = new ControllerContext { HttpContext = context } };
        var controller = Controller();
        var request = new RegistrationObservationRequest(now.AddDays(-2), "email_password");
        check(typeof(RegistrationsController).GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>().Single().AuthenticationSchemes == "Bearer",
            "registration endpoint requires the existing Firebase bearer validator");
        check(typeof(RegistrationObservationRequest).GetProperties().All(p => p.Name != "UserId"),
            "registration request cannot choose its account owner");
        context.User = new ClaimsPrincipal(new ClaimsIdentity());
        check(await controller.Post(request, default) is UnauthorizedResult && telemetry.Events.Count == 0,
            "legacy anonymous callers cannot submit registration facts");
        context.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("email", "not-a-uid") }, "test"));
        check(await controller.Post(request, default) is UnauthorizedResult,
            "authenticated principal missing an account ID cannot submit a fact");
        context.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("user_id", "account-one") }, "test"));
        foreach (var invalid in new[]
        {
            request with { CreatedAt = default },
            request with { CreatedAt = now.AddDays(1) },
            request with { Method = "ordinary_login" },
            request with { Method = null! }
        })
            check(await controller.Post(invalid, default) is BadRequestObjectResult,
                "invalid registration timestamps and operations are rejected");
        check(await controller.Post(request, default) is OkObjectResult, "first registration fact is acknowledged");
        var key = UsageMetrics.ResourceKey("registration", "account-one");
        var stored = (await table.GetEntityIfExistsAsync<TableEntity>(RegistrationStore.Partition, key)).Value;
        check(stored.GetDateTimeOffset("CreatedAt") == request.CreatedAt
            && stored.GetDateTimeOffset("FirstReportedAt") == now,
            "creation time and first observation time are durable separate facts");
        check(telemetry.Events.Single()["registration_key"] == key
            && telemetry.Events.Single()["registration_source"] == "firebase_client_new_user",
            "telemetry identifies the source as a client new-user assertion");
        await Controller().Post(request with { CreatedAt = now, Method = "google_redirect" }, default);
        check(telemetry.Events.Last()["created_at"] == request.CreatedAt.ToString("O")
            && telemetry.Events.Last()["registration_method"] == "email_password",
            "service recreation and retries cannot rewrite first fact or its method");

        var competing = Enumerable.Range(0, 12).Select(index => Task.Run(() =>
            new RegistrationStore(table).GetOrCreateAsync(
                new RegistrationFact("race", now.AddMinutes(index), now, index.ToString()), default)));
        var facts = await Task.WhenAll(competing);
        check(facts.Distinct().Count() == 1, "concurrent insert races converge on one durable account fact");

        telemetry.Fail = true;
        context.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("user_id", "account-two") }, "test"));
        try { await Controller().Post(request, default); check(false, "failed projection cannot claim receipt"); }
        catch (InvalidOperationException) { check(true, "projection failure propagates for retry"); }
        telemetry.Fail = false;
        await Controller().Post(request with { CreatedAt = now }, default);
        check(telemetry.Events.Last()["created_at"] == request.CreatedAt.ToString("O")
            && telemetry.Events.Last()["registration_key"] != key,
            "insert-before-projection failure is recoverable without rewriting creation or merging accounts");
        check(await Controller(new FailedStore()).Post(request, default) is ObjectResult { StatusCode: 503 }
            && context.Response.Headers["Retry-After"] == "60",
            "storage failure surfaces an explicit retryable response");
        context.Request.Headers["client_version"] = " METRICS-VERIFICATION-registration ";
        context.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("user_id", "verification-account") }, "test"));
        var eventCount = telemetry.Events.Count;
        check(await Controller().Post(request, default) is OkObjectResult
            && telemetry.Events.Count == eventCount
            && !(await table.GetEntityIfExistsAsync<TableEntity>(RegistrationStore.Partition,
                UsageMetrics.ResourceKey("registration", "verification-account"))).HasValue,
            "verification registration requests are excluded before durable insert and projection");
    }

    private sealed class Clock : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public Clock(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }

    private sealed class FailedStore : IRegistrationStore
    {
        public Task<RegistrationFact> GetOrCreateAsync(RegistrationFact fact, CancellationToken cancellationToken) =>
            throw new RequestFailedException(503, "synthetic");
    }

    private sealed class RecordingTelemetry : IUsageTelemetry
    {
        public bool Fail { get; set; }
        public List<Dictionary<string, string?>> Events { get; } = new();
        public void Track(string eventName, string? clientVersion = null, string? userId = null,
            IReadOnlyDictionary<string, string?>? properties = null,
            IReadOnlyDictionary<string, double>? measurements = null)
        {
            if (Fail) throw new InvalidOperationException("synthetic projection failure");
            Events.Add(properties!.ToDictionary(p => p.Key, p => p.Value));
        }
    }
}
