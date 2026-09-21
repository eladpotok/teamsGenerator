using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Caching.Memory;

namespace TeamsGeneratorWebAPI.Invitations;

public sealed class InvitationExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        if (context.Exception is not InvitationException error) return;
        context.Result = new ObjectResult(new { error = error.Message }) { StatusCode = error.StatusCode };
        context.ExceptionHandled = true;
    }
}

public sealed class InvitationPublicRateLimiter : IDisposable
{
    private sealed class Counter { public int Count; }
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 20000 });
    private readonly object _gate = new();
    public void Check(string token, string? address, string? playerKey)
    {
        // Household/NAT clients share a generous read allowance; write throttles are per player.
        var key = InvitationKeys.Hash(token + "\n" + (playerKey == null ? address : playerKey));
        var limit = playerKey == null ? 600 : 30;
        lock (_gate)
        {
            if (!_cache.TryGetValue(key, out Counter? counter))
            {
                counter = new Counter();
                _cache.Set(key, counter, new MemoryCacheEntryOptions
                    { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1), Size = 1 });
            }
            if (++counter!.Count > limit) throw new InvitationException(429, "Too many requests. Retry in one minute.");
        }
    }
    public void Dispose() => _cache.Dispose();
}

public sealed class InvitationTelemetryRedactor : ITelemetryInitializer
{
    public void Initialize(ITelemetry telemetry)
    {
        if (telemetry is not RequestTelemetry request || request.Url == null) return;
        const string route = "/Invitations/Public/";
        var index = request.Url.AbsolutePath.IndexOf(route, StringComparison.OrdinalIgnoreCase);
        if (index < 0) return;
        var builder = new UriBuilder(request.Url) { Path = request.Url.AbsolutePath[..index] + route + "[redacted]", Query = "" };
        request.Url = builder.Uri;
        request.Name = request.Name.Contains("Responses", StringComparison.OrdinalIgnoreCase)
            ? "POST Invitations/Public/[redacted]/Responses" : "GET Invitations/Public/[redacted]";
        request.Context.Operation.Name = request.Name;
    }
}
