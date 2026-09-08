using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TeamsGeneratorWebAPI.Authentication;
using TeamsGeneratorWebAPI.Collaboration;
using TeamsGeneratorWebAPI.PlayersBlob;

namespace TeamsGeneratorWebAPI.Controllers;

[Authorize]
[ApiController]
[Route("[controller]")]
public sealed class CollaborationController : ControllerBase
{
    private readonly IGroupCollaborationService _collaboration;
    private readonly IPlayersStorageBlobConnector _players;

    public CollaborationController(
        IGroupCollaborationService collaboration,
        IPlayersStorageBlobConnector players)
    {
        _collaboration = collaboration;
        _players = players;
    }

    [HttpPost("Invitations")]
    public async Task<IActionResult> CreateInvitation(
        [FromBody] CreateGroupInvitationRequest request)
    {
        var ownerId = RequestUserId.Resolve(User, string.Empty);
        var groups = await _players.ListGroupsAsync(ownerId);
        var group = groups.Groups.FirstOrDefault(item =>
            item.Id == request.GroupId);
        if (group == null)
        {
            return NotFound(new { error = "Player group was not found." });
        }
        return Ok(await _collaboration.CreateInvitationAsync(
            ownerId,
            group.Id,
            group.Name));
    }

    [HttpPost("Invitations/Redeem")]
    public async Task<IActionResult> RedeemInvitation(
        [FromBody] RedeemGroupInvitationRequest request)
    {
        try
        {
            var memberId = RequestUserId.Resolve(User, string.Empty);
            var memberName =
                User.FindFirstValue("name")
                ?? User.FindFirstValue(ClaimTypes.Name)
                ?? "Co-organizer";
            return Ok(await _collaboration.RedeemInvitationAsync(
                memberId,
                memberName,
                request.Token));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { error = exception.Message });
        }
    }
}

public sealed class CreateGroupInvitationRequest
{
    public string GroupId { get; set; }
}

public sealed class RedeemGroupInvitationRequest
{
    public string Token { get; set; }
}
