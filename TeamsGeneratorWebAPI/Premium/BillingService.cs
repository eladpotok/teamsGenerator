using Microsoft.Extensions.Options;

namespace TeamsGeneratorWebAPI.Premium;

public sealed class BillingService
{
    private readonly IBillingStore _store;
    private readonly BillingOptions _options;
    private readonly IPlayPurchaseStore? _play;
    public BillingService(IBillingStore store, IOptions<BillingOptions> options,
        IPlayPurchaseStore? play = null)
        => (_store, _options, _play) = (store, options.Value, play);
    public bool Enabled => _options.Enabled;
    public string Environment => _options.Environment;
    public void RequireEnabled()
    {
        if (!Enabled) throw new BillingException(503, "billing_disabled", "Google Play subscriptions are not available yet.");
    }
    public async Task<BillingAccount> BindAsync(string uid, CancellationToken ct)
    {
        RequireEnabled();
        var account = await _store.BindAsync(uid, ct);
        // Persist identity before checkout. RevenueCat receives a token only after Google verifies this binding.
        return await SyncAsync(account, ct);
    }
    public async Task<BillingAccount?> ReadAsync(string uid, CancellationToken ct)
    {
        if (!Enabled) return null;
        var account = await _store.ReadAsync(TableBillingStore.AccountKey(uid), ct);
        return account == null ? null : await ReconcileAsync(account, ct, advanceSchedule: false);
    }
    public bool IsActive(BillingAccount? account, DateTimeOffset now) => Enabled && account != null
        && account.Sandbox == _options.Sandbox && account.ExpiresAt > now
        && account.VerifiedAt > now.AddHours(-_options.MaximumStaleHours);
    public async Task<BillingAccount> SyncAsync(BillingAccount account, CancellationToken ct)
    {
        RequireEnabled();
        var current = await ReadRequiredAsync(account.RowKey, ct);
        return await ReconcileAsync(current, ct, advanceSchedule: true);
    }

    private async Task<BillingAccount> ReadRequiredAsync(string key, CancellationToken ct) =>
        await _store.ReadAsync(key, ct)
            ?? throw new BillingException(409, "billing_binding_missing", "Purchase account binding is missing.");

    private async Task<BillingAccount> ReconcileAsync(BillingAccount current, CancellationToken ct, bool advanceSchedule)
    {
        for (var attempt = 0; attempt < 6; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            if (attempt > 0) current = await ReadRequiredAsync(current.RowKey, ct);
            var receipts = _play == null ? Array.Empty<PlayReceipt>() : await _play.AccountReceiptsAsync(current.RowKey, ct);
            var now = DateTimeOffset.UtcNow;
            var latest = receipts.Where(r => r.VerifiedAt > now.AddHours(-_options.MaximumStaleHours)
                && r.AccountKey == current.RowKey && r.SupersededByToken == null)
                .OrderByDescending(r => r.ExpiresAt > now).ThenByDescending(r => r.ExpiresAt)
                .ThenByDescending(r => r.VerifiedAt).FirstOrDefault();
            var providerDate = latest?.VerifiedAt?.ToUnixTimeMilliseconds() ?? 0;
            var product = latest == null ? null : "teamify_premium";
            var status = latest?.State ?? "inactive";
            var order = latest?.ExpiresAt.HasValue == true ? latest.Order : null;
            var unchanged = current.ExpiresAt == latest?.ExpiresAt && current.VerifiedAt == latest?.VerifiedAt
                && current.ProviderDateMs == providerDate && current.Product == product
                && current.Status == status && current.Sandbox == _options.Sandbox
                && current.PurchaseOrder == order;
            // Reads must not push the background reconciliation deadline into the future.
            // A due worker still advances its deadline even when the paid state is unchanged.
            if (unchanged && (!advanceSchedule || current.RetryAt > now)) return current;
            // RevenueCat IDs/aliases and client receipt submissions are not ownership evidence.
            // Only Google-verified, durably claimed receipts may grant paid access.
            current.ExpiresAt = latest?.ExpiresAt;
            current.VerifiedAt = latest?.VerifiedAt;
            current.ProviderDateMs = providerDate;
            current.Product = product;
            current.Status = status;
            current.Sandbox = _options.Sandbox;
            current.PurchaseOrder = order;
            current.RetryAt = now.AddMinutes(_options.ReconcileMinutes);
            try
            {
                if (await _store.SaveAsync(current, order, ct))
                    return current;
            }
            catch (BillingException ex) when (ex.Code == "billing_ownership_conflict")
            {
                current.ExpiresAt = null;
                current.Status = "ownership_conflict";
                current.PurchaseOrder = null;
                if (await _store.SaveAsync(current, null, ct)) return current;
            }
        }
        throw new BillingException(409, "billing_sync_conflict", "Purchase verification conflicted. Please retry without purchasing again.");
    }
}
