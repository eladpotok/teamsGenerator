using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Options;

namespace TeamsGeneratorWebAPI.Premium;

public sealed record PlayPurchase(string? AccountId, string? LinkedToken, string? Order,
    string State, DateTimeOffset? ExpiresAt, bool Acknowledged, bool Sandbox,
    string? ProfileId = null, bool OutOfAppResubscription = false,
    DateTimeOffset? StartedAt = null, bool AcknowledgmentKnown = true, bool PaidInitialOrder = false);

public interface IPlayPurchaseClient
{
    Task<PlayPurchase> GetAsync(string token, CancellationToken ct);
    Task AcknowledgeAsync(string token, string? accountId, string? profileId, CancellationToken ct);
    Task ImportAsync(string token, string appUserId, CancellationToken ct);
}

public sealed class PlayPurchaseClient : IPlayPurchaseClient
{
    private readonly IHttpClientFactory _clients;
    private readonly BillingOptions _options;
    private GoogleCredential? _credential;
    private readonly SemaphoreSlim _credentialGate = new(1, 1);
    public PlayPurchaseClient(IHttpClientFactory clients, IOptions<BillingOptions> options)
        => (_clients, _options) = (clients, options.Value);

    public PlayPurchaseClient(IHttpClientFactory clients, IOptions<BillingOptions> options, GoogleCredential credential)
        : this(clients, options) => _credential = credential;

    private async Task<string> AccessTokenAsync(CancellationToken ct)
    {
        await _credentialGate.WaitAsync(ct);
        try
        {
            // ADC is supplied by the host/secret mount, never by the mobile app or a request body.
            try
            {
                _credential ??= (await GoogleCredential.GetApplicationDefaultAsync(ct))
                    .CreateScoped("https://www.googleapis.com/auth/androidpublisher");
                return await _credential.UnderlyingCredential.GetAccessTokenForRequestAsync(cancellationToken: ct);
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException
                or Google.Apis.Auth.OAuth2.Responses.TokenResponseException or HttpRequestException)
            {
                throw new BillingException(503, "billing_google_credentials", "Google purchase verification credentials are unavailable.");
            }
        }
        finally { _credentialGate.Release(); }
    }

    private async Task<HttpResponseMessage> SendAsync(string client, HttpRequestMessage request, CancellationToken ct)
    {
        try
        {
            var response = await _clients.CreateClient(client).SendAsync(request, ct);
            if (response.IsSuccessStatusCode) return response;
            var status = response.StatusCode;
            response.Dispose();
            throw new BillingException(503, status == HttpStatusCode.Gone ? "billing_receipt_expired" : "billing_provider_unavailable",
                "Purchase verification is unavailable. Retry without purchasing again.");
        }
        catch (HttpRequestException)
        {
            throw new BillingException(503, "billing_provider_unavailable", "Purchase verification is unavailable.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new BillingException(503, "billing_provider_timeout", "Purchase verification timed out.");
        }
    }

    public async Task<PlayPurchase> GetAsync(string token, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "purchases/subscriptionsv2/tokens/" + Uri.EscapeDataString(token));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await AccessTokenAsync(ct));
        using var response = await SendAsync("GooglePlay", request, ct);
        try
        {
            var purchase = Parse(await response.Content.ReadAsStringAsync(ct));
            if (purchase.Order != null && purchase.ExpiresAt > DateTimeOffset.UtcNow)
            {
                using var orderRequest = new HttpRequestMessage(HttpMethod.Get, "orders/" + Uri.EscapeDataString(purchase.Order)
                    + "?fields=orderId,purchaseToken,state,total,lineItems");
                orderRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await AccessTokenAsync(ct));
                using var orderResponse = await SendAsync("GooglePlay", orderRequest, ct);
                var orderJson = await orderResponse.Content.ReadAsStringAsync(ct);
                if (!OrderPaid(orderJson, purchase.Order, token))
                    purchase = purchase with { ExpiresAt = null, State = "REFUNDED" };
                else
                    purchase = purchase with { PaidInitialOrder = IsPaidInitialOrder(orderJson, purchase) };
            }
            return purchase;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        { throw new BillingException(502, "billing_provider_response", "Invalid Google purchase response."); }
    }

    public async Task AcknowledgeAsync(string token, string? accountId, string? profileId, CancellationToken ct)
    {
        var body = new Dictionary<string, object>();
        // Google permits these fields only on resubscriptions, with names different from subscriptionsv2 GET.
        if (accountId != null)
        {
            var identifiers = new Dictionary<string, string> { ["obfuscatedAccountId"] = accountId };
            if (profileId != null) identifiers["obfuscatedProfileId"] = profileId;
            body["externalAccountIds"] = identifiers;
        }
        using var request = new HttpRequestMessage(HttpMethod.Post,
            "purchases/subscriptions/teamify_premium/tokens/" + Uri.EscapeDataString(token) + ":acknowledge")
        { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await AccessTokenAsync(ct));
        using var response = await SendAsync("GooglePlay", request, ct);
    }

    public async Task ImportAsync(string token, string appUserId, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "receipts")
        {
            Content = JsonContent.Create(new { app_user_id = appUserId, fetch_token = token,
                product_id = "teamify_premium", presented_offering_identifier = "default", is_restore = false })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.AndroidPublicApiKey);
        request.Headers.Add("X-Platform", "android");
        using var response = await SendAsync("RevenueCat", request, ct);
        // Do not allow the provider's first-receipt association to override Google's immutable owner.
        RevenueCatSnapshot result;
        try { result = RevenueCatClient.Parse(await response.Content.ReadAsStringAsync(ct), appUserId, _options.Sandbox, DateTimeOffset.UtcNow); }
        catch (JsonException) { throw new BillingException(502, "billing_provider_response", "Invalid RevenueCat import response."); }
        if (result.Status == "ownership_conflict")
            throw new BillingException(409, "billing_provider_ownership_conflict", "Provider purchase attribution needs support review.");
    }

    public static PlayPurchase Parse(string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
        var root = document.RootElement;
        string? Text(JsonElement obj, string key) => obj.TryGetProperty(key, out var value)
            && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        var state = Text(root, "subscriptionState") ?? throw new JsonException("Missing state.");
        var supported = new[] { "SUBSCRIPTION_STATE_PENDING", "SUBSCRIPTION_STATE_ACTIVE",
            "SUBSCRIPTION_STATE_IN_GRACE_PERIOD", "SUBSCRIPTION_STATE_ON_HOLD", "SUBSCRIPTION_STATE_PAUSED",
            "SUBSCRIPTION_STATE_CANCELED", "SUBSCRIPTION_STATE_EXPIRED", "SUBSCRIPTION_STATE_PENDING_PURCHASE_CANCELED" };
        if (!supported.Contains(state)) throw new JsonException("Unknown subscription state.");
        var items = root.GetProperty("lineItems");
        if (items.GetArrayLength() != 1) throw new JsonException("Unsupported subscription items.");
        var item = items[0];
        if (Text(item, "productId") != "teamify_premium") throw new JsonException("Unexpected product.");
        if (!item.TryGetProperty("offerDetails", out var offer) || Text(offer, "basePlanId") is not ("monthly" or "yearly")
            || Text(offer, "offerId") != null || !item.TryGetProperty("autoRenewingPlan", out _))
            throw new JsonException("Unsupported subscription plan.");
        var owner = root.TryGetProperty("externalAccountIdentifiers", out var external) && external.ValueKind == JsonValueKind.Object
            ? Text(external, "obfuscatedExternalAccountId") : null;
        var profile = external.ValueKind == JsonValueKind.Object ? Text(external, "obfuscatedExternalProfileId") : null;
        var outOfAppResubscription = root.TryGetProperty("outOfAppPurchaseContext", out var outOfApp)
            && outOfApp.ValueKind == JsonValueKind.Object;
        if (outOfAppResubscription
            && outOfApp.TryGetProperty("expiredExternalAccountIdentifiers", out var expiredIds)
            && expiredIds.ValueKind == JsonValueKind.Object)
        {
            var expiredOwner = Text(expiredIds, "obfuscatedExternalAccountId");
            var expiredProfile = Text(expiredIds, "obfuscatedExternalProfileId");
            if ((owner != null && expiredOwner != null && owner != expiredOwner)
                || (profile != null && expiredProfile != null && profile != expiredProfile))
                throw new JsonException("Conflicting resubscription identifiers.");
            owner ??= expiredOwner;
            profile ??= expiredProfile;
        }
        var expiry = Text(item, "expiryTime") is { } time && DateTimeOffset.TryParse(time, out var parsed) ? parsed : (DateTimeOffset?)null;
        if (state is not ("SUBSCRIPTION_STATE_ACTIVE" or "SUBSCRIPTION_STATE_IN_GRACE_PERIOD" or "SUBSCRIPTION_STATE_CANCELED"))
            expiry = null;
        var order = Text(item, "latestSuccessfulOrderId") ?? Text(root, "latestOrderId");
        if (expiry != null && (order == null || !order.StartsWith("GPA.", StringComparison.Ordinal)))
            throw new JsonException("Missing paid order.");
        var acknowledgment = Text(root, "acknowledgementState");
        if (acknowledgment is not (null or "ACKNOWLEDGEMENT_STATE_UNSPECIFIED"
            or "ACKNOWLEDGEMENT_STATE_PENDING" or "ACKNOWLEDGEMENT_STATE_ACKNOWLEDGED"))
            throw new JsonException("Unknown acknowledgment state.");
        DateTimeOffset? started = null;
        if (root.TryGetProperty("startTime", out var start))
        {
            if (start.ValueKind != JsonValueKind.String || !start.TryGetDateTimeOffset(out var timestamp))
                throw new JsonException("Invalid subscription start time.");
            started = timestamp;
        }
        return new(owner, Text(root, "linkedPurchaseToken"), order, state, expiry,
            acknowledgment == "ACKNOWLEDGEMENT_STATE_ACKNOWLEDGED",
            root.TryGetProperty("testPurchase", out var test) && test.ValueKind == JsonValueKind.Object,
            profile, outOfAppResubscription, started,
            acknowledgment is "ACKNOWLEDGEMENT_STATE_PENDING" or "ACKNOWLEDGEMENT_STATE_ACKNOWLEDGED");
    }

    public static bool OrderPaid(string json, string order, string token)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.GetProperty("orderId").GetString() != order || root.GetProperty("purchaseToken").GetString() != token)
            throw new JsonException("Order does not match the verified subscription.");
        return root.GetProperty("state").GetString() switch
        {
            "PROCESSED" => true,
            "REFUNDED" or "PARTIALLY_REFUNDED" or "CANCELED" or "PENDING_REFUND" => false,
            _ => throw new JsonException("Order has not been processed.")
        };
    }

    public static bool IsPaidInitialOrder(string json, PlayPurchase purchase)
    {
        if (purchase.Sandbox || purchase.LinkedToken != null || purchase.OutOfAppResubscription
            || purchase.StartedAt == null || purchase.State is not ("SUBSCRIPTION_STATE_ACTIVE" or "SUBSCRIPTION_STATE_CANCELED"))
            return false;
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        // Entitlement/processed state alone does not prove money changed hands.
        // Alert-only fields are optional evidence, never a reason to reject an otherwise verified order.
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("total", out var total)
            || total.ValueKind != JsonValueKind.Object || !total.TryGetProperty("currencyCode", out var currency)
            || currency.ValueKind != JsonValueKind.String || currency.GetString() is not { Length: 3 }) return false;
        var whole = 0L;
        if (total.TryGetProperty("units", out var units)
            && (units.ValueKind != JsonValueKind.String || !long.TryParse(units.GetString(), out whole))) return false;
        var nanos = 0;
        if (total.TryGetProperty("nanos", out var fraction)
            && (fraction.ValueKind != JsonValueKind.Number || !fraction.TryGetInt32(out nanos))) return false;
        if (whole < 0 || nanos is < 0 or >= 1000000000 || (whole == 0 && nanos == 0)) return false;
        if (!root.TryGetProperty("lineItems", out var items) || items.ValueKind != JsonValueKind.Array
            || items.GetArrayLength() != 1 || items[0].ValueKind != JsonValueKind.Object
            || !items[0].TryGetProperty("subscriptionDetails", out var details) || details.ValueKind != JsonValueKind.Object)
            return false;
        var basePhase = details.TryGetProperty("offerPhaseDetails", out var phase)
            ? phase.ValueKind == JsonValueKind.Object && phase.TryGetProperty("baseDetails", out var basis)
                && basis.ValueKind == JsonValueKind.Object && !phase.TryGetProperty("freeTrialDetails", out _)
                && !phase.TryGetProperty("prorationPeriodDetails", out _) && !phase.TryGetProperty("introductoryPriceDetails", out _)
            : details.TryGetProperty("offerPhase", out var legacyPhase)
                && legacyPhase.ValueKind == JsonValueKind.String && legacyPhase.GetString() == "BASE";
        // The initial funded service period must begin at the subscription grant time. This excludes
        // renewals/trial conversions without guessing Google's evolving order-suffix conventions.
        return basePhase && details.TryGetProperty("servicePeriodStartTime", out var start)
            && start.ValueKind == JsonValueKind.String && start.TryGetDateTimeOffset(out var periodStart) && periodStart == purchase.StartedAt;
    }
}
