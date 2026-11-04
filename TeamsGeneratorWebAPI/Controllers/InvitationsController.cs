using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TeamsGeneratorWebAPI.Authentication;
using TeamsGeneratorWebAPI.Invitations;
using TeamsGeneratorWebAPI.Telemetry;
using TeamsGeneratorWebAPI.PlayerProfiles;

namespace TeamsGeneratorWebAPI.Controllers;

[ApiController]
[Route("[controller]")]
[Authorize]
[ServiceFilter(typeof(InvitationExceptionFilter))]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class InvitationsController : ControllerBase
{
    private readonly InvitationService _invitations;
    private readonly InvitationPublicRateLimiter _limiter;
    private readonly IUsageTelemetry? _telemetry;
    public InvitationsController(InvitationService invitations, InvitationPublicRateLimiter limiter,
        IUsageTelemetry? telemetry = null)
        => (_invitations, _limiter, _telemetry) = (invitations, limiter, telemetry);

    [HttpPost]
    public async Task<ActionResult<OrganizerInvitation>> Create(CreateInvitationRequest request, CancellationToken ct) =>
        Ok(await _invitations.CreateAsync(AuthenticatedUser(), request, ct));

    [HttpGet("Current")]
    public async Task<IActionResult> Current([FromQuery] string groupId, [FromQuery] string scheduledAt, CancellationToken ct) =>
        Ok(new { invitation = await _invitations.CurrentAsync(AuthenticatedUser(), groupId, scheduledAt, ct) });

    [HttpGet("{id}")]
    public async Task<ActionResult<OrganizerInvitation>> GetById(string id, CancellationToken ct) =>
        Ok(await _invitations.GetForRecipientAsync(AuthenticatedUser(), id, ct));

    [HttpDelete("Current")]
    public async Task<IActionResult> Revoke([FromQuery] string groupId, [FromQuery] string scheduledAt, CancellationToken ct)
    {
        await _invitations.RevokeAsync(AuthenticatedUser(), groupId, scheduledAt, ct);
        return Ok(new { success = true });
    }

    [AllowAnonymous]
    [HttpGet("Public/{token}")]
    public async Task<ActionResult<PublicInvitation>> Public(string token, CancellationToken ct)
    {
        PublicHeaders();
        var outcome = "failed";
        try
        {
            _limiter.Check(token, HttpContext.Connection.RemoteIpAddress?.ToString(), null);
            var result = await _invitations.PublicAsync(token, ct);
            outcome = "succeeded";
            return Ok(result);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            outcome = "canceled";
            throw;
        }
        finally
        {
            // These are HTTP read attempts, including polling and automated clients, not unique visitors.
            _telemetry?.Track("InvitationPublicReadRequested", properties: new Dictionary<string, string?>
                { ["actor_type"] = "participant", ["outcome"] = outcome });
        }
    }

    [AllowAnonymous]
    [HttpPost("Public/{token}/Responses")]
    [RequestSizeLimit(4096)]
    public async Task<ActionResult<PublicInvitation>> Respond(string token, InvitationResponseRequest request, CancellationToken ct)
    {
        PublicHeaders();
        _limiter.Check(token, HttpContext.Connection.RemoteIpAddress?.ToString(), null);
        _limiter.Check(token, null, request.PlayerKey ?? "");
        return Ok(await _invitations.RespondAsync(token, request, ct));
    }

    [HttpGet("Public/{token}/Me")]
    public async Task<ActionResult<OwnInvitation>> Own(string token, CancellationToken ct)
    {
        PublicHeaders();
        var user = PlayerProfileService.Account(User);
        _limiter.Check(token, HttpContext.Connection.RemoteIpAddress?.ToString(), user);
        return Ok(await _invitations.OwnAsync(user, token, ct));
    }

    [HttpPost("Public/{token}/Me/Responses")]
    [RequestSizeLimit(4096)]
    public async Task<ActionResult<PublicInvitation>> RespondOwn(string token, OwnInvitationResponseRequest request, CancellationToken ct)
    {
        PublicHeaders();
        var user = PlayerProfileService.Account(User);
        _limiter.Check(token, HttpContext.Connection.RemoteIpAddress?.ToString(), user);
        return Ok(await _invitations.RespondOwnAsync(user, token, request, ct));
    }

    private void PublicHeaders()
    {
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
    }

    private string AuthenticatedUser()
    {
        var id = RequestUserId.Resolve(User, "");
        return User.Identity?.IsAuthenticated == true && !string.IsNullOrWhiteSpace(id)
            ? id : throw new InvitationException(401, "Authentication is required.");
    }
}
