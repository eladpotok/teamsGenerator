using System.Text.Json;
using Azure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TeamsGeneratorWebAPI.Authentication;
using TeamsGeneratorWebAPI.OwnerEmail;

namespace TeamsGeneratorWebAPI.Controllers;

[ApiController]
[Authorize]
[Route("api/feedback")]
public sealed class FeedbackController(OwnerEmailService email) : ControllerBase
{
    public sealed record FeedbackRequest(string? Category, string? Message);

    [HttpGet("capabilities")]
    public IActionResult Capabilities()
    {
        Response.Headers.CacheControl = "no-store";
        return VerifiedUid() == null ? Unauthorized(new { code = "feedback_sign_in_required" })
            : Ok(new { enabled = email.FeedbackEnabled, ownerName = OwnerEmailOptions.OwnerName });
    }

    [HttpPost]
    [RequestSizeLimit(24 * 1024)]
    public async Task<IActionResult> Send([FromBody] FeedbackRequest body, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var uid = VerifiedUid();
        if (uid == null) return Unauthorized(new { code = "feedback_sign_in_required" });
        try
        {
            var header = Request.Headers["Idempotency-Key"];
            if (header.Count > 1) return BadRequest(new { code = "feedback_invalid_idempotency_key" });
            await email.AcceptFeedbackAsync(uid, RequestUserId.ResolveVerifiedEmail(User), body.Category,
                body.Message, header.Count == 0 ? null : header.ToString(), ct);
            return Accepted(new { accepted = true, status = "queued", ownerName = OwnerEmailOptions.OwnerName });
        }
        catch (OwnerEmailException ex)
        {
            if (ex.RetryAfter.HasValue) Response.Headers.RetryAfter = ex.RetryAfter.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return StatusCode(ex.Status, new { code = ex.Code, retryAfter = ex.RetryAfter });
        }
        catch (RequestFailedException)
        {
            return StatusCode(503, new { code = "feedback_storage_unavailable" });
        }
    }

    private string? VerifiedUid()
    {
        var uid = RequestUserId.Resolve(User, "");
        if (!RequestUserId.IsAuthenticatedOrganizer(User, uid)) return null;
        if (User.FindFirst("firebase")?.Value is { } firebase)
        {
            try
            {
                using var claim = JsonDocument.Parse(firebase);
                if (claim.RootElement.ValueKind != JsonValueKind.Object) return null;
                if (claim.RootElement.TryGetProperty("sign_in_provider", out var provider)
                    && (provider.ValueKind != JsonValueKind.String || provider.GetString() == "anonymous")) return null;
            }
            catch (JsonException) { return null; }
        }
        return uid;
    }
}
