using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TeamsGeneratorWebAPI.Invitations;
using TeamsGeneratorWebAPI.PlayerProfiles;

namespace TeamsGeneratorWebAPI.Controllers;

[ApiController]
[Authorize]
[Route("[controller]")]
[ServiceFilter(typeof(InvitationExceptionFilter))]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PlayerProfileController(PlayerProfileService profiles, InvitationPublicRateLimiter limiter,
    PlayerPerformanceService performance, PlayerMembershipService memberships, IInvitationGroupSource groups) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Ensure(CancellationToken ct)
    {
        limiter.Check("player-profile", null, PlayerProfileService.Account(User));
        return Ok(await profiles.EnsureAsync(User, ct));
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct) =>
        Ok(await profiles.GetAsync(PlayerProfileService.Account(User), ct));

    [HttpGet("Groups")]
    public async Task<IActionResult> Groups(CancellationToken ct)
    {
        var uid = PlayerProfileService.Account(User);
        limiter.Check("player-profile-groups", null, uid);
        return Ok(await performance.GetAsync(uid, ct));
    }

    [HttpPut]
    [RequestSizeLimit(750000)]
    public async Task<IActionResult> Update(PlayerProfileUpdate input, CancellationToken ct)
    {
        limiter.Check("player-profile", null, PlayerProfileService.Account(User));
        return Ok(await profiles.UpdateAsync(User, input, ct));
    }

    [HttpGet("Memberships")]
    public async Task<IActionResult> Memberships(CancellationToken ct)
    {
        var uid = PlayerProfileService.Account(User);
        limiter.Check("player-profile-memberships", null, uid);
        return Ok(await memberships.SummariesAsync(uid, groups, ct));
    }

    [HttpPut("Mode")]
    public async Task<IActionResult> SetMode(AccountModeUpdate input, CancellationToken ct)
    {
        limiter.Check("player-profile", null, PlayerProfileService.Account(User));
        return Ok(await profiles.SetModeAsync(User, input, ct));
    }
}
