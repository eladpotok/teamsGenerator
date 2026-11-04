using Azure;
using Azure.Data.Tables;

namespace TeamsGeneratorWebAPI.Premium;

public sealed class PlayReceipt : ITableEntity
{
    public string PartitionKey { get; set; } = "";
    public string RowKey { get; set; } = "";
    public string Kind { get; set; } = "PlayReceipt";
    public string Token { get; set; } = "";
    public string? AccountKey { get; set; }
    public string? VerifiedAccountId { get; set; }
    public string? VerifiedProfileId { get; set; }
    public bool OutOfAppResubscription { get; set; }
    public bool Sandbox { get; set; }
    public bool PaidInitialOrder { get; set; }
    public DateTimeOffset? NewPurchaseEventAt { get; set; }
    public string? OwnerAlertKey { get; set; }
    public bool OwnerAlertPending { get; set; }
    public string? Order { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? VerifiedAt { get; set; }
    public DateTimeOffset? EligiblePurchaseObservedAt { get; set; }
    public DateTimeOffset? ProviderStartedAt { get; set; }
    public DateTimeOffset? AcknowledgmentDeadlineEstimateAt { get; set; }
    public string AcknowledgmentState { get; set; } = "unknown";
    public DateTimeOffset? AcknowledgmentConfirmedAt { get; set; }
    public string? AcknowledgmentConfirmationSource { get; set; }
    public DateTimeOffset? AcknowledgmentFirstAttemptAt { get; set; }
    public DateTimeOffset? AcknowledgmentLastAttemptAt { get; set; }
    public int AcknowledgmentAttempts { get; set; }
    public DateTimeOffset? RecoveryCycleStartedAt { get; set; }
    public int RecoveryAttempts { get; set; }
    public int RecoveryCycleAttempts { get; set; }
    public DateTimeOffset RetryAt { get; set; } = DateTimeOffset.UtcNow;
    public string State { get; set; } = "unverified";
    public bool Completed { get; set; }
    public string? SupersededByToken { get; set; }
    public string? MirrorError { get; set; }
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    internal bool AcknowledgmentEligible => SupersededByToken == null && !Completed
        && State is "active" or "cancelled" or "grace_period";
}

public interface IPlayPurchaseStore
{
    Task<PlayReceipt> ReceiveAsync(string token, CancellationToken ct);
    Task<PlayReceipt?> ReadReceiptAsync(string token, CancellationToken ct);
    Task<PlayReceipt?> ReadReceiptKeyAsync(string key, CancellationToken ct);
    Task<BillingAccount?> FindPlayAccountAsync(string accountId, CancellationToken ct);
    Task<bool> SaveReceiptAsync(PlayReceipt receipt, CancellationToken ct);
    Task ClaimAsync(PlayReceipt receipt, CancellationToken ct);
    Task<PlayReceipt?> VerifiedOrderAsync(string order, string accountKey, CancellationToken ct);
    Task<BillingPage<PlayReceipt>> ReceiptsAsync(string? cursor, CancellationToken ct);
    Task<IReadOnlyList<PlayReceipt>> AccountReceiptsAsync(string accountKey, CancellationToken ct);
    Task SupersedeAsync(string token, string replacement, string accountKey, CancellationToken ct);
}

public sealed partial class TableBillingStore
{
    private string ReceiptPartition => _options.Environment + "-Play";
    private static string OrderKey(string order) => "play-order-" + Hash(order.Split(new[] { ".." }, StringSplitOptions.None)[0]);

    public async Task<PlayReceipt> ReceiveAsync(string token, CancellationToken ct)
    {
        var table = await GetTableAsync(ct);
        var receipt = new PlayReceipt { PartitionKey = ReceiptPartition, RowKey = Hash(token), Token = token };
        try { await table.AddEntityAsync(receipt, ct); }
        catch (RequestFailedException ex) when (ex.Status == 409) { }
        var current = await ReadReceiptAsync(token, ct)
            ?? throw new BillingException(503, "billing_storage_unavailable", "Purchase recovery is unavailable.");
        current.Completed = false;
        current.RetryAt = DateTimeOffset.UtcNow;
        if (!await SaveReceiptAsync(current, ct))
            throw new BillingException(409, "billing_sync_conflict", "Purchase recovery changed. Retry.");
        return current;
    }

    public async Task<PlayReceipt?> ReadReceiptAsync(string token, CancellationToken ct)
        => await ReadReceiptKeyAsync(Hash(token), ct);

    public async Task<PlayReceipt?> ReadReceiptKeyAsync(string key, CancellationToken ct)
    {
        var table = await GetTableAsync(ct);
        var result = await table.GetEntityIfExistsAsync<PlayReceipt>(ReceiptPartition, key, cancellationToken: ct);
        return result.HasValue ? result.Value : null;
    }

    public async Task<BillingAccount?> FindPlayAccountAsync(string accountId, CancellationToken ct)
    {
        var table = await GetTableAsync(ct);
        var reverse = await table.GetEntityIfExistsAsync<TableEntity>(_options.Environment,
            "customer-" + accountId, cancellationToken: ct);
        return reverse.HasValue ? await ReadAsync(reverse.Value.GetString("AccountKey"), ct) : null;
    }

    public async Task<bool> SaveReceiptAsync(PlayReceipt receipt, CancellationToken ct)
    {
        var table = await GetTableAsync(ct);
        try
        {
            var result = await table.UpdateEntityAsync(receipt, receipt.ETag, TableUpdateMode.Replace, ct);
            if (!result.Headers.TryGetValue("ETag", out var etag))
                throw new BillingException(503, "billing_storage_response", "Missing purchase concurrency token.");
            receipt.ETag = new ETag(etag);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 412) { return false; }
    }

    public async Task ClaimAsync(PlayReceipt receipt, CancellationToken ct)
    {
        if (receipt.Order == null || receipt.AccountKey == null)
            throw new BillingException(409, "billing_ownership_unknown", "Purchase ownership is not verified.");
        var table = await GetTableAsync(ct);
        var key = OrderKey(receipt.Order);
        // This immutable claim is made only after the Google API verifies the purchase's initiating identity.
        var claim = new TableEntity(_options.Environment, key)
        {
            ["Kind"] = "PlayOwnership", ["AccountKey"] = receipt.AccountKey, ["Token"] = receipt.Token
        };
        try { await table.AddEntityAsync(claim, ct); }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            var existing = await table.GetEntityIfExistsAsync<TableEntity>(_options.Environment, key, cancellationToken: ct);
            if (!existing.HasValue)
                throw new BillingException(409, "billing_sync_conflict", "Purchase claim changed. Retry.");
            if (existing.Value.GetString("AccountKey") != receipt.AccountKey)
                throw new BillingException(409, "billing_ownership_conflict", "This purchase belongs to another account.");
            // A replacement token can have the same root order. Retain the original owner, follow the latest token.
            if (existing.Value.GetString("Token") != receipt.Token)
            {
                existing.Value["Token"] = receipt.Token;
                await table.UpdateEntityAsync(existing.Value, existing.Value.ETag, TableUpdateMode.Replace, ct);
            }
        }
    }

    public async Task<PlayReceipt?> VerifiedOrderAsync(string order, string accountKey, CancellationToken ct)
    {
        var table = await GetTableAsync(ct);
        var claim = await table.GetEntityIfExistsAsync<TableEntity>(_options.Environment, OrderKey(order), cancellationToken: ct);
        if (!claim.HasValue || claim.Value.GetString("AccountKey") != accountKey) return null;
        var receipt = await ReadReceiptAsync(claim.Value.GetString("Token"), ct);
        return receipt?.AccountKey == accountKey ? receipt : null;
    }

    public async Task<BillingPage<PlayReceipt>> ReceiptsAsync(string? cursor, CancellationToken ct)
    {
        var table = await GetTableAsync(ct);
        var partition = ReceiptPartition;
        var now = DateTimeOffset.UtcNow;
        await foreach (var page in table.QueryAsync<PlayReceipt>(r => r.PartitionKey == partition && (!r.Completed || r.OwnerAlertPending)
            && r.RetryAt <= now, maxPerPage: 32, cancellationToken: ct).AsPages(cursor, 32))
            return new(page.Values, page.ContinuationToken);
        return new(Array.Empty<PlayReceipt>(), null);
    }

    public async Task<IReadOnlyList<PlayReceipt>> AccountReceiptsAsync(string accountKey, CancellationToken ct)
    {
        var table = await GetTableAsync(ct);
        var partition = ReceiptPartition;
        var receipts = new List<PlayReceipt>();
        await foreach (var receipt in table.QueryAsync<PlayReceipt>(r => r.PartitionKey == partition
            && r.AccountKey == accountKey, cancellationToken: ct))
            receipts.Add(receipt);
        return receipts;
    }

    public async Task SupersedeAsync(string token, string replacement, string accountKey, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 6; attempt++)
        {
            var receipt = await ReadReceiptAsync(token, ct) ?? await ReceiveAsync(token, ct);
            if (receipt.AccountKey != null && receipt.AccountKey != accountKey)
                throw new BillingException(409, "billing_ownership_conflict", "Subscription chain owner differs.");
            receipt.AccountKey = accountKey;
            receipt.SupersededByToken = replacement;
            receipt.ExpiresAt = null;
            receipt.State = "replaced";
            receipt.Completed = true;
            if (await SaveReceiptAsync(receipt, ct)) return;
        }
        throw new BillingException(409, "billing_sync_conflict", "Subscription replacement conflicted. Retry.");
    }
}
