using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Azure;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TeamsGeneratorWebAPI.Controllers;
using TeamsGeneratorWebAPI.Premium;

internal static class BillingRegression
{
    public static async Task Run(Action<bool, string> check)
    {
        var now = DateTimeOffset.UtcNow;
        var id = "tfp_" + new string('a', 64);
        string Customer(string? expiry = null, string? grace = null, string? refund = null,
            string? cancelled = null, bool sandbox = false, string owner = "PURCHASED",
            string store = "play_store", string? originalId = null, string product = "teamify_premium:monthly",
            bool lifetime = false, bool entitlement = true, bool billingIssue = false, bool pause = false,
            bool transaction = true, string period = "normal") => JsonSerializer.Serialize(new
            {
                request_date_ms = now.ToUnixTimeMilliseconds(),
                subscriber = new
                {
                    original_app_user_id = originalId ?? id,
                    entitlements = entitlement ? new Dictionary<string, object> { ["teamify_premium"] = new
                    {
                        product_identifier = product, expires_date = lifetime ? null : expiry ?? now.AddDays(10).ToString("O"),
                        grace_period_expires_date = grace
                    }} : new Dictionary<string, object>(),
                    subscriptions = new Dictionary<string, object> { [product] = new
                    {
                        store, is_sandbox = sandbox, ownership_type = owner, period_type = period,
                        expires_date = lifetime ? null : expiry ?? now.AddDays(10).ToString("O"),
                        grace_period_expires_date = grace, refunded_at = refund,
                        unsubscribe_detected_at = cancelled,
                        billing_issues_detected_at = billingIssue ? now.ToString("O") : null,
                        auto_resume_date = pause ? now.AddMonths(1).ToString("O") : null,
                        store_transaction_id = transaction ? "GPA.1234-5678-9101-12131..2" : null
                    }}
                }
            });
        RevenueCatSnapshot Parse(string json, bool sandbox = false) => RevenueCatClient.Parse(json, id, sandbox, now);
        check(Parse(Customer()).Status == "active", "verified Play subscription is active");
        check(Parse(Customer(cancelled: now.ToString("O"))).ExpiresAt > now,
            "cancellation preserves paid period");
        check(Parse(Customer(pause: true)).ExpiresAt > now, "scheduled pause preserves paid period");
        check(Parse(Customer(expiry: now.AddMinutes(-1).ToString("O"), pause: true)).Status == "expired",
            "effective pause ends entitlement at expiry");
        check(Parse(Customer(expiry: now.AddMinutes(-1).ToString("O"), billingIssue: true,
            grace: now.AddHours(2).ToString("O"))).ExpiresAt > now, "provider-confirmed grace retains access");
        check(Parse(Customer(expiry: now.AddMinutes(-1).ToString("O"), billingIssue: true)).Status == "expired",
            "account hold without grace cannot extend access");
        check(Parse(Customer(refund: now.ToString("O"))).ExpiresAt == null, "refund revokes future-dated entitlement");
        check(Parse(Customer(sandbox: true)).ExpiresAt == null, "sandbox cannot grant production Premium");
        check(Parse(Customer(), true).ExpiresAt == null, "production cannot grant sandbox Premium");
        check(Parse(Customer(sandbox: true), true).ExpiresAt > now, "sandbox backend accepts sandbox only");
        foreach (var json in new[] { Customer(owner: "FAMILY_SHARED"), Customer(store: "promotional"),
            Customer(store: "app_store"), Customer(originalId: "public-firebase-uid"), Customer(lifetime: true),
            Customer(entitlement: false), Customer(product: "other_product"), Customer(transaction: false),
            Customer(period: "trial"), Customer(period: "intro") })
            check(Parse(json).ExpiresAt == null, "unsupported or unowned purchase fails closed");
        foreach (var json in new[] { "{}", """{"request_date_ms":1,"subscriber":{}}""", "invalid" })
        {
            try { Parse(json); throw new Exception("Invalid response accepted"); }
            catch (JsonException) { check(true, "malformed canonical response rejected"); }
        }

        var options = Options.Create(new BillingOptions { Enabled = true, AppId = "app_test",
            WebhookAuthorization = new string('x', 48), KeepOriginalAppUserIdConfirmed = true });
        var memory = new MemoryStore();
        var play = new PlayBillingRegression.Store();
        var service = new BillingService(memory, options, play);
        var account = await memory.BindAsync("uid", default);
        account.AppUserId = id;
        memory.Account = account;
        var verified = new PlayReceipt { Token = "verified", AccountKey = account.RowKey, ExpiresAt = now.AddDays(10),
            VerifiedAt = now, Order = "GPA.1234-5678-9101-12131..2", State = "active" };
        play.Receipts.Add(verified.Token, verified);
        account = await service.SyncAsync(account, default);
        check(service.IsActive(account, now), "paid state requires current server observation");
        check(!service.IsActive(await new BillingService(memory, options, new PlayBillingRegression.Store())
            .SyncAsync(account, default), now), "cached or RevenueCat-only paid state cannot grant access without Google ownership evidence");
        account = await service.SyncAsync(account, default);
        check(!service.IsActive(account, now.AddDays(2)), "stale server verification expires despite long paid period");
        check(!service.IsActive(account, now.AddDays(11)), "paid period ends without waiting for webhook");
        check((await service.SyncAsync(account, default)).ExpiresAt > now, "RevenueCat alias/event response cannot overwrite verified Google evidence");
        memory.ConflictWrites = 1;
        verified.VerifiedAt = now.AddSeconds(1);
        check(service.IsActive(await service.SyncAsync(account, default), now), "ETag collision is retried");
        memory.RejectOwnership = true;
        verified.Order = "GPA.2222-5678-9101-12131..0";
        account = await service.SyncAsync(account, default);
        check(account.Status == "ownership_conflict" && account.ExpiresAt == null,
            "purchase claimed by different account never grants access");
        memory.RejectOwnership = false;
        account = await service.SyncAsync(account, default);
        var manual = new ManualStore { Premium = true };
        var entitlementService = new AccountEntitlementService(new ConfigurationBuilder().Build(), manual, billing: service);
        check((await entitlementService.GetAsync("uid")).IsPremium, "manual OR paid grants Premium");
        manual.Premium = false;
        check((await entitlementService.GetAsync("uid")).IsPremium, "paid-only account receives server capabilities");
        check(service.IsActive(await service.SyncAsync(account, default), now),
            "RevenueCat outage does not strand the Google-verified rightful subscriber");
        var disabled = new BillingService(memory, Options.Create(new BillingOptions()));
        check(!disabled.IsActive(account, now) && await disabled.ReadAsync("uid", default) == null, "disabled billing does not activate paid records");
        manual.Premium = true;
        check((await new AccountEntitlementService(new ConfigurationBuilder().Build(), manual, billing: disabled)
            .GetAsync("uid")).IsPremium, "disabled paid billing preserves manual Premium");
        try { await disabled.BindAsync("uid", default); throw new Exception("Disabled purchase accepted"); }
        catch (BillingException ex) { check(ex.Code == "billing_disabled", "disabled binding is explicit failure"); }

        var controller = new BillingController(service, memory, entitlementService, options)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        foreach (var method in new[] { nameof(BillingController.Binding), nameof(BillingController.Status), nameof(BillingController.Sync) })
            check(typeof(BillingController).GetMethod(method)!.IsDefined(typeof(AuthorizeAttribute), true),
                method + " requires Firebase authentication");
        check(((ObjectResult)await controller.Binding(default)).StatusCode == 401, "client query UID cannot replace verified principal");
        controller.HttpContext.User = new(new ClaimsIdentity(new[] { new Claim("user_id", "uid"),
            new Claim("firebase", """{ "sign_in_provider": "anonymous" }""") }, "test"));
        check(((ObjectResult)await controller.Binding(default)).StatusCode == 401, "Firebase anonymous session is not a billing account");
        controller.HttpContext.User = new(new ClaimsIdentity(new[] { new Claim("user_id", "uid") }, "test"));
        check(await controller.Status(default) is OkObjectResult, "signed-in status works independently of checkout");
        async Task<IActionResult> Webhook(string json, string? auth = null, string scheme = "https")
        {
            controller.Request.Scheme = scheme;
            controller.Request.Headers.Authorization = auth ?? options.Value.WebhookAuthorization;
            controller.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(json));
            return await controller.Webhook(default);
        }
        string Event(string eventId = "event-1", string type = "INITIAL_PURCHASE", string app = "app_test",
            string environment = "PRODUCTION") => JsonSerializer.Serialize(new { api_version = "1.0", @event = new
            { id = eventId, type, app_id = app, environment, app_user_id = id, aliases = new[] { id, "unknown" },
                transferred_from = new[] { id }, transferred_to = new[] { "unknown" }, expires_at_ms = long.MaxValue } });
        check(await Webhook(Event(), "wrong") is UnauthorizedResult, "unauthenticated webhook is rejected");
        check(await Webhook(Event(), scheme: "http") is BadRequestObjectResult, "webhook requires HTTPS");
        check(await Webhook(Event(app: "wrong")) is BadRequestObjectResult, "wrong app webhook rejected");
        check(await Webhook(Event(environment: "SANDBOX")) is OkObjectResult && memory.Inbox.Count == 0,
            "sandbox webhook ignored by production without writes");
        check(await Webhook("{") is BadRequestObjectResult, "malformed webhook rejected");
        check(await Webhook(Event()) is OkObjectResult && memory.Inbox.Count == 1, "webhook acknowledged only after durable acceptance");
        check(!memory.Inbox.Values.First().CustomerIdsJson.Contains("expires_at"), "webhook stores only minimal identity references, not receipts or supplied entitlement");
        await Webhook(Event());
        check(memory.Inbox.Count == 1, "duplicate webhook acceptance is idempotent");
        memory.FailAccept = true;
        check(((ObjectResult)await Webhook(Event("event-2"))).StatusCode == 503, "storage failure is not acknowledged");
        memory.FailAccept = false;
        var worker = new BillingWorker(memory, service, NullLogger<BillingWorker>.Instance);
        verified.ExpiresAt = null;
        verified.State = "expired";
        await worker.ProcessEventAsync(memory.Inbox.Values.First(), default);
        check(memory.Inbox.Values.First().Completed && memory.Account!.ExpiresAt == null,
            "webhook re-fetches canonical refund instead of trusting event expiry");
        await Webhook(Event("event-transfer", type: "TRANSFER"));
        memory.FailSave = true;
        verified.VerifiedAt = now.AddSeconds(2);
        var transfer = memory.Inbox.Values.Single(e => !e.Completed);
        await worker.ProcessEventAsync(transfer, default);
        check(!transfer.Completed && transfer.RetryAt > now, "failed processing remains durably retryable");
        memory.FailSave = false;
        var dependency = new DependencyTelemetry { Target = "api.revenuecat.com", Data = "https://api.revenuecat.com/v1/subscribers/" + id };
        new BillingTelemetryRedactor().Initialize(dependency);
        check(!dependency.Data.Contains(id) && !dependency.Name.Contains(id), "opaque billing identities redacted from dependency telemetry");
        check(BillingController.ValidAuthorization(options.Value.WebhookAuthorization, options.Value.WebhookAuthorization)
            && !BillingController.ValidAuthorization("wrong", options.Value.WebhookAuthorization)
            && !BillingController.ValidAuthorization("", ""), "webhook authentication cannot be enabled with empty secret");
        foreach (var enabled in new[] { false, true })
        {
            var services = new ServiceCollection();
            services.AddPremiumBilling(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                { ["Billing:Enabled"] = enabled.ToString() }).Build());
            using var container = services.BuildServiceProvider();
            try
            {
                var settings = container.GetRequiredService<IOptions<BillingOptions>>().Value;
                check(!enabled && !settings.Enabled, "unconfigured billing is safely disabled");
            }
            catch (OptionsValidationException) { check(enabled, "enabled billing rejects missing credentials/policy"); }
        }
        await PlayBillingRegression.Run(check);
        await BillingSnapshotRegression.Run(check);
    }

    private sealed class ManualStore : IPremiumAccountStore
    {
        public bool Premium { get; set; }
        public Task<bool> IsPremiumAsync(string uid, string? email = null, CancellationToken ct = default) => Task.FromResult(Premium);
        public Task UpdateVerifiedIdentityAsync(ClaimsPrincipal user, CancellationToken ct = default) => Task.CompletedTask;
    }
    internal sealed class MemoryStore : IBillingStore
    {
        public BillingAccount? Account { get; set; }
        public Dictionary<string, BillingInbox> Inbox { get; } = new();
        public bool RejectOwnership { get; set; }
        public bool FailAccept { get; set; }
        public bool FailSave { get; set; }
        public int ConflictWrites { get; set; }
        public int ReadCalls { get; set; }
        public int SaveCalls { get; set; }
        public Action? BeforeSave { get; set; }
        public Task<BillingAccount?> ReadAsync(string key, CancellationToken ct)
        {
            ReadCalls++;
            return Task.FromResult(Account == null || Account.RowKey != key ? null : JsonSerializer.Deserialize<BillingAccount>(JsonSerializer.Serialize(Account)));
        }
        public Task<BillingAccount> BindAsync(string uid, CancellationToken ct)
        {
            Account ??= new BillingAccount { RowKey = TableBillingStore.AccountKey(uid), AppUserId = "tfp_" + new string('a', 64) };
            return Task.FromResult(Account);
        }
        public Task<BillingAccount?> FindCustomerAsync(string id, CancellationToken ct) =>
            id == Account?.AppUserId ? ReadAsync(Account.RowKey, ct) : Task.FromResult<BillingAccount?>(null);
        public Task<bool> SaveAsync(BillingAccount account, string? transaction, CancellationToken ct)
        {
            SaveCalls++;
            BeforeSave?.Invoke();
            if (FailSave) throw new BillingException(503, "billing_storage_unavailable", "Injected outage");
            if (transaction != null && RejectOwnership) throw new BillingException(409, "billing_ownership_conflict", "Injected conflict");
            if (ConflictWrites-- > 0) return Task.FromResult(false);
            Account = account;
            return Task.FromResult(true);
        }
        public Task AcceptAsync(BillingInbox item, CancellationToken ct)
        {
            if (FailAccept) throw new RequestFailedException(503, "Injected storage failure");
            Inbox.TryAdd(item.RowKey, item);
            return Task.CompletedTask;
        }
        public Func<BillingInbox, bool>? InboxWrite { get; set; }
        public Task<bool> SaveInboxAsync(BillingInbox item, CancellationToken ct)
        {
            if (InboxWrite?.Invoke(item) == false) return Task.FromResult(false);
            Inbox[item.RowKey] = item;
            return Task.FromResult(true);
        }
        public Task<BillingPage<BillingInbox>> EventsAsync(string? cursor, CancellationToken ct) => Task.FromResult(new BillingPage<BillingInbox>(Inbox.Values.ToArray(), null));
        public Task<BillingPage<BillingAccount>> AccountsAsync(string? cursor, CancellationToken ct) =>
            Task.FromResult(new BillingPage<BillingAccount>(Account == null ? Array.Empty<BillingAccount>() : new[] { Account }, null));
    }
}
