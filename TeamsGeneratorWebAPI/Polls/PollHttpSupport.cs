using Azure;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Caching.Memory;

namespace TeamsGeneratorWebAPI.Polls;

public sealed class PollExceptionFilter : IExceptionFilter, IActionFilter
{
    public void OnException(ExceptionContext context)
    {
        var error = context.Exception switch
        {
            PollException poll => poll,
            RequestFailedException => new PollException(503, "poll_storage_unavailable", "Poll storage is unavailable. Please retry."),
            _ => null
        };
        if (error == null) return;
        context.Result = new ObjectResult(new { errorCode = error.ErrorCode, message = error.Message }) { StatusCode = error.StatusCode };
        if (error.StatusCode is 429 or 503) context.HttpContext.Response.Headers.RetryAfter = "60";
        context.ExceptionHandled = true;
    }

    public void OnActionExecuting(ActionExecutingContext context)
    {
        context.HttpContext.Response.Headers.CacheControl = "no-store";
        if (!context.ModelState.IsValid)
            context.Result = new BadRequestObjectResult(new { errorCode = "invalid_request", message = "The request body is invalid." });
    }
    public void OnActionExecuted(ActionExecutedContext context) { }
}

public sealed class PollPublicRateLimiter : IDisposable
{
    private sealed class Counter { public int Count; }
    private readonly MemoryCache cache = new(new MemoryCacheOptions { SizeLimit = 20000 });
    private readonly object gate = new();

    public void Check(string? address, bool write, bool issuing)
    {
        var key = (issuing ? "issue:" : write ? "write:" : "read:") + PollKeys.Hash(address ?? "unknown");
        var limit = issuing ? 12 : write ? 60 : 600;
        lock (gate)
        {
            if (!cache.TryGetValue(key, out Counter? counter))
            {
                counter = new();
                cache.Set(key, counter, new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1) });
            }
            if (++counter!.Count > limit)
                throw new PollException(429, "rate_limited", "Too many requests. Retry in one minute.");
        }
    }
    public void Dispose() => cache.Dispose();
}

public sealed class PollTelemetryRedactor : ITelemetryInitializer
{
    public void Initialize(ITelemetry telemetry)
    {
        if (telemetry is not RequestTelemetry request || request.Url == null) return;
        var path = request.Url.AbsolutePath;
        if (!path.StartsWith("/api/polls/", StringComparison.OrdinalIgnoreCase)
            && !path.StartsWith("/api/matchday-polls/", StringComparison.OrdinalIgnoreCase)) return;
        request.Url = new UriBuilder(request.Url) { Path = "/api/polls/[redacted]", Query = "" }.Uri;
        request.Name = "Poll request";
        request.Context.Operation.Name = request.Name;
        request.Context.Location.Ip = "0.0.0.0";
    }
}
