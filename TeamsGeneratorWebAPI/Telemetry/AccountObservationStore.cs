using Azure;
using Azure.Data.Tables;

namespace TeamsGeneratorWebAPI.Telemetry;

// Separate from VerifiedIdentity: its replace writes and entitlement grants must not
// overwrite analytics metadata (or acquire analytics-driven authorization behavior).
public sealed class AccountObservationStore
{
    public const string MetadataPartition = "AccountObservationMetadata";
    public const string FactPartition = "AccountObservationFact";
    private readonly TableClient _table;

    public AccountObservationStore(TableServiceClient tables, IConfiguration configuration)
        : this(tables.GetTableClient(configuration.GetValue("Premium:TableName", "PremiumAccounts")!)) { }

    public AccountObservationStore(TableClient table) => _table = table;

    public async Task<TableEntity> ReadOrCreateAsync(string partition, string key,
        DateTimeOffset observedAt, CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string?>? fields = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var existing = await _table.GetEntityIfExistsAsync<TableEntity>(
            partition, key, cancellationToken: cancellationToken);
        if (existing.HasValue) return existing.Value;
        try
        {
            var fact = new TableEntity(partition, key) { ["FirstObservedAt"] = observedAt };
            if (fields != null)
                foreach (var field in fields)
                    if (field.Value != null) fact[field.Key] = field.Value;
            await _table.AddEntityAsync(fact, cancellationToken);
        }
        catch (RequestFailedException exception) when (exception.Status == 409) { }
        var inserted = await _table.GetEntityIfExistsAsync<TableEntity>(
            partition, key, cancellationToken: cancellationToken);
        if (!inserted.HasValue) throw new RequestFailedException(503, "Account observation changed during retry.");
        return inserted.Value;
    }

    public async Task<bool> ReserveLookupAsync(TableEntity metadata, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (metadata.GetDateTimeOffset("CreatedAt") != null
            || metadata.GetDateTimeOffset("NextLookupAt") > now) return false;
        metadata["NextLookupAt"] = now.AddHours(6);
        try
        {
            var response = await _table.UpdateEntityAsync(metadata, metadata.ETag,
                TableUpdateMode.Replace, cancellationToken);
            if (!response.Headers.TryGetValue("ETag", out var etag))
                throw new RequestFailedException(503, "Account observation reservation has no ETag.");
            metadata.ETag = new ETag(etag);
            return true;
        }
        catch (RequestFailedException exception) when (exception.Status == 412) { return false; }
    }

    public async Task SaveCreationAsync(TableEntity metadata, DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        metadata["CreatedAt"] = createdAt;
        await _table.UpdateEntityAsync(metadata, metadata.ETag, TableUpdateMode.Replace, cancellationToken);
    }
}
