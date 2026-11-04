using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TeamsGeneratorWebAPI.Announcements;

namespace TeamsGeneratorWebAPI.Controllers;

[ApiController]
[Authorize]
[Route("api/announcements")]
[ServiceFilter(typeof(AnnouncementExceptionFilter))]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AnnouncementsController(AccountAnnouncementService announcements) : ControllerBase
{
    [HttpGet]
    public Task<AnnouncementFeed> Feed(CancellationToken ct) => announcements.FeedAsync(User, ct);

    [HttpPost("{id}/dismiss")]
    public async Task<IActionResult> Dismiss(string id, CancellationToken ct)
    {
        await announcements.DismissAsync(User, id, ct);
        return NoContent();
    }

    [HttpGet("admin")]
    public Task<IReadOnlyList<AnnouncementAdminMessage>> List(CancellationToken ct) => announcements.ListAsync(User, ct);

    [HttpPost("admin")]
    [RequestSizeLimit(24 * 1024)]
    public async Task<ActionResult<AnnouncementAdminMessage>> Publish(PublishAnnouncementRequest body, CancellationToken ct)
        => Ok(await announcements.PublishAsync(User, body, ct));

    [HttpPost("admin/{id}/withdraw")]
    public async Task<IActionResult> Withdraw(string id, CancellationToken ct)
    {
        await announcements.WithdrawAsync(User, id, ct);
        return NoContent();
    }
}
