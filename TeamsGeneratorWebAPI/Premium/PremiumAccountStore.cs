using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Caching.Memory;
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

public sealed class PremiumAccountStore : IPremiumAccountStore, IDisposable
{
    internal const string AccountPartition = "Account";
    internal const string EmailPartition = "Email";
    internal const string IdentityPartition = "VerifiedIdentity";
    private readonly TableClient _table;
    private readonly TimeProvider _clock;
    private readonly MemoryCache _identities = new(new MemoryCacheOptions { SizeLimit = 10000 });
    private readonly SemaphoreSlim[] _identityGates = Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1)).ToArray();
    private sealed record VerifiedIdentity(string EmailRowKey, DateTimeOffset ExpiresAt);

    public PremiumAccountStore(
        TableServiceClient tableServiceClient,
        IConfiguration configuration,
        TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;
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
        var gate = _identityGates[(uint)StringComparer.Ordinal.GetHashCode(userId) % (uint)_identityGates.Length];
        await gate.WaitAsync(cancellationToken);
        try
        {
            // Cache only successfully synchronized identity, never grants or entitlement decisions.
            if (_identities.TryGetValue(userId, out VerifiedIdentity? identity)
                && identity!.ExpiresAt > _clock.GetUtcNow() && identity.EmailRowKey == emailRowKey)
                return;
            _identities.Remove(userId);
            var existing = await GetIdentityAsync(userId, cancellationToken);
            if ((existing?.GetString("EmailRowKey") ?? string.Empty) != emailRowKey)
            {
                // Changed or removed verification must persist before this request can authenticate.
                await _table.UpsertEntityAsync(
                    new TableEntity(IdentityPartition, userId) { ["EmailRowKey"] = emailRowKey },
                    TableUpdateMode.Replace,
                    cancellationToken);
            }

            _identities.Set(userId, new VerifiedIdentity(emailRowKey, _clock.GetUtcNow().AddMinutes(5)),
                new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5) });
        }
        finally { gate.Release(); }
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

    public void Dispose()
    {
        _identities.Dispose();
        foreach (var gate in _identityGates) gate.Dispose();
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
