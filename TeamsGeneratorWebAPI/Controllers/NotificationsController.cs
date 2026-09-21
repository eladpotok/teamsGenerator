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
public sealed class NotificationsController : ControllerBase
{
    private readonly NotificationAccountService _accounts;
    private readonly IInvitationPushSender _sender;
    public NotificationsController(NotificationAccountService accounts, IInvitationPushSender sender) =>
        (_accounts, _sender) = (accounts, sender);

    [HttpGet("Preferences")]
    public async Task<ActionResult<NotificationPreferencesResponse>> Preferences(CancellationToken ct)
    {
        var preferences = (await _accounts.GetAsync(AuthenticatedUser(), ct)).Preferences;
        return Ok(new NotificationPreferencesResponse(preferences.Enabled, preferences.PlayerThreshold,
            preferences.IntervalMinutes, await _sender.IsDeliveryConfiguredAsync(ct)));
    }

    [HttpPut("Preferences")]
    public async Task<ActionResult<NotificationPreferences>> SetPreferences(NotificationPreferences preferences, CancellationToken ct) =>
        Ok(await _accounts.SetPreferencesAsync(AuthenticatedUser(), preferences, ct));

    [HttpPost("Devices")]
    [RequestSizeLimit(8192)]
    public async Task<IActionResult> Register(DeviceRequest device, CancellationToken ct)
    {
        await _accounts.RegisterAsync(AuthenticatedUser(), device, ct);
        return Ok(new { success = true });
    }

    [HttpDelete("Devices")]
    [RequestSizeLimit(8192)]
    public async Task<IActionResult> Remove([FromBody] DeviceRequest device, CancellationToken ct)
    {
        await _accounts.RemoveAsync(AuthenticatedUser(), device.Token, ct);
        return Ok(new { success = true });
    }

    private string AuthenticatedUser()
    {
        var id = RequestUserId.Resolve(User, "");
        return User.Identity?.IsAuthenticated == true && !string.IsNullOrWhiteSpace(id)
            ? id : throw new InvitationException(401, "Authentication is required.");
    }
}
