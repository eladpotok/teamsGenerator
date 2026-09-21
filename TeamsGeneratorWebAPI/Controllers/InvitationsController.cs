using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TeamsGeneratorWebAPI.Authentication;
using TeamsGeneratorWebAPI.Invitations;

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
    public InvitationsController(InvitationService invitations, InvitationPublicRateLimiter limiter)
        => (_invitations, _limiter) = (invitations, limiter);

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
        _limiter.Check(token, HttpContext.Connection.RemoteIpAddress?.ToString(), null);
        return Ok(await _invitations.PublicAsync(token, ct));
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
