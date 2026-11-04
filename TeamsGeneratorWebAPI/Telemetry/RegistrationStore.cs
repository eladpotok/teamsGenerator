using Azure;
using Azure.Data.Tables;

namespace TeamsGeneratorWebAPI.Telemetry;

public sealed record RegistrationFact(string AccountKey, DateTimeOffset CreatedAt,
    DateTimeOffset FirstReportedAt, string Method, string Source = "firebase_client_new_user");

public interface IRegistrationStore
{
    Task<RegistrationFact> GetOrCreateAsync(RegistrationFact fact, CancellationToken cancellationToken);
}

public sealed class RegistrationStore : IRegistrationStore
{
    public const string Partition = "RegistrationObserved";
    public const string VerifiedPartition = "RegistrationVerified";
    private readonly TableClient _table;

    public RegistrationStore(TableServiceClient tables, IConfiguration configuration)
        : this(tables.GetTableClient(configuration.GetValue("Premium:TableName", "PremiumAccounts")!)) { }

    public RegistrationStore(TableClient table) => _table = table;

    public async Task<RegistrationFact> GetOrCreateAsync(RegistrationFact fact, CancellationToken cancellationToken)
    {
        var partition = fact.Source == "firebase_accounts_lookup" ? VerifiedPartition : Partition;
        // The existing PremiumAccountStore initializes this table during bearer validation.
        // Insert-only: neither retries nor a competing report can rewrite the first fact.
        try
        {
            await _table.AddEntityAsync(new TableEntity(partition, fact.AccountKey)
            {
                ["CreatedAt"] = fact.CreatedAt,
                ["FirstReportedAt"] = fact.FirstReportedAt,
                ["Method"] = fact.Method,
                ["Source"] = fact.Source
            }, cancellationToken);
            return fact;
        }
        catch (RequestFailedException exception) when (exception.Status == 409)
        {
            var response = await _table.GetEntityIfExistsAsync<TableEntity>(
                partition, fact.AccountKey, cancellationToken: cancellationToken);
            if (!response.HasValue)
                throw new RequestFailedException(503, "Registration fact changed during retry.");
            var existing = response.Value;
            return new RegistrationFact(fact.AccountKey, existing.GetDateTimeOffset("CreatedAt")!.Value,
                existing.GetDateTimeOffset("FirstReportedAt")!.Value, existing.GetString("Method"), fact.Source);
        }
    }
}
