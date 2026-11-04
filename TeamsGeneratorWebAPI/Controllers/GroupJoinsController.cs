using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TeamsGeneratorWebAPI.Authentication;
using TeamsGeneratorWebAPI.GroupJoins;
using TeamsGeneratorWebAPI.Invitations;
using TeamsGeneratorWebAPI.PlayerProfiles;

namespace TeamsGeneratorWebAPI.Controllers;

[ApiController]
[Route("[controller]")]
[Authorize]
[ServiceFilter(typeof(InvitationExceptionFilter))]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class GroupJoinsController : ControllerBase
{
    private readonly GroupJoinService _joins;
    private readonly InvitationPublicRateLimiter _limiter;
    public GroupJoinsController(GroupJoinService joins, InvitationPublicRateLimiter limiter) => (_joins, _limiter) = (joins, limiter);

    [HttpGet("Link")]
    public async Task<IActionResult> Link(string groupId, CancellationToken ct) =>
        Ok(await _joins.LinkAsync(UserId(), groupId, false, ct));

    [HttpPost("Link")]
    public async Task<IActionResult> CreateLink(string groupId, CancellationToken ct) =>
        Ok(await _joins.LinkAsync(UserId(), groupId, true, ct));

    [HttpDelete("Link")]
    public async Task<IActionResult> RevokeLink(string groupId, CancellationToken ct)
    {
        await _joins.RevokeAsync(UserId(), groupId, ct);
        return Ok(new { success = true });
    }

    [HttpGet("Requests")]
    public async Task<IActionResult> Requests(string groupId, CancellationToken ct) =>
        Ok(new { requests = await _joins.RequestsAsync(UserId(), groupId, ct) });

    [HttpPost("Requests/{id}/Analyze")]
    public async Task<IActionResult> Analyze(string groupId, string id, GroupJoinReviewVersion input, CancellationToken ct)
    {
        var user = UserId();
        _limiter.Check("group-join-ai", null, user);
        return Ok(await _joins.AnalyzeAsync(user, groupId, id, input.Revision, ct));
    }

    [HttpPost("Requests/{id}/Approve")]
    public async Task<IActionResult> Approve(string groupId, string id, GroupJoinDecision input, CancellationToken ct) =>
        Ok(await _joins.ApproveAsync(UserId(), groupId, id, input, ct));

    [HttpPost("Requests/{id}/Reject")]
    public async Task<IActionResult> Reject(string groupId, string id, GroupJoinReviewVersion input, CancellationToken ct) =>
        Ok(await _joins.RejectAsync(UserId(), groupId, id, input.Revision, ct));

    [AllowAnonymous]
    [HttpGet("Public/{token}")]
    public async Task<IActionResult> Public(string token, CancellationToken ct)
    {
        PublicHeaders();
        _limiter.Check("group-join:" + token, HttpContext.Connection.RemoteIpAddress?.ToString(), null);
        return Ok(await _joins.PublicAsync(token, ct));
    }

    [HttpPost("Public/{token}/Requests")]
    [RequestSizeLimit(16384)]
    public async Task<IActionResult> Submit(string token, ProfileGroupJoinSubmission input, CancellationToken ct)
    {
        PublicHeaders();
        _limiter.Check("group-join-ip", null, HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
        _limiter.Check("group-join-token", null, token);
        var user = PlayerProfileService.Account(User);
        _limiter.Check("group-join-account", null, user);
        await _joins.SubmitProfileAsync(user, token, input, ct);
        return Ok(new { success = true });
    }

    private void PublicHeaders()
    {
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
    }
    private string UserId()
    {
        var id = RequestUserId.ResolveOptional(User, null);
        return User.Identity?.IsAuthenticated == true && !string.IsNullOrWhiteSpace(id)
            ? id : throw new InvitationException(401, "Authentication is required.");
    }
}
