using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TeamsGeneratorWebAPI.Telemetry;

internal static class AuthenticatedAccountRegression
{
    public static async Task Run(Action<bool, string> check)
    {
        var now = DateTimeOffset.Parse("2026-10-06T23:59:59Z", CultureInfo.InvariantCulture);
        var clock = new Clock { Now = now };
        var table = new BillingStorageRegression.MemoryTable();
        var events = new Events();
        var logger = new Logs();
        var lookup = new Lookup { Result = new(now.AddYears(-2), "verified") };
        var store = new AccountObservationStore(table);
        var probe = await store.ReadOrCreateAsync(AccountObservationStore.MetadataPartition, "etag-probe", now, default);
        check(await store.ReserveLookupAsync(probe, now, default), "lookup reservation succeeds");
        var reserved = (await table.GetEntityIfExistsAsync<TableEntity>(AccountObservationStore.MetadataPartition, "etag-probe")).Value;
        check(probe.ETag == reserved.ETag, $"reservation ETag matches: {probe.ETag} / {reserved.ETag}");
        await store.SaveCreationAsync(probe, now, default);
        AuthenticatedAccountObserver Observer() => new(store, new RegistrationStore(table), lookup, events, clock, logger);
        using var observer = Observer();
        var user = Principal(now.AddDays(-5).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        await observer.ObserveAsync(user, "PRIVATE_TOKEN", "release", default);
        check(lookup.Calls == 1 && events.Of("AccountAuthenticatedObserved").Single()["activity_date"] == "2026-10-06",
            "authenticated day uses backend UTC date");
        var signin = events.Of("AccountSignInObserved").Single();
        check(signin["authenticated_at"] == now.AddDays(-5).ToString("O"),
            "first observed old sign-in remains on actual auth date, never deployment date");
        var durableSignin = (await table.GetEntityIfExistsAsync<TableEntity>(
            AccountObservationStore.FactPartition, signin["signin_key"]!)).Value;
        check(durableSignin.GetString("authenticated_at") == signin["authenticated_at"]
            && durableSignin.GetString("account_key") == UsageTelemetry.CreateAnonymousUserId("test-account"),
            "durable sign-in preserves occurrence date and account pseudonym for retrospective aggregation");
        check(events.Of("AccountRegistrationObserved").Any(), "creation projection: " + string.Join("; ", logger.Messages));
        check(events.Of("AccountRegistrationObserved").Single()["created_at"] == now.AddYears(-2).ToString("O")
            && events.Of("AccountRegistrationObserved").Single()["registration_source"] == "firebase_accounts_lookup",
            "old existing account registers retrospectively only on provider creation date");
        check(events.Of("AccountFirstSeenObserved").Single()["first_seen_at"] == now.ToString("O"),
            "first-seen remains distinct from actual creation");
        var firstCount = events.Items.Count;
        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => observer.ObserveAsync(user, "REFRESHED_PRIVATE_TOKEN", "release", default)));
        check(events.Items.Count == firstCount && lookup.Calls == 1, "repeats and refreshed tokens share cached auth_time and metadata");
        using (var restarted = Observer())
            await restarted.ObserveAsync(user, "PRIVATE_TOKEN", "release", default);
        check(lookup.Calls == 1 && events.Of("AccountSignInObserved").Select(e => e["signin_key"]).Distinct().Count() == 1,
            "restart reprojects the same durable sign-in without another provider lookup");
        var nextSignin = Principal(now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        await observer.ObserveAsync(nextSignin, "PRIVATE_TOKEN", "release", default);
        check(events.Of("AccountSignInObserved").Select(e => e["signin_key"]).Distinct().Count() == 2,
            "new verified auth_time is a distinct sign-in despite same UID");
        clock.Now = now.AddSeconds(1);
        await observer.ObserveAsync(user, "PRIVATE_TOKEN", "release", default);
        check(events.Of("AccountAuthenticatedObserved").Select(e => e["activity_date"]).Distinct().Count() == 2
            && events.Of("AccountSignInObserved").Select(e => e["signin_key"]).Distinct().Count() == 2,
            "midnight counts a new active day, not a refreshed-token sign-in");
        check(!JsonSerializer.Serialize(events.Items.Select(e => e.Properties)).Contains("PRIVATE")
            && !JsonSerializer.Serialize(events.Items.Select(e => e.Properties)).Contains("test-account")
            && logger.Messages.All(m => !m.Contains("PRIVATE")), "event properties and logs contain no UID, token or profile");
        var count = events.Items.Count;
        await observer.ObserveAsync(Principal("1", "verification-account"), "PRIVATE_TOKEN", " METRICS-VERIFICATION-load ", default);
        check(events.Items.Count == count && lookup.Calls == 1,
            "verification clients are excluded before all durable writes or lookups");
        count = events.Of("AccountAuthenticatedObserved").Count();
        await observer.ObserveAsync(new ClaimsPrincipal(new ClaimsIdentity()), "PRIVATE_TOKEN", "release", default);
        check(events.Of("AccountAuthenticatedObserved").Count() == count && logger.Messages.Last().Contains("invalid_identity"),
            "unauthenticated identities cannot enter authenticated observations");
        foreach (var value in new[] { null, "-1", "not-a-date", long.MaxValue.ToString(), now.AddDays(1).ToUnixTimeSeconds().ToString() })
        {
            var invalidUser = Principal(value, "invalid-" + (value ?? "missing"));
            var before = events.Of("AccountSignInObserved").Count();
            await observer.ObserveAsync(invalidUser, "PRIVATE_TOKEN", "release", default);
            check(events.Of("AccountSignInObserved").Count() == before && logger.Messages.Last().Contains("invalid_auth_time"),
                "missing, negative, malformed, out-of-range and future auth_time never count");
        }
        var duplicate = Principal("1", "duplicate-claims");
        ((ClaimsIdentity)duplicate.Identity!).AddClaim(new Claim("auth_time", "2"));
        count = events.Of("AccountSignInObserved").Count();
        await observer.ObserveAsync(duplicate, "PRIVATE_TOKEN", "release", default);
        check(events.Of("AccountSignInObserved").Count() == count, "ambiguous duplicate auth_time rejected");

        var unavailable = new Lookup { Result = new(null, "not_configured") };
        using var fallback = new AuthenticatedAccountObserver(store, new RegistrationStore(table),
            unavailable, events, clock, logger);
        var unknown = Principal("1", "unknown-created");
        var registrations = events.Of("AccountRegistrationObserved").Count();
        await fallback.ObserveAsync(unknown, "PRIVATE_TOKEN", "release", default);
        clock.Now = clock.Now.AddMinutes(6);
        await fallback.ObserveAsync(unknown, "PRIVATE_TOKEN", "release", default);
        check(unavailable.Calls == 1 && events.Of("AccountRegistrationObserved").Count() == registrations,
            "unknown creation is not registered and failed lookup has durable cooldown");
        clock.Now = clock.Now.AddHours(6);
        unavailable.Result = new(now.AddYears(-1), "verified");
        await fallback.ObserveAsync(unknown, "PRIVATE_TOKEN", "release", default);
        check(unavailable.Calls == 2 && events.Of("AccountRegistrationObserved").Count() == registrations + 1,
            "later metadata recovery registers on actual creation date");

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        count = events.Of("AccountAuthenticatedObserved").Count();
        await observer.ObserveAsync(Principal("1", "cancelled"), "PRIVATE_TOKEN", "release", cancelled.Token);
        check(events.Of("AccountAuthenticatedObserved").Count() == count && logger.Messages.Last().Contains("request_cancelled"),
            "request cancellation is bounded, classified, and never breaks authentication");
        events.Fail = true;
        await observer.ObserveAsync(Principal("1", "telemetry-failure"), "PRIVATE_TOKEN", "release", default);
        events.Fail = false;
        check(logger.Messages.Last().Contains("telemetry unavailable"), "projection failures cannot break auth or leak exception messages");
        await observer.ObserveAsync(Principal("1", "telemetry-failure"), "PRIVATE_TOKEN", "release", default);
        check(events.Of("AccountAuthenticatedObserved").Any(e => e["account_key"] == UsageTelemetry.CreateAnonymousUserId("telemetry-failure")),
            "insert-before-projection failure is recoverable");
        using var failedStore = new AuthenticatedAccountObserver(new AccountObservationStore(new FailingTable()),
            new RegistrationStore(table), lookup, events, clock, logger);
        await failedStore.ObserveAsync(user, "PRIVATE_TOKEN", "release", default);
        check(logger.Messages.Last().Contains("storage_unavailable"), "storage errors fail open with sanitized category");
        using var timed = new AuthenticatedAccountObserver(store, new RegistrationStore(table),
            new Lookup { Wait = true }, events, clock, logger);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        await timed.ObserveAsync(Principal("1", "timed"), "PRIVATE_TOKEN", "release", default);
        check(watch.Elapsed < TimeSpan.FromSeconds(5) && logger.Messages.Last().Contains("timeout"),
            "whole tracking path is bounded and preserves normal authentication");

        var racingLookup = new Lookup { Result = new(now.AddYears(-1), "verified"), Delay = true };
        var racing = Enumerable.Range(0, 12).Select(_ => new AuthenticatedAccountObserver(store,
            new RegistrationStore(table), racingLookup, events, clock, logger)).ToArray();
        await Task.WhenAll(racing.Select(o => o.ObserveAsync(Principal("1", "race"), "PRIVATE_TOKEN", "release", default)));
        foreach (var item in racing) item.Dispose();
        check(racingLookup.Calls == 1, "ETag lease deduplicates lookup across independent service instances");
        var raceKey = UsageMetrics.ResourceKey("registration", "race");
        await new RegistrationStore(table).GetOrCreateAsync(new(raceKey, now, now, "email_password"), default);
        check((await new RegistrationStore(table).GetOrCreateAsync(
            new(raceKey, now, now, "unknown", "firebase_accounts_lookup"), default)).CreatedAt == now.AddYears(-1),
            "legacy insert-only client assertion cannot poison server-verified creation facts");
        var clientFirstKey = UsageMetrics.ResourceKey("registration", "client-first");
        var registrationsStore = new RegistrationStore(table);
        var clientFact = await registrationsStore.GetOrCreateAsync(
            new(clientFirstKey, now, now, "email_password"), default);
        var serverFact = await registrationsStore.GetOrCreateAsync(
            new(clientFirstKey, now.AddYears(-3), now, "unknown", "firebase_accounts_lookup"), default);
        var clientRetry = await registrationsStore.GetOrCreateAsync(
            new(clientFirstKey, now.AddDays(-1), now, "google_redirect"), default);
        check(clientFact.AccountKey == serverFact.AccountKey && clientRetry.AccountKey == serverFact.AccountKey
            && serverFact.CreatedAt == now.AddYears(-3) && clientRetry.CreatedAt == now,
            "client-first and server observations share canonical registration key while preserving independent authoritative creation");
        await Provider(check, now);
        await ClaimMapping(check);
    }

    private static async Task ClaimMapping(Action<bool, string> check)
    {
        var now = DateTimeOffset.UtcNow;
        var clock = new Clock { Now = now };
        var signingKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
            System.Text.Encoding.UTF8.GetBytes("regression-only-not-a-production-signing-key"));
        const string issuer = "https://securetoken.google.com/regression-project";
        foreach (var mapClaims in new[] { true, false })
        {
            var handler = new Microsoft.IdentityModel.JsonWebTokens.JsonWebTokenHandler { MapInboundClaims = mapClaims };
            var token = handler.CreateToken(new Microsoft.IdentityModel.Tokens.SecurityTokenDescriptor
            {
                Issuer = issuer,
                Audience = "regression-project",
                IssuedAt = now.AddMinutes(-1).UtcDateTime,
                NotBefore = now.AddMinutes(-1).UtcDateTime,
                Expires = now.AddMinutes(5).UtcDateTime,
                SigningCredentials = new(signingKey, Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256),
                Claims = new Dictionary<string, object>
                {
                    ["sub"] = "mapped-account",
                    ["auth_time"] = now.AddMinutes(-2).ToUnixTimeSeconds()
                }
            });
            var validation = await handler.ValidateTokenAsync(token, new Microsoft.IdentityModel.Tokens.TokenValidationParameters
            {
                ValidIssuer = issuer, ValidAudience = "regression-project", IssuerSigningKey = signingKey,
                ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true, ValidateIssuerSigningKey = true
            });
            check(validation.IsValid
                && validation.ClaimsIdentity.FindFirst(mapClaims ? ClaimTypes.NameIdentifier : "sub")?.Value == "mapped-account",
                "real token validation exposes sub through the configured inbound mapping");
            var table = new BillingStorageRegression.MemoryTable();
            var events = new Events();
            using var observer = new AuthenticatedAccountObserver(new(table), new RegistrationStore(table),
                new Lookup(), events, clock, new Logs());
            await observer.ObserveAsync(new ClaimsPrincipal(validation.ClaimsIdentity), token, "regression", default);
            check(events.Of("AccountAuthenticatedObserved").Single()["account_key"] == UsageTelemetry.CreateAnonymousUserId("mapped-account")
                && events.Of("AccountSignInObserved").Single()["authenticated_at"] ==
                    DateTimeOffset.FromUnixTimeSeconds(now.AddMinutes(-2).ToUnixTimeSeconds()).ToString("O"),
                "mapped NameIdentifier and unmapped sub use the same validated account resolution and verified auth_time");
        }
    }

    private static async Task Provider(Action<bool, string> check, DateTimeOffset now)
    {
        var handler = new Handler();
        var factory = new Clients(handler);
        IConfiguration Config(string? key = "PRIVATE_KEY", string? origin = null) =>
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                { ["Firebase:WebApiKey"] = key, ["Firebase:AccountsOrigin"] = origin }).Build();
        var lookup = new FirebaseAccountLookup(factory, Config());
        handler.Body = "{\"users\":[{\"localId\":\"uid\",\"createdAt\":\"" + now.AddYears(-1).ToUnixTimeMilliseconds() + "\",\"email\":\"PRIVATE_EMAIL\"}]}";
        var result = await lookup.LookupAsync("uid", "PRIVATE_TOKEN", now, default);
        check(result.CreatedAt == now.AddYears(-1) && handler.ValidRequest,
            "lookup sends only current token to allowlisted provider and reads actual creation milliseconds");
        foreach (var origin in new[] { "http://identitytoolkit.googleapis.com", "https://evil.invalid",
            "https://identitytoolkit.googleapis.com.evil.invalid", "https://identitytoolkit.googleapis.com/private",
            "https://identitytoolkit.googleapis.com?key=other", "https://user@identitytoolkit.googleapis.com" })
            check((await new FirebaseAccountLookup(factory, Config(origin: origin)).LookupAsync("uid", "token", now, default)).Status == "invalid_origin",
                "unapproved origins, paths, userinfo and query strings are rejected before network access");
        var calls = handler.Calls;
        check((await new FirebaseAccountLookup(factory, Config(null)).LookupAsync("uid", "token", now, default)).Status == "not_configured"
            && handler.Calls == calls, "missing key never calls provider");
        foreach (var body in new[] { "{}", "null", "[]", "{\"users\":[]}", "{\"users\":[{\"localId\":\"other\",\"createdAt\":\"1\"}]}" })
        {
            handler.Body = body;
            check((await lookup.LookupAsync("uid", "token", now, default)).Status == "identity_mismatch",
                "provider account must match validated UID exactly");
        }
        foreach (var value in new[] { "null", "\"-1\"", "\"no\"", "1", "\"9223372036854775807\"", "\"" + now.AddSeconds(1).ToUnixTimeMilliseconds() + "\"" })
        {
            handler.Body = "{\"users\":[{\"localId\":\"uid\",\"createdAt\":" + value + "}]}";
            check((await lookup.LookupAsync("uid", "token", now, default)).Status == "invalid_creation_metadata",
                "invalid, missing and future creation metadata never falls back to JWT dates");
        }
        handler.Status = HttpStatusCode.Forbidden;
        check((await lookup.LookupAsync("uid", "token", now, default)).Status == "provider_denied",
            "provider permission errors are explicitly classified without response body logging");
        handler.Status = HttpStatusCode.Redirect;
        check((await lookup.LookupAsync("uid", "token", now, default)).Status == "provider_error", "redirect response is not metadata");
        handler.Status = HttpStatusCode.OK;
        handler.Body = "not json";
        try { await lookup.LookupAsync("uid", "token", now, default); check(false, "invalid JSON must not be metadata"); }
        catch (JsonException) { check(true, "malformed provider JSON surfaces to sanitized observer failure boundary"); }
        handler.Body = new string('x', 65537);
        try { await lookup.LookupAsync("uid", "token", now, default); check(false, "oversized JSON must be bounded"); }
        catch (HttpRequestException) { check(true, "provider response buffer rejects more than 64 KiB"); }
        var dependency = new DependencyTelemetry { Data = FirebaseAccountLookup.Origin + "?key=PRIVATE_KEY",
            Name = "PRIVATE_KEY", Target = FirebaseAccountLookup.Origin + "?key=PRIVATE_KEY" };
        dependency.Properties["secret"] = "PRIVATE_TOKEN";
        new FirebaseAccountTelemetryRedactor().Initialize(dependency);
        check(!dependency.Data.Contains("PRIVATE") && !dependency.Name.Contains("PRIVATE")
            && !dependency.Target.Contains("PRIVATE") && dependency.Properties.Count == 0,
            "dependency auto-instrumentation redacts API key and arbitrary properties");
        handler.Status = HttpStatusCode.OK;
        handler.Wait = true;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        try { await lookup.LookupAsync("uid", "token", now, default); check(false, "lookup must time out"); }
        catch (OperationCanceledException) { check(watch.Elapsed < TimeSpan.FromSeconds(3), "provider lookup has one-second timeout"); }
    }

    private static ClaimsPrincipal Principal(string? authTime, string uid = "test-account")
    {
        var claims = new List<Claim> { new("user_id", uid), new("email", "PRIVATE_EMAIL"), new("iat", "999999") };
        if (authTime != null) claims.Add(new("auth_time", authTime));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
    }
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; }
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Lookup : IFirebaseAccountLookup
    {
        public int Calls;
        public bool Wait;
        public bool Delay;
        public AccountCreationLookup Result = new(null, "not_configured");
        public async Task<AccountCreationLookup> LookupAsync(string userId, string idToken, DateTimeOffset now, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            if (Wait) await Task.Delay(Timeout.Infinite, cancellationToken);
            if (Delay) await Task.Delay(30, cancellationToken);
            return Result;
        }
    }
    private sealed class Events : IUsageTelemetry
    {
        public readonly List<(string Name, Dictionary<string, string?> Properties)> Items = new();
        public bool Fail;
        public IEnumerable<Dictionary<string, string?>> Of(string name) => Items.Where(e => e.Name == name).Select(e => e.Properties);
        public void Track(string eventName, string? clientVersion = null, string? userId = null,
            IReadOnlyDictionary<string, string?>? properties = null, IReadOnlyDictionary<string, double>? measurements = null)
        {
            if (Fail) throw new InvalidOperationException("PRIVATE_TOKEN");
            lock (Items) Items.Add((eventName, properties!.ToDictionary(p => p.Key, p => p.Value)));
        }
    }
    private sealed class Logs : ILogger<AuthenticatedAccountObserver>
    {
        public readonly List<string> Messages = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (Messages) Messages.Add(formatter(state, exception));
        }
    }
    private sealed class FailingTable : TableClient
    {
        public override Task<NullableResponse<T>> GetEntityIfExistsAsync<T>(string partitionKey, string rowKey,
            IEnumerable<string>? select = null, CancellationToken cancellationToken = default) =>
            throw new RequestFailedException(503, "PRIVATE_TOKEN");
    }
    private sealed class Clients : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public Clients(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, false);
    }
    private sealed class Handler : HttpMessageHandler
    {
        public string Body = "{}";
        public HttpStatusCode Status = HttpStatusCode.OK;
        public int Calls;
        public bool ValidRequest;
        public bool Wait;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            ValidRequest = request.Method == HttpMethod.Post && request.RequestUri!.Host == "identitytoolkit.googleapis.com"
                && (await request.Content!.ReadAsStringAsync(cancellationToken)) == "{\"idToken\":\"PRIVATE_TOKEN\"}";
            if (Wait) await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage(Status) { Content = new StringContent(Body) };
        }
    }
}
