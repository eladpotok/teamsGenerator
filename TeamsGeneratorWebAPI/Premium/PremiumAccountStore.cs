using Azure;
using Azure.Data.Tables;
using System.Text;

namespace TeamsGeneratorWebAPI.Premium;

public interface IPremiumAccountStore
{
    Task<bool> IsPremiumAsync(
        string userId,
        string? verifiedEmail = null,
        CancellationToken cancellationToken = default);
}

public sealed class PremiumAccountStore : IPremiumAccountStore
{
    internal const string AccountPartition = "Account";
    internal const string EmailPartition = "Email";
    private readonly TableClient _table;

    public PremiumAccountStore(
        TableServiceClient tableServiceClient,
        IConfiguration configuration)
    {
        var tableName = configuration.GetValue(
            "Premium:TableName",
            "PremiumAccounts");
        _table = tableServiceClient.GetTableClient(tableName);
        _table.CreateIfNotExists();
    }

    public async Task<bool> IsPremiumAsync(
        string userId,
        string? verifiedEmail = null,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(userId))
        {
            var account = await GetAccountAsync(
                AccountPartition,
                userId,
                cancellationToken);
            if (IsActive(account))
            {
                return true;
            }
        }

        if (string.IsNullOrWhiteSpace(verifiedEmail))
        {
            return false;
        }

        var emailAccount = await GetAccountAsync(
            EmailPartition,
            CreateEmailRowKey(verifiedEmail),
            cancellationToken);
        return IsActive(emailAccount);
    }

    internal static string CreateEmailRowKey(string email)
    {
        var normalized = email.Trim().ToLowerInvariant();
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(normalized))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private async Task<PremiumAccountEntity?> GetAccountAsync(
        string partitionKey,
        string rowKey,
        CancellationToken cancellationToken)
    {
        var response =
            await _table.GetEntityIfExistsAsync<PremiumAccountEntity>(
                partitionKey,
                rowKey,
                cancellationToken: cancellationToken);
        return response.HasValue ? response.Value : null;
    }

    private static bool IsActive(PremiumAccountEntity? account)
    {
        return account is not null
            && account.IsPremium
            && (!account.ExpiresAt.HasValue
                || account.ExpiresAt.Value > DateTimeOffset.UtcNow);
    }
}

public sealed class PremiumAccountEntity : ITableEntity
{
    public string PartitionKey { get; set; } =
        PremiumAccountStore.AccountPartition;

    public string RowKey { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public bool IsPremium { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }

    public DateTimeOffset? Timestamp { get; set; }

    public ETag ETag { get; set; }
}
