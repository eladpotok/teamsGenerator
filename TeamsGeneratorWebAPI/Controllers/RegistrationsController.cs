using Azure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TeamsGeneratorWebAPI.Authentication;
using TeamsGeneratorWebAPI.Telemetry;

namespace TeamsGeneratorWebAPI.Controllers;

public sealed record RegistrationObservationRequest(DateTimeOffset CreatedAt, string Method);

[ApiController]
[Route("api/registrations")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public sealed class RegistrationsController : ControllerBase
{
    private readonly IRegistrationStore _store;
    private readonly IUsageTelemetry _telemetry;
    private readonly TimeProvider _clock;
    private readonly ILogger<RegistrationsController> _logger;

    public RegistrationsController(IRegistrationStore store, IUsageTelemetry telemetry,
        TimeProvider clock, ILogger<RegistrationsController> logger)
    {
        _store = store;
        _telemetry = telemetry;
        _clock = clock;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> Post([FromBody] RegistrationObservationRequest request,
        CancellationToken cancellationToken)
    {
        var userId = RequestUserId.Resolve(User, string.Empty);
        if (!RequestUserId.IsAuthenticatedOrganizer(User, userId)) return Unauthorized();
        var now = _clock.GetUtcNow();
        if (request == null || request.Method is not ("email_password" or "google_redirect")
            || request.CreatedAt < DateTimeOffset.UnixEpoch || request.CreatedAt > now.AddMinutes(5))
            return BadRequest(new { code = "invalid_registration_observation" });
        if (UsageMetrics.IsVerificationClient(Request.Headers["client_version"]))
            return Ok(new { recorded = false, excluded = true });

        RegistrationFact fact;
        try
        {
            fact = await _store.GetOrCreateAsync(new RegistrationFact(
                UsageMetrics.ResourceKey("registration", userId), request.CreatedAt.ToUniversalTime(),
                now, request.Method), cancellationToken);
        }
        catch (RequestFailedException exception)
        {
            _logger.LogWarning("Registration observation storage failed with status {Status}.", exception.Status);
            Response.Headers["Retry-After"] = "60";
            return StatusCode(503, new { code = "registration_storage_unavailable" });
        }

        // Re-project on retry, even after a prior insert: an insert is not an ingestion receipt.
        // Dashboards deduplicate by registration_key, never by raw event count.
        _telemetry.Track("AccountRegistrationObserved", userId: userId,
            properties: new Dictionary<string, string?>
            {
                ["registration_version"] = "1",
                ["registration_key"] = fact.AccountKey,
                ["created_at"] = fact.CreatedAt.ToString("O"),
                ["first_reported_at"] = fact.FirstReportedAt.ToString("O"),
                ["registration_method"] = fact.Method,
                ["registration_source"] = "firebase_client_new_user",
                ["outcome"] = "succeeded"
            });
        return Ok(new { recorded = true });
    }
}
