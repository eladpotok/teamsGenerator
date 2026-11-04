using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TeamsGeneratorWebAPI.Telemetry;

namespace TeamsGeneratorWebAPI.Controllers;

[ApiController]
[Route("api/website-visits")]
public sealed class WebsiteVisitsController : ControllerBase
{
    private readonly IUsageTelemetry _telemetry;
    private readonly WebsiteVisitRateLimiter _limiter;

    public WebsiteVisitsController(IUsageTelemetry telemetry, WebsiteVisitRateLimiter limiter)
    {
        _telemetry = telemetry;
        _limiter = limiter;
    }

    [HttpPost]
    [AllowAnonymous]
    [RequestSizeLimit(2048)]
    public IActionResult Post([FromBody] WebsiteVisitRequest request)
    {
        Response.Headers.CacheControl = "no-store";
        var properties = WebsiteAttribution.Properties(request);
        if (properties == null) return BadRequest(new { code = "invalid_website_visit" });
        if (!_limiter.Allow(HttpContext.Connection.RemoteIpAddress?.ToString()))
        {
            Response.Headers.RetryAfter = "60";
            return StatusCode(429, new { code = "website_visit_rate_limited" });
        }
        if (UsageMetrics.IsVerificationClient(Request.Headers["client_version"])
            || Request.Headers["X-Teamify-Readiness"] == "1")
            return Ok(new { recorded = false, excluded = true });

        properties["actor_type"] = "visitor";
        properties["traffic_type"] = WebsiteVisitRateLimiter.IsAutomated(Request.Headers.UserAgent)
            ? "automated" : "browser";
        properties["outcome"] = "succeeded";
        _telemetry.Track("WebsiteVisitObserved", properties: properties);
        return Ok(new { recorded = true });
    }
}
