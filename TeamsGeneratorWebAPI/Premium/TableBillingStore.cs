using System.Security.Cryptography;
using System.Text;
using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Options;

namespace TeamsGeneratorWebAPI.Premium;

public sealed class BillingAccount : ITableEntity
{
    public string PartitionKey { get; set; } = "";
    public string RowKey { get; set; } = "";
    public string Kind { get; set; } = "Account";
    public string AppUserId { get; set; } = "";
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? VerifiedAt { get; set; }
    public DateTimeOffset RetryAt { get; set; } = DateTimeOffset.UtcNow;
    public long ProviderDateMs { get; set; }
    public string Status { get; set; } = "inactive";
    public string? Product { get; set; }
    // Written atomically with the original-order ownership claim in SaveAsync.
    public string? PurchaseOrder { get; set; }
    public bool Sandbox { get; set; }
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
}

public sealed class BillingInbox : ITableEntity
{
    public string PartitionKey { get; set; } = "";
    public string RowKey { get; set; } = "";
    public string Kind { get; set; } = "Event";
    public string CustomerIdsJson { get; set; } = "[]";
    public string? NewPurchaseOrder { get; set; }
    public DateTimeOffset? NewPurchaseAt { get; set; }
    public DateTimeOffset RetryAt { get; set; } = DateTimeOffset.UtcNow;
    public bool Completed { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
}

public sealed record BillingPage<T>(IReadOnlyList<T> Items, string? Cursor);

public interface IBillingStore
{
    Task<BillingAccount?> ReadAsync(string key, CancellationToken ct);
    Task<BillingAccount> BindAsync(string uid, CancellationToken ct);
    Task<BillingAccount?> FindCustomerAsync(string id, CancellationToken ct);
    Task<bool> SaveAsync(BillingAccount account, string? transaction, CancellationToken ct);
    Task AcceptAsync(BillingInbox item, CancellationToken ct);
    Task<bool> SaveInboxAsync(BillingInbox item, CancellationToken ct);
    Task<BillingPage<BillingInbox>> EventsAsync(string? cursor, CancellationToken ct);
    Task<BillingPage<BillingAccount>> AccountsAsync(string? cursor, CancellationToken ct);
}

public sealed partial class TableBillingStore : IBillingStore, IPlayPurchaseStore
{
    private readonly Lazy<TableClient> _table;
    private readonly SemaphoreSlim _initialization = new(1, 1);
    private bool _initialized;
    private readonly BillingOptions _options;
    public TableBillingStore(TableServiceClient tables, IOptions<BillingOptions> options)
    {
        _options = options.Value;
        _table = new Lazy<TableClient>(() => tables.GetTableClient(_options.TableName));
    }
    private async Task<TableClient> GetTableAsync(CancellationToken ct)
    {
        await _initialization.WaitAsync(ct);
        try
        {
            ct.ThrowIfCancellationRequested();
            if (!_initialized)
            {
                await _table.Value.CreateIfNotExistsAsync(ct);
                ct.ThrowIfCancellationRequested();
                _initialized = true;
            }
            return _table.Value;
        }
        finally { _initialization.Release(); }
    }
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static string AccountKey(string uid) => "account-" + Hash(uid);
    public async Task<BillingAccount?> ReadAsync(string key, CancellationToken ct)
    {
        var table = await GetTableAsync(ct);
        var result = await table.GetEntityIfExistsAsync<BillingAccount>(_options.Environment, key, cancellationToken: ct);
        return result.HasValue ? result.Value : null;
    }
    public async Task<BillingAccount> BindAsync(string uid, CancellationToken ct)
    {
        var key = AccountKey(uid);
        var existing = await ReadAsync(key, ct);
        if (existing != null) return existing;
        var table = await GetTableAsync(ct);
        var id = (_options.Sandbox ? "tfs_" : "tfp_") + Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var account = new BillingAccount { PartitionKey = _options.Environment, RowKey = key, AppUserId = id, Sandbox = _options.Sandbox };
        var reverse = new TableEntity(_options.Environment, "customer-" + Hash(id)) { ["AccountKey"] = key, ["Kind"] = "Identity" };
        try
        {
            await table.SubmitTransactionAsync(new[]
            {
                new TableTransactionAction(TableTransactionActionType.Add, account),
                new TableTransactionAction(TableTransactionActionType.Add, reverse)
            }, ct);
        }
        catch (RequestFailedException ex) when (ex.Status == 409) { }
        return await ReadAsync(key, ct) ?? throw new BillingException(409, "billing_binding_conflict", "Account binding conflicted. Please retry.");
    }
    public async Task<BillingAccount?> FindCustomerAsync(string id, CancellationToken ct)
    {
        var table = await GetTableAsync(ct);
        var reverse = await table.GetEntityIfExistsAsync<TableEntity>(_options.Environment,
            "customer-" + Hash(id), cancellationToken: ct);
        return reverse.HasValue ? await ReadAsync(reverse.Value.GetString("AccountKey"), ct) : null;
    }
    public async Task<bool> SaveAsync(BillingAccount account, string? transaction, CancellationToken ct)
    {
        var table = await GetTableAsync(ct);
        var actions = new List<TableTransactionAction>
        {
            new(TableTransactionActionType.UpdateReplace, account, account.ETag)
        };
        if (transaction != null)
        {
            // Google renewal order suffixes change; ownership of the original order never transfers.
            var rootOrder = transaction.Split(new[] { ".." }, StringSplitOptions.None)[0];
            var rowKey = "purchase-" + Hash(rootOrder);
            var claim = await table.GetEntityIfExistsAsync<TableEntity>(_options.Environment, rowKey, cancellationToken: ct);
            if (claim.HasValue && claim.Value.GetString("AccountKey") != account.RowKey)
                throw new BillingException(409, "billing_ownership_conflict", "This purchase belongs to another Teamify account. Sign in to the original account.");
            if (!claim.HasValue) actions.Add(new(TableTransactionActionType.Add,
                new TableEntity(_options.Environment, rowKey) { ["Kind"] = "Purchase", ["AccountKey"] = account.RowKey }));
        }
        try
        {
            await table.SubmitTransactionAsync(actions, ct);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status is 409 or 412) { return false; }
    }
    public async Task AcceptAsync(BillingInbox item, CancellationToken ct)
    {
        var table = await GetTableAsync(ct);
        item.PartitionKey = _options.Environment + "-Inbox";
        try { await table.AddEntityAsync(item, ct); }
        catch (RequestFailedException ex) when (ex.Status == 409) { /* Durable duplicate, already accepted. */ }
    }
    public async Task<bool> SaveInboxAsync(BillingInbox item, CancellationToken ct)
    {
        var table = await GetTableAsync(ct);
        try
        {
            var response = await table.UpdateEntityAsync(item, item.ETag, TableUpdateMode.Replace, ct);
            if (!response.Headers.TryGetValue("ETag", out var etag))
                throw new BillingException(503, "billing_storage_response", "Billing storage did not return a concurrency token.");
            item.ETag = new ETag(etag);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 412) { return false; }
    }
    public async Task<BillingPage<BillingInbox>> EventsAsync(string? cursor, CancellationToken ct)
    {
        var table = await GetTableAsync(ct);
        var partition = _options.Environment + "-Inbox";
        var now = DateTimeOffset.UtcNow;
        await foreach (var page in table.QueryAsync<BillingInbox>(e => e.PartitionKey == partition
            && !e.Completed && e.RetryAt <= now, maxPerPage: 32, cancellationToken: ct).AsPages(cursor, 32))
            return new(page.Values, page.ContinuationToken);
        return new(Array.Empty<BillingInbox>(), null);
    }
    public async Task<BillingPage<BillingAccount>> AccountsAsync(string? cursor, CancellationToken ct)
    {
        var table = await GetTableAsync(ct);
        var partition = _options.Environment;
        var now = DateTimeOffset.UtcNow;
        await foreach (var page in table.QueryAsync<BillingAccount>(a => a.PartitionKey == partition
            && a.Kind == "Account" && a.RetryAt <= now, maxPerPage: 32, cancellationToken: ct).AsPages(cursor, 32))
            return new(page.Values, page.ContinuationToken);
        return new(Array.Empty<BillingAccount>(), null);
    }
}
