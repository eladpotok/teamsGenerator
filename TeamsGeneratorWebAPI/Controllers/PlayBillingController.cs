using System.Text.Json;
using Azure;
using Google.Apis.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TeamsGeneratorWebAPI.Authentication;
using TeamsGeneratorWebAPI.Premium;

namespace TeamsGeneratorWebAPI.Controllers;

[ApiController]
[Route("api/billing/google")]
public sealed class PlayBillingController : ControllerBase
{
    private readonly PlayPurchaseRecovery _recovery;
    private readonly BillingOptions _options;
    public PlayBillingController(PlayPurchaseRecovery recovery, IOptions<BillingOptions> options)
        => (_recovery, _options) = (recovery, options.Value);

    public sealed record ReceiptRequest(string PurchaseToken);
    public sealed record ResolutionRequest(string ReceiptKey);

    [Authorize]
    [HttpPost("recover")]
    [RequestSizeLimit(8192)]
    public Task<IActionResult> Recover([FromBody] ReceiptRequest body, CancellationToken ct) => Execute(async () =>
    {
        var uid = VerifiedUid();
        var receipt = await _recovery.ReceiveAsync(body.PurchaseToken, ct);
        receipt = await _recovery.ProcessAsync(receipt, ct);
        // Recover the rightful owner even when another Teamify user is currently signed in.
        // Never disclose that owner's identifier, status, or expiry to the caller.
        if (receipt.AccountKey != TableBillingStore.AccountKey(uid))
            return Conflict(new { code = "billing_ownership_conflict" });
        return Ok(new { recovered = true, pending = receipt.State == "pending", providerSyncPending = receipt.MirrorError != null });
    });

    [Authorize]
    [HttpPost("resolution")]
    [RequestSizeLimit(1024)]
    public Task<IActionResult> Resolution([FromBody] ResolutionRequest body, CancellationToken ct) => Execute(async () =>
        Ok(new { resolved = await _recovery.IsResolvedAsync(VerifiedUid(), body.ReceiptKey, ct) }));

    private string VerifiedUid()
    {
        var uid = RequestUserId.Resolve(User, "");
        var anonymous = false;
        if (User.FindFirst("firebase")?.Value is { } firebase)
        {
            using var claim = JsonDocument.Parse(firebase);
            anonymous = claim.RootElement.TryGetProperty("sign_in_provider", out var provider)
                && provider.GetString() == "anonymous";
        }
        if (!RequestUserId.IsAuthenticatedOrganizer(User, uid) || anonymous)
            throw new BillingException(401, "billing_sign_in_required", "Sign in to your Teamify account.");
        return uid;
    }

    [AllowAnonymous]
    [HttpPost("notifications")]
    [RequestSizeLimit(16 * 1024)]
    public Task<IActionResult> Notifications(CancellationToken ct) => Execute(async () =>
    {
        _recovery.RequireEnabled();
        if (!Request.IsHttps) return BadRequest();
        var header = Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.Ordinal)) return Unauthorized();
        GoogleJsonWebSignature.Payload identity;
        try
        {
            identity = await GoogleJsonWebSignature.ValidateAsync(header[7..],
                new GoogleJsonWebSignature.ValidationSettings { Audience = new[] { _options.GooglePushAudience } });
        }
        catch (InvalidJwtException) { return Unauthorized(); }
        if (identity.Email != _options.GooglePushServiceAccount || !identity.EmailVerified) return Unauthorized();
        using var document = await JsonDocument.ParseAsync(Request.Body, new JsonDocumentOptions { MaxDepth = 8 }, ct);
        if (!document.RootElement.TryGetProperty("message", out var message)
            || !message.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.String)
            return BadRequest();
        byte[] decoded;
        try { decoded = Convert.FromBase64String(data.GetString()!); }
        catch (FormatException) { return BadRequest(); }
        if (decoded.Length > 8192) return BadRequest();
        using var notification = JsonDocument.Parse(decoded, new JsonDocumentOptions { MaxDepth = 8 });
        if (notification.RootElement.GetProperty("packageName").GetString() != "teams.generator.new") return BadRequest();
        if (notification.RootElement.TryGetProperty("testNotification", out _)) return Ok();
        if (!notification.RootElement.TryGetProperty("subscriptionNotification", out var subscription))
            return Ok(new { ignored = true });
        var token = subscription.GetProperty("purchaseToken").GetString()!;
        if (subscription.TryGetProperty("notificationType", out var type) && type.TryGetInt32(out var kind) && kind == 4
            && notification.RootElement.TryGetProperty("eventTimeMillis", out var eventTime)
            && eventTime.ValueKind == JsonValueKind.String && long.TryParse(eventTime.GetString(), out var millis)
            && millis >= 0 && millis <= DateTimeOffset.MaxValue.ToUnixTimeMilliseconds())
            await _recovery.ReceiveNewPurchaseNotificationAsync(token, DateTimeOffset.FromUnixTimeMilliseconds(millis), ct);
        else
            await _recovery.ReceiveAsync(token, ct);
        return Ok(new { accepted = true });
    });

    private async Task<IActionResult> Execute(Func<Task<IActionResult>> action)
    {
        Response.Headers.CacheControl = "no-store";
        try { return await action(); }
        catch (BillingException ex) { return StatusCode(ex.Status, new { code = ex.Code }); }
        catch (RequestFailedException) { return StatusCode(503, new { code = "billing_storage_unavailable" }); }
        catch (JsonException) { return BadRequest(new { code = "billing_invalid_json" }); }
        catch (KeyNotFoundException) { return BadRequest(new { code = "billing_invalid_json" }); }
    }
}
