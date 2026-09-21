using Azure;
using Azure.Data.Tables;
using System.Security.Claims;
using System.Text;
using TeamsGeneratorWebAPI.Authentication;

namespace TeamsGeneratorWebAPI.Premium;

public interface IPremiumAccountStore
{
    Task UpdateVerifiedIdentityAsync(
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default);

    Task<bool> IsPremiumAsync(
        string userId,
        string? verifiedEmail = null,
        CancellationToken cancellationToken = default);
}

public sealed class PremiumAccountStore : IPremiumAccountStore
{
    internal const string AccountPartition = "Account";
    internal const string EmailPartition = "Email";
    internal const string IdentityPartition = "VerifiedIdentity";
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
            if (string.IsNullOrWhiteSpace(userId)) return false;
            var identity = await GetIdentityAsync(userId, cancellationToken);
            var emailRowKey = identity?.GetString("EmailRowKey");
            if (string.IsNullOrEmpty(emailRowKey)) return false;

            // The binding identifies the grant; it never snapshots Premium or its expiry.
            return IsActive(await GetAccountAsync(
                EmailPartition, emailRowKey, cancellationToken));
        }

        var emailAccount = await GetAccountAsync(
            EmailPartition,
            CreateEmailRowKey(verifiedEmail),
            cancellationToken);
        return IsActive(emailAccount);
    }

    public async Task UpdateVerifiedIdentityAsync(
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        var userId = RequestUserId.Resolve(user, string.Empty);
        if (!RequestUserId.IsAuthenticatedOrganizer(user, userId))
            throw new InvalidOperationException("A validated account identity is required.");

        var email = RequestUserId.ResolveVerifiedEmail(user);
        var emailRowKey = email == null ? string.Empty : CreateEmailRowKey(email);
        var existing = await GetIdentityAsync(userId, cancellationToken);
        if ((existing?.GetString("EmailRowKey") ?? string.Empty) == emailRowKey) return;

        // Keep identity separate from Account/Email grants, including when verification is removed.
        await _table.UpsertEntityAsync(
            new TableEntity(IdentityPartition, userId) { ["EmailRowKey"] = emailRowKey },
            TableUpdateMode.Replace,
            cancellationToken);
    }

    private async Task<TableEntity?> GetIdentityAsync(string userId, CancellationToken cancellationToken)
    {
        var response = await _table.GetEntityIfExistsAsync<TableEntity>(
            IdentityPartition, userId, cancellationToken: cancellationToken);
        return response.HasValue ? response.Value : null;
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
