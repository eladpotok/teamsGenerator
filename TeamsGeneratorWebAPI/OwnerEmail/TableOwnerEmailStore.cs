using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Options;
using TeamsGeneratorWebAPI.Premium;

namespace TeamsGeneratorWebAPI.OwnerEmail;

public sealed class OwnerEmailItem : ITableEntity
{
    public string PartitionKey { get; set; } = "owner-email-v1";
    public string RowKey { get; set; } = "";
    public string Kind { get; set; } = "Mail";
    public string ContentHash { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Body { get; set; } = "";
    public string OperationId { get; set; } = Guid.NewGuid().ToString();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset RetryAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DispatchStartedAt { get; set; }
    public bool Completed { get; set; }
    public string Status { get; set; } = "queued";
    public int Attempts { get; set; }
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
}

public interface IOwnerEmailStore
{
    Task AcceptAsync(OwnerEmailItem item, string? userKey, CancellationToken ct);
    Task<BillingPage<OwnerEmailItem>> DueAsync(string? cursor, CancellationToken ct);
    Task<bool> SaveAsync(OwnerEmailItem item, CancellationToken ct);
}

public sealed class TableOwnerEmailStore : IOwnerEmailStore
{
    private readonly Lazy<TableClient> _table;
    public TableOwnerEmailStore(TableServiceClient tables, IOptions<OwnerEmailOptions> options)
        => _table = new(() => tables.GetTableClient(options.Value.TableName));

    // Provisioning creates the dedicated table; runtime cannot silently create or fall back to another store.
    public async Task AcceptAsync(OwnerEmailItem item, string? userKey, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 12; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var prior = await _table.Value.GetEntityIfExistsAsync<OwnerEmailItem>(item.PartitionKey, item.RowKey, cancellationToken: ct);
            if (prior.HasValue)
            {
                if (prior.Value.ContentHash != item.ContentHash)
                    throw new OwnerEmailException(409, "feedback_idempotency_conflict");
                return;
            }
            var actions = new List<TableTransactionAction> { new(TableTransactionActionType.Add, item) };
            if (userKey != null)
            {
                await AddQuotaAsync(actions, item.PartitionKey, "quota-user-" + userKey, 3, ct);
                await AddQuotaAsync(actions, item.PartitionKey, "quota-global", 100, ct);
            }
            try
            {
                // Outbox, global quota and user quota either all commit or none do, including across instances.
                await _table.Value.SubmitTransactionAsync(actions, ct);
                return;
            }
            catch (RequestFailedException ex) when (ex.Status is 409 or 412)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(10 * (attempt + 1)), ct);
            }
        }
        throw new OwnerEmailException(503, "feedback_storage_busy");
    }

    private async Task AddQuotaAsync(List<TableTransactionAction> actions, string partition, string key, int limit, CancellationToken ct)
    {
        var existing = await _table.Value.GetEntityIfExistsAsync<TableEntity>(partition, key, cancellationToken: ct);
        var row = existing.HasValue ? existing.Value : new TableEntity(partition, key);
        var now = DateTimeOffset.UtcNow;
        var reset = row.GetDateTimeOffset("ResetAt") ?? now;
        var count = reset <= now ? 0 : row.GetInt32("Count") ?? 0;
        if (count >= limit)
            throw new OwnerEmailException(429, "feedback_rate_limited", Math.Max(1, (int)Math.Ceiling((reset - now).TotalSeconds)));
        row["Kind"] = "Quota";
        row["ResetAt"] = reset <= now ? now.AddDays(1) : reset;
        row["Count"] = count + 1;
        actions.Add(new(existing.HasValue ? TableTransactionActionType.UpdateReplace : TableTransactionActionType.Add, row, row.ETag));
    }

    public async Task<BillingPage<OwnerEmailItem>> DueAsync(string? cursor, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        await foreach (var page in _table.Value.QueryAsync<OwnerEmailItem>(
            r => r.PartitionKey == "owner-email-v1" && r.Kind == "Mail" && !r.Completed && r.RetryAt <= now,
            maxPerPage: 16, cancellationToken: ct).AsPages(cursor, 16))
            return new(page.Values, page.ContinuationToken);
        return new(Array.Empty<OwnerEmailItem>(), null);
    }

    public async Task<bool> SaveAsync(OwnerEmailItem item, CancellationToken ct)
    {
        try
        {
            var response = await _table.Value.UpdateEntityAsync(item, item.ETag, TableUpdateMode.Replace, ct);
            if (!response.Headers.TryGetValue("ETag", out var etag))
                throw new OwnerEmailException(503, "email_storage_response");
            item.ETag = new ETag(etag);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 412) { return false; }
    }
}
