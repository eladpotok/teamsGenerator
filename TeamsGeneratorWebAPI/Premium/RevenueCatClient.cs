using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace TeamsGeneratorWebAPI.Premium;

public interface IRevenueCatClient
{
    Task<RevenueCatSnapshot> GetAsync(string appUserId, CancellationToken ct);
}

public sealed record RevenueCatSnapshot(long RequestDateMs, string OriginalAppUserId,
    DateTimeOffset? ExpiresAt, string Status, string? Product, bool Sandbox, string? Transaction);

public sealed class RevenueCatClient : IRevenueCatClient
{
    private readonly IHttpClientFactory _clients;
    private readonly BillingOptions _options;
    public RevenueCatClient(IHttpClientFactory clients, IOptions<BillingOptions> options)
        => (_clients, _options) = (clients, options.Value);

    public async Task<RevenueCatSnapshot> GetAsync(string appUserId, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "subscribers/" + Uri.EscapeDataString(appUserId));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.SecretApiKey);
        try
        {
            using var response = await _clients.CreateClient("RevenueCat").SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                throw new BillingException(503, "billing_provider_unavailable", "Purchase verification is temporarily unavailable. Retry without purchasing again.");
            var json = await response.Content.ReadAsStringAsync(ct);
            return Parse(json, appUserId, _options.Sandbox, DateTimeOffset.UtcNow);
        }
        catch (HttpRequestException)
        {
            throw new BillingException(503, "billing_provider_unavailable", "Purchase verification is temporarily unavailable. Retry without purchasing again.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new BillingException(503, "billing_provider_timeout", "Purchase verification timed out. Retry without purchasing again.");
        }
        catch (JsonException)
        {
            throw new BillingException(502, "billing_provider_response", "The purchase provider returned an invalid response.");
        }
    }

    public static RevenueCatSnapshot Parse(string json, string expectedId, bool sandbox, DateTimeOffset now)
    {
        var response = JsonSerializer.Deserialize<CustomerResponse>(json)
            ?? throw new JsonException("Missing customer response.");
        var customer = response.Subscriber ?? throw new JsonException("Missing subscriber.");
        if (response.RequestDateMs <= 0 || response.RequestDateMs > now.AddMinutes(5).ToUnixTimeMilliseconds()
            || string.IsNullOrEmpty(customer.OriginalAppUserId) || customer.Entitlements == null || customer.Subscriptions == null)
            throw new JsonException("Invalid customer metadata.");
        RevenueCatSnapshot Inactive(string status) =>
            new(response.RequestDateMs, customer.OriginalAppUserId, null, status, null, sandbox, null);
        // Never accept anonymous aliases, another account's customer, promotions or lifetime grants.
        if (customer.OriginalAppUserId != expectedId) return Inactive("ownership_conflict");
        if (!customer.Entitlements.TryGetValue("teamify_premium", out var entitlement)) return Inactive("inactive");
        var product = entitlement.Product;
        if (product == null || !(product == "teamify_premium" || product == "teamify_premium:monthly"
            || product == "teamify_premium:yearly") || !customer.Subscriptions.TryGetValue(product, out var subscription))
            return Inactive("unsupported_product");
        if (subscription.Store != "play_store" || subscription.Sandbox != sandbox
            || subscription.Ownership != "PURCHASED" || string.IsNullOrWhiteSpace(subscription.Transaction)
            || !string.Equals(subscription.PeriodType, "normal", StringComparison.OrdinalIgnoreCase)
            || subscription.ExpiresAt == null || entitlement.ExpiresAt == null)
            return Inactive("inactive");
        if (subscription.RefundedAt != null) return Inactive("refunded");
        // Cancellation/pending pause do not end the paid period. Only a provider-confirmed grace window extends it.
        var expiry = subscription.ExpiresAt.Value < entitlement.ExpiresAt.Value
            ? subscription.ExpiresAt.Value : entitlement.ExpiresAt.Value;
        if (subscription.GraceAt is { } grace && entitlement.GraceAt is { } entitlementGrace)
            expiry = new[] { expiry, grace < entitlementGrace ? grace : entitlementGrace }.Max();
        var status = expiry <= now ? "expired"
            : subscription.BillingIssueAt != null ? "grace_period"
            : subscription.UnsubscribeAt != null ? "cancelled" : "active";
        return new(response.RequestDateMs, customer.OriginalAppUserId, expiry, status, product, sandbox,
            subscription.Transaction);
    }

    private sealed class CustomerResponse
    {
        [JsonPropertyName("request_date_ms")] public long RequestDateMs { get; set; }
        [JsonPropertyName("subscriber")] public Customer? Subscriber { get; set; }
    }
    private sealed class Customer
    {
        [JsonPropertyName("original_app_user_id")] public string OriginalAppUserId { get; set; } = "";
        [JsonPropertyName("entitlements")] public Dictionary<string, Entitlement>? Entitlements { get; set; }
        [JsonPropertyName("subscriptions")] public Dictionary<string, Subscription>? Subscriptions { get; set; }
    }
    private sealed class Entitlement
    {
        [JsonPropertyName("product_identifier")] public string? Product { get; set; }
        [JsonPropertyName("expires_date")] public DateTimeOffset? ExpiresAt { get; set; }
        [JsonPropertyName("grace_period_expires_date")] public DateTimeOffset? GraceAt { get; set; }
    }
    private sealed class Subscription
    {
        [JsonPropertyName("store")] public string? Store { get; set; }
        [JsonPropertyName("is_sandbox")] public bool? Sandbox { get; set; }
        [JsonPropertyName("ownership_type")] public string? Ownership { get; set; }
        [JsonPropertyName("period_type")] public string? PeriodType { get; set; }
        [JsonPropertyName("expires_date")] public DateTimeOffset? ExpiresAt { get; set; }
        [JsonPropertyName("grace_period_expires_date")] public DateTimeOffset? GraceAt { get; set; }
        [JsonPropertyName("refunded_at")] public DateTimeOffset? RefundedAt { get; set; }
        [JsonPropertyName("billing_issues_detected_at")] public DateTimeOffset? BillingIssueAt { get; set; }
        [JsonPropertyName("unsubscribe_detected_at")] public DateTimeOffset? UnsubscribeAt { get; set; }
        [JsonPropertyName("store_transaction_id")] public string? Transaction { get; set; }
    }
}
