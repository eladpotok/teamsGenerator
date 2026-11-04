using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using TeamsGenerator.Algos.SkillWiseAlgo;
using TeamsGeneratorWebAPI;

internal static class RuntimeFrameworkRegression
{
    internal static async Task Run(Action<bool, string> check)
    {
        check(Environment.Version.Major == 10, "regressions execute on .NET 10 without major runtime roll-forward");
        check(typeof(JwtBearerOptions).Assembly.GetName().Version?.Major == 10,
            "JWT bearer authentication uses the ASP.NET Core 10 assembly");

        const string legacyHash =
            "AAABAgMEBQYHCAkKCwwNDg/rDBJD/1cxbmDmi+CY8bH4tXUIc8ZodIldz8Rf8fhE3w==";
        check(Utils.VerifyHashedPassword(legacyHash, "legacy-test-password"),
            "pre-migration Identity V2 password fixture still authenticates");
        check(!Utils.VerifyHashedPassword(legacyHash, "incorrect-password"),
            "legacy password fixture rejects an incorrect password");
        var hash = Utils.HashPassword("new-test-password");
        var bytes = Convert.FromBase64String(hash);
        check(bytes.Length == 49 && bytes[0] == 0
            && Utils.VerifyHashedPassword(hash, "new-test-password"),
            "new password hashes preserve the existing storage format");
        check(hash != Utils.HashPassword("new-test-password"), "password hashes retain independent random salts");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllers().AddNewtonsoftJson();
        await using var provider = services.BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = provider };
        await using var body = new MemoryStream();
        context.Response.Body = body;

        // Execute MVC's actual formatter, not just JsonConvert, to catch runtime/package mismatches.
        var payload = new
        {
            Success = true,
            Player = new SkillWisePlayer { Key = "runtime-test", GoalkeeperSkill = 8 },
            Config = JObject.Parse("""{"customSkill":7,"nullableValue":null}""")
        };
        await new JsonResult(payload).ExecuteResultAsync(
            new ActionContext(context, new RouteData(), new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor()));
        var mvc = JObject.Parse(Encoding.UTF8.GetString(body.ToArray()));
        check(mvc["success"]?.Value<bool>() == true && mvc["player"]?["key"]?.Value<string>() == "runtime-test",
            "MVC Newtonsoft formatter preserves camel-case response contracts");
        check(mvc["player"]?["effectiveRank"] == null && mvc["player"]?["goalkeeperSkill"]?.Value<int>() == 8,
            "MVC Newtonsoft formatter honors player assessment serialization attributes");
        check(mvc["config"]?["customSkill"]?.Value<int>() == 7
            && mvc["config"]?["nullableValue"]?.Type == JTokenType.Null,
            "MVC Newtonsoft formatter preserves dynamic JSON and explicit nulls");

        body.SetLength(0);
        await ApplicationHealth.Read(context).ExecuteAsync(context);
        using var health = JsonDocument.Parse(body.ToArray());
        check(context.Response.StatusCode == 200 && health.RootElement.GetProperty("status").GetString() == "healthy",
            "minimal health result serializes using the modern System.Text.Json runtime");
        check(Encoding.UTF8.GetString(body.ToArray()) == """{"status":"healthy"}""",
            "revision-aware health preserves the exact legacy JSON body");
        check(context.Response.Headers[ApplicationHealth.RevisionHeader] == typeof(ApplicationHealth).Module.ModuleVersionId.ToString("N"),
            "health identifies the loaded API module, not configured deployment metadata");
        check(context.Response.Headers[ApplicationHealth.RuntimeHeader] == Environment.Version.ToString()
            && context.Response.Headers.CacheControl == "no-store",
            "health supplies the actual runtime and prevents cached cutover evidence");

        var forwarding = new ForwardedHeadersMiddleware(_ => Task.CompletedTask, NullLoggerFactory.Instance,
            Options.Create(new ForwardedHeadersOptions
            {
                ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
            }));
        async Task<DefaultHttpContext> Forward(IPAddress proxy)
        {
            var request = new DefaultHttpContext();
            request.Connection.RemoteIpAddress = proxy;
            request.Request.Scheme = "http";
            request.Request.Headers["X-Forwarded-For"] = "198.51.100.1";
            request.Request.Headers["X-Forwarded-Proto"] = "https";
            await forwarding.Invoke(request);
            return request;
        }
        var trusted = await Forward(IPAddress.Loopback);
        check(trusted.Request.IsHttps && trusted.Connection.RemoteIpAddress?.ToString() == "198.51.100.1",
            "restricted loopback proxy forwarding preserves original HTTPS and client address");
        var unknown = await Forward(IPAddress.Parse("192.0.2.1"));
        check(!unknown.Request.IsHttps && unknown.Connection.RemoteIpAddress?.ToString() == "192.0.2.1",
            "unknown proxy headers cannot spoof HTTPS or client identity on .NET 10");
    }
}
