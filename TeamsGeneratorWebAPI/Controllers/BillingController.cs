using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TeamsGeneratorWebAPI.Authentication;
using TeamsGeneratorWebAPI.Premium;

namespace TeamsGeneratorWebAPI.Controllers;

[ApiController]
[Route("api/billing")]
public sealed class BillingController : ControllerBase
{
    private readonly BillingService _billing;
    private readonly IBillingStore _store;
    private readonly IAccountEntitlementService _entitlements;
    private readonly BillingOptions _options;
    public BillingController(BillingService billing, IBillingStore store, IAccountEntitlementService entitlements,
        IOptions<BillingOptions> options) => (_billing, _store, _entitlements, _options) = (billing, store, entitlements, options.Value);

    [Authorize]
    [HttpPost("binding")]
    public Task<IActionResult> Binding(CancellationToken ct) => Execute(async () =>
    {
        var account = await _billing.BindAsync(VerifiedUid(), ct);
        return Ok(new { appUserId = account.AppUserId, playAccountId = TableBillingStore.Hash(account.AppUserId),
            environment = _options.Environment, entitlement = "teamify_premium", offering = "default",
            checkoutEnabled = _options.CheckoutEnabled, recoveryEnabled = _options.RecoveryEnabled });
    });

    [Authorize]
    [HttpGet("status")]
    public Task<IActionResult> Status(CancellationToken ct) => Execute(async () =>
        Ok(await StatusBody(await _billing.ReadAsync(VerifiedUid(), ct), ct)));

    [Authorize]
    [HttpPost("sync")]
    public Task<IActionResult> Sync(CancellationToken ct) => Execute(async () =>
    {
        _billing.RequireEnabled();
        var account = await _billing.ReadAsync(VerifiedUid(), ct)
            ?? throw new BillingException(409, "billing_binding_missing", "Open subscription options before restoring a purchase.");
        return Ok(await StatusBody(account, ct));
    });

    private async Task<object> StatusBody(BillingAccount? account, CancellationToken ct)
    {
        var paidActive = _billing.IsActive(account, DateTimeOffset.UtcNow);
        return new
        {
            enabled = _billing.Enabled,
            paidActive,
            state = account?.Status ?? "inactive",
            expiresAt = account?.ExpiresAt,
            verifiedAt = account?.VerifiedAt,
            environment = _options.Environment,
            entitlements = await _entitlements.GetAsync(
                VerifiedUid(), RequestUserId.ResolveVerifiedEmail(User), ct, paidActive: paidActive)
        };
    }

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
            throw new BillingException(401, "billing_sign_in_required", "Sign in to your Teamify account first.");
        return uid;
    }

    // Separate admission path: provider traffic must not share a user/IP write-capacity bucket.
    [AllowAnonymous]
    [HttpPost("revenuecat/webhook")]
    [RequestSizeLimit(64 * 1024)]
    public Task<IActionResult> Webhook(CancellationToken ct) => Execute(async () =>
    {
        _billing.RequireEnabled();
        if (!Request.IsHttps) return BadRequest(new { code = "billing_https_required" });
        if (!ValidAuthorization(Request.Headers.Authorization.ToString(), _options.WebhookAuthorization))
            return Unauthorized();
        using var body = new MemoryStream();
        var buffer = new byte[4096];
        int read;
        while ((read = await Request.Body.ReadAsync(buffer.AsMemory(), ct)) > 0)
        {
            if (body.Length + read > 64 * 1024) return StatusCode(413);
            await body.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        body.Position = 0;
        using var json = await JsonDocument.ParseAsync(body, new JsonDocumentOptions { MaxDepth = 16 }, ct);
        if (!json.RootElement.TryGetProperty("event", out var ev) || ev.ValueKind != JsonValueKind.Object)
            return BadRequest(new { code = "billing_invalid_event" });
        string? Text(string key) => ev.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        var eventId = Text("id");
        var type = Text("type");
        if (string.IsNullOrWhiteSpace(eventId) || eventId.Length > 256 || string.IsNullOrEmpty(type))
            return BadRequest(new { code = "billing_invalid_event" });
        if (Text("app_id") != _options.AppId) return BadRequest(new { code = "billing_wrong_app" });
        if (type != "TRANSFER" && Text("environment") != _options.Environment)
            return Ok(new { ignored = true });
        var ids = new HashSet<string>(StringComparer.Ordinal);
        void Add(string? id) { if (!string.IsNullOrEmpty(id) && id.Length <= 256) ids.Add(id); }
        Add(Text("app_user_id"));
        Add(Text("original_app_user_id"));
        foreach (var field in new[] { "aliases", "transferred_from", "transferred_to" })
            if (ev.TryGetProperty(field, out var array) && array.ValueKind == JsonValueKind.Array)
                foreach (var id in array.EnumerateArray())
                    if (id.ValueKind == JsonValueKind.String) Add(id.GetString());
        if (ids.Count > 128) return BadRequest(new { code = "billing_invalid_event" });
        await _store.AcceptAsync(new BillingInbox
        {
            RowKey = TableBillingStore.Hash(eventId),
            CustomerIdsJson = JsonSerializer.Serialize(type == "TEST" ? Array.Empty<string>() : ids.ToArray()),
            NewPurchaseOrder = type == "INITIAL_PURCHASE" && Text("environment") == "PRODUCTION"
                && Text("store") == "PLAY_STORE" && Text("period_type") == "NORMAL"
                && Text("transaction_id") is { Length: <= 256 } transaction ? transaction : null,
            NewPurchaseAt = ev.TryGetProperty("purchased_at_ms", out var purchased)
                && purchased.ValueKind == JsonValueKind.Number && purchased.TryGetInt64(out var milliseconds) && milliseconds >= 0
                && milliseconds <= DateTimeOffset.MaxValue.ToUnixTimeMilliseconds()
                    ? DateTimeOffset.FromUnixTimeMilliseconds(milliseconds) : null
        }, ct);
        return Ok(new { accepted = true });
    });

    public static bool ValidAuthorization(string actual, string expected) => expected.Length >= 32
        && CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(actual)),
            SHA256.HashData(Encoding.UTF8.GetBytes(expected)));

    private async Task<IActionResult> Execute(Func<Task<IActionResult>> operation)
    {
        Response.Headers.CacheControl = "no-store";
        try { return await operation(); }
        catch (BillingException ex) { return StatusCode(ex.Status, new { code = ex.Code, message = ex.Message }); }
        catch (RequestFailedException) { return StatusCode(503, new { code = "billing_storage_unavailable", message = "Purchase verification is unavailable. Please retry." }); }
        catch (JsonException) { return BadRequest(new { code = "billing_invalid_json" }); }
    }
}
