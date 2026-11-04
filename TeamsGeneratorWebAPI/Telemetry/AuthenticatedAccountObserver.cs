using System.Globalization;
using System.Security.Claims;
using Azure;
using Microsoft.Extensions.Caching.Memory;
using TeamsGeneratorWebAPI.Authentication;

namespace TeamsGeneratorWebAPI.Telemetry;

public sealed class AuthenticatedAccountObserver : IDisposable
{
    private readonly AccountObservationStore _store;
    private readonly IRegistrationStore _registrations;
    private readonly IFirebaseAccountLookup _lookup;
    private readonly IUsageTelemetry _telemetry;
    private readonly TimeProvider _clock;
    private readonly ILogger<AuthenticatedAccountObserver> _logger;
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 10000 });
    private readonly SemaphoreSlim[] _gates = Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1)).ToArray();

    public AuthenticatedAccountObserver(AccountObservationStore store, IRegistrationStore registrations,
        IFirebaseAccountLookup lookup, IUsageTelemetry telemetry, TimeProvider clock,
        ILogger<AuthenticatedAccountObserver> logger)
    {
        _store = store;
        _registrations = registrations;
        _lookup = lookup;
        _telemetry = telemetry;
        _clock = clock;
        _logger = logger;
    }

    public async Task ObserveAsync(ClaimsPrincipal principal, string idToken, string? clientVersion,
        CancellationToken requestAborted)
    {
        if (UsageMetrics.IsVerificationClient(clientVersion)) return;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(requestAborted);
        budget.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            var uid = RequestUserId.Resolve(principal, string.Empty);
            if (!RequestUserId.IsAuthenticatedOrganizer(principal, uid))
            {
                Failure("invalid_identity", clientVersion);
                return;
            }
            var account = UsageTelemetry.CreateAnonymousUserId(uid);
            var gate = _gates[(int)(Convert.ToUInt32(account[..8], 16) % (uint)_gates.Length)];
            await gate.WaitAsync(budget.Token);
            try { await ObserveCoreAsync(principal, uid, account, idToken, clientVersion, budget.Token); }
            finally { gate.Release(); }
        }
        // The analytics boundary is deliberately fail-open, never the identity/authorization write.
        // Do not log exceptions: network/provider messages can include the token, key, or profile.
        catch (Exception exception)
        {
            Failure(exception switch
            {
                OperationCanceledException => requestAborted.IsCancellationRequested ? "request_cancelled" : "timeout",
                RequestFailedException => "storage_unavailable",
                HttpRequestException => "lookup_transport",
                System.Text.Json.JsonException => "lookup_invalid_json",
                _ => "observation_failed"
            }, clientVersion);
        }
    }

    private async Task ObserveCoreAsync(ClaimsPrincipal principal, string uid, string account, string token,
        string? version, CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow().ToUniversalTime();
        var date = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var dailyKey = UsageMetrics.ResourceKey("authenticated-day", uid, date);
        await ProjectAsync("AccountAuthenticatedObserved", dailyKey, uid, account, version, now,
            new() { ["observation_key"] = dailyKey, ["activity_date"] = date, ["observed_at"] = now.ToString("O") },
            cancellationToken);

        var authClaims = principal.FindAll("auth_time").ToArray();
        if (authClaims.Length == 1
            && long.TryParse(authClaims[0].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            && seconds >= 0 && seconds <= now.ToUnixTimeSeconds())
        {
            var signin = UsageMetrics.ResourceKey("signin", uid, seconds.ToString(CultureInfo.InvariantCulture));
            await ProjectAsync("AccountSignInObserved", signin, uid, account, version, now,
                new() { ["signin_key"] = signin, ["authenticated_at"] = DateTimeOffset.FromUnixTimeSeconds(seconds).ToString("O") },
                cancellationToken);
        }
        else if (!Cached("invalid-auth:" + account))
        {
            Failure("invalid_auth_time", version);
            Cache("invalid-auth:" + account, now.AddHours(1));
        }

        var metadataKey = "metadata:" + account;
        if (Cached(metadataKey)) return;
        var metadata = await _store.ReadOrCreateAsync(AccountObservationStore.MetadataPartition,
            account, now, cancellationToken);
        var firstSeen = metadata.GetDateTimeOffset("FirstObservedAt")!.Value;
        _telemetry.Track("AccountFirstSeenObserved", version, uid, Properties(account,
            new() { ["first_seen_at"] = firstSeen.ToString("O"), ["creation_status"] = "not_inferred" }));

        var createdAt = metadata.GetDateTimeOffset("CreatedAt");
        if (createdAt == null && await _store.ReserveLookupAsync(metadata, now, cancellationToken))
        {
            var result = await _lookup.LookupAsync(uid, token, now, cancellationToken);
            if (result.CreatedAt is { } verified && verified >= DateTimeOffset.UnixEpoch && verified <= now
                && result.Status == "verified")
            {
                await _store.SaveCreationAsync(metadata, verified, cancellationToken);
                createdAt = verified;
            }
            else Failure(result.Status == "verified" ? "invalid_creation_metadata" : result.Status, version);
        }
        if (createdAt is { } creation)
        {
            var fact = await _registrations.GetOrCreateAsync(new RegistrationFact(
                UsageMetrics.ResourceKey("registration", uid), creation, firstSeen, "unknown",
                "firebase_accounts_lookup"), cancellationToken);
            _telemetry.Track("AccountRegistrationObserved", version, uid, Properties(account, new()
            {
                ["registration_version"] = "2", ["registration_key"] = fact.AccountKey,
                ["created_at"] = fact.CreatedAt.ToString("O"),
                ["first_reported_at"] = fact.FirstReportedAt.ToString("O"),
                ["registration_method"] = fact.Method, ["registration_source"] = fact.Source,
                ["outcome"] = "succeeded"
            }));
        }
        Cache(metadataKey, now.AddMinutes(5));
    }

    private async Task ProjectAsync(string eventName, string key, string uid, string account,
        string? version, DateTimeOffset now, Dictionary<string, string?> fields, CancellationToken cancellationToken)
    {
        if (Cached(key)) return;
        var fact = await _store.ReadOrCreateAsync(AccountObservationStore.FactPartition, key, now,
            cancellationToken, Properties(account, fields));
        fields["first_observed_at"] = fact.GetDateTimeOffset("FirstObservedAt")!.Value.ToString("O");
        _telemetry.Track(eventName, version, uid, Properties(account, fields));
        // Re-project durable facts after restart/eviction. Telemetry is not a transactional
        // receipt; consumers deduplicate the stable key, not the number of emitted events.
        Cache(key, new DateTimeOffset(now.UtcDateTime.Date.AddDays(1), TimeSpan.Zero));
    }

    private static Dictionary<string, string?> Properties(string account, Dictionary<string, string?> fields)
    {
        fields["account_key"] = account;
        fields["identity_source"] = "authenticated";
        fields["coverage"] = "observed_authenticated_backend";
        fields["auth_observation_version"] = "1";
        return fields;
    }

    private bool Cached(string key) =>
        _cache.TryGetValue(key, out DateTimeOffset expiry) && expiry > _clock.GetUtcNow();

    private void Cache(string key, DateTimeOffset expiry) =>
        _cache.Set(key, expiry, new MemoryCacheEntryOptions { Size = 1,
            AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(1) });

    private void Failure(string category, string? version)
    {
        _logger.LogWarning("Authenticated account observation incomplete: {Category}.", category);
        try
        {
            _telemetry.Track("AccountObservationFailed", version,
                properties: new Dictionary<string, string?> { ["failure_category"] = category,
                    ["coverage"] = "observed_authenticated_backend" });
        }
        catch (Exception)
        {
            _logger.LogWarning("Authenticated account observation failure telemetry unavailable.");
        }
    }

    public void Dispose()
    {
        _cache.Dispose();
        foreach (var gate in _gates) gate.Dispose();
    }
}
