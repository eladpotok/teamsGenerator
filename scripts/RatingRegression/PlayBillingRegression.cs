using System.Text.Json;
using Azure;
using Microsoft.Extensions.Options;
using TeamsGeneratorWebAPI.Premium;
using TeamsGeneratorWebAPI.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

internal static class PlayBillingRegression
{
    public static async Task Run(Action<bool, string> check)
    {
        var now = DateTimeOffset.UtcNow;
        var owner = new BillingAccount { RowKey = TableBillingStore.AccountKey("owner"), AppUserId = "tfp_" + new string('a', 64) };
        var store = new Store();
        store.Accounts.Add(TableBillingStore.Hash(owner.AppUserId), owner);
        var google = new Client
        {
            Purchase = new(TableBillingStore.Hash(owner.AppUserId), null, "GPA.1234-5678-9101-12131..0",
                "SUBSCRIPTION_STATE_ACTIVE", now.AddMonths(1), false, false)
        };
        var options = Options.Create(new BillingOptions { Enabled = true, RecoveryEnabled = true });
        PlayPurchaseRecovery Recovery() => new(store, google, options);
        var initial = await Recovery().ReceiveAsync("receipt-from-another-signed-in-user", default);
        var recovered = await Recovery().ProcessAsync(initial, default);
        check(recovered.AccountKey == owner.RowKey && google.ImportedOwner == owner.AppUserId,
            "first recovery uses Google initiating account, never current Teamify user");
        check(google.Acknowledgements == 1 && store.Claims.Count == 1 && recovered.ExpiresAt > now,
            "verified receipt is durably claimed, acknowledged and mirrored to RevenueCat");
        check(store.Receipts.Values.Single().VerifiedAt != null, "receipt evidence survives service recreation");
        var controller = new PlayBillingController(Recovery(), options)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        check(typeof(PlayBillingController).GetMethod(nameof(PlayBillingController.Recover))!.IsDefined(typeof(AuthorizeAttribute), true)
            && typeof(PlayBillingController).GetMethod(nameof(PlayBillingController.Resolution))!.IsDefined(typeof(AuthorizeAttribute), true),
            "recovery and terminal-resolution endpoints require Firebase authentication");
        check(((ObjectResult)await controller.Recover(new("receipt"), default)).StatusCode == 401, "unauthenticated token cannot choose an owner");
        controller.HttpContext.User = new(new ClaimsIdentity(new[] { new Claim("user_id", "owner"),
            new Claim("firebase", """{"sign_in_provider":"anonymous"}""") }, "test"));
        check(((ObjectResult)await controller.Recover(new("receipt"), default)).StatusCode == 401, "anonymous account cannot use purchase recovery");
        controller.HttpContext.User = new(new ClaimsIdentity(new[] { new Claim("user_id", "different-user") }, "test"));
        var conflict = await controller.Recover(new(initial.Token), default) as ConflictObjectResult;
        check(conflict != null && !JsonSerializer.Serialize(conflict.Value).Contains(owner.AppUserId),
            "different signed-in caller recovers rightful owner and gets only a private ownership conflict");
        controller.HttpContext.User = new(new ClaimsIdentity(new[] { new Claim("user_id", "owner") }, "test"));
        check(await controller.Recover(new(initial.Token), default) is OkObjectResult, "rightful owner recovers without client identity claims");
        controller.Request.Scheme = "https";
        check(await controller.Notifications(default) is UnauthorizedResult, "notification token injection requires signed Google push authentication");
        recovered = await Recovery().ProcessAsync(await store.ReadReceiptAsync(initial.Token, default) ?? throw new Exception(), default);
        check(recovered.AccountKey == owner.RowKey, "idempotent recovery survives restart without a device journal");

        google.Purchase = google.Purchase with { State = "SUBSCRIPTION_STATE_PENDING", ExpiresAt = null, Order = null };
        recovered = await Recovery().ProcessAsync(await Recovery().ReceiveAsync("pending", default), default);
        check(recovered.State == "pending" && recovered.ExpiresAt == null && !recovered.Completed,
            "pending purchases cannot grant access or stop durable recovery");
        var count = google.Acknowledgements;
        google.Purchase = google.Purchase with { State = "SUBSCRIPTION_STATE_ACTIVE", ExpiresAt = now.AddDays(30),
            Order = "GPA.2222-5678-9101-12131" };
        google.ImportFails = true;
        recovered = await Recovery().ProcessAsync(recovered, default);
        check(recovered.MirrorError == "billing_provider_unavailable" && !recovered.Completed,
            "provider outage is durably surfaced as retrying without denying verified Google entitlement");
        var afterFailure = (await store.ReadReceiptAsync("pending", default))!;
        check(afterFailure.ExpiresAt > now && afterFailure.AccountKey == owner.RowKey && google.Acknowledgements == count + 1,
            "RevenueCat outage after charge cannot strand rightful entitlement or erase the recovery token");
        google.ImportFails = false;
        recovered = await Recovery().ProcessAsync(afterFailure, default);
        check(recovered.ExpiresAt > now && recovered.MirrorError == null && google.ImportedOwner == owner.AppUserId,
            "mirror failure is retryable after restart");

        google.Purchase = google.Purchase with { AccountId = "public-uid" };
        count = google.Acknowledgements;
        try { await Recovery().ProcessAsync(await Recovery().ReceiveAsync("unknown-owner", default), default); check(false, "unknown owner"); }
        catch (BillingException ex) { check(ex.Code == "billing_ownership_unknown", "unknown original identity cannot become first caller's purchase"); }
        check(google.Acknowledgements == count, "unknown ownership never acknowledged or imported");
        google.Purchase = google.Purchase with { AccountId = TableBillingStore.Hash(owner.AppUserId), Sandbox = true };
        try { await Recovery().ProcessAsync(await Recovery().ReceiveAsync("sandbox", default), default); check(false, "wrong environment"); }
        catch (BillingException ex) { check(ex.Code == "billing_wrong_environment", "sandbox receipt cannot cross environment"); }
        google.Purchase = google.Purchase with { Sandbox = false, LinkedToken = "older" };
        store.Receipts["older"] = new PlayReceipt { Token = "older", AccountKey = "another-account" };
        try { await Recovery().ProcessAsync(await Recovery().ReceiveAsync("replacement", default), default); check(false, "chain ownership"); }
        catch (BillingException ex) { check(ex.Code == "billing_ownership_conflict", "linked subscription chain cannot transfer to a different owner"); }
        store.Receipts["older"] = new PlayReceipt { Token = "older", AccountKey = owner.RowKey, VerifiedAt = now, ExpiresAt = now.AddDays(10) };
        await Recovery().ProcessAsync(await Recovery().ReceiveAsync("valid-replacement", default), default);
        var old = (await store.ReadReceiptAsync("older", default))!;
        check(old.SupersededByToken == "valid-replacement" && old.ExpiresAt == null,
            "same-owner linked replacement durably revokes the prior token");
        old = await Recovery().ProcessAsync(await Recovery().ReceiveAsync("older", default), default);
        check(old.State == "replaced" && old.ExpiresAt == null && old.Completed,
            "replayed old-token notification cannot resurrect replaced entitlement");
        google.Purchase = google.Purchase with { LinkedToken = null, State = "SUBSCRIPTION_STATE_EXPIRED", ExpiresAt = null };
        recovered = await Recovery().ProcessAsync(await store.ReadReceiptAsync("pending", default) ?? throw new Exception(), default);
        check(recovered.ExpiresAt == null && recovered.Completed, "refund/expiry clears a previously future-dated canonical receipt");
        check(await Recovery().IsResolvedAsync("owner", TableBillingStore.Hash("pending"), default)
            && !await Recovery().IsResolvedAsync("different-user", TableBillingStore.Hash("pending"), default),
            "definitive terminal resolution is bound to both the original user and the exact prior receipt");
        recovered = await Recovery().ReceiveAsync("pending", default);
        check(!recovered.Completed, "a delayed/new notification reopens terminal receipt for canonical recheck");

        string GoogleJson(string state, bool sandbox = false, string? accountId = null, string product = "teamify_premium",
            string basePlan = "monthly", string? offer = null) => JsonSerializer.Serialize(new
        {
            subscriptionState = state, acknowledgementState = "ACKNOWLEDGEMENT_STATE_ACKNOWLEDGED",
            externalAccountIdentifiers = new { obfuscatedExternalAccountId = accountId ?? TableBillingStore.Hash(owner.AppUserId) },
            lineItems = new[] { new { productId = product, offerDetails = new { basePlanId = basePlan, offerId = offer },
                autoRenewingPlan = new { autoRenewEnabled = true }, expiryTime = now.AddDays(1).ToString("O"),
                latestSuccessfulOrderId = "GPA.1234-5678-9101-12131..3" } },
            testPurchase = sandbox ? new { } : null
        });
        foreach (var state in new[] { "ACTIVE", "IN_GRACE_PERIOD", "CANCELED" })
            check(PlayPurchaseClient.Parse(GoogleJson("SUBSCRIPTION_STATE_" + state)).ExpiresAt > now,
                "Google " + state + " preserves current paid/grace expiry");
        foreach (var state in new[] { "PENDING", "ON_HOLD", "PAUSED", "EXPIRED", "PENDING_PURCHASE_CANCELED" })
            check(PlayPurchaseClient.Parse(GoogleJson("SUBSCRIPTION_STATE_" + state)).ExpiresAt == null,
                "Google " + state + " denies access despite supplied future date");
        check(!PlayPurchaseClient.Parse(GoogleJson("SUBSCRIPTION_STATE_ACTIVE")).Sandbox
            && PlayPurchaseClient.Parse(GoogleJson("SUBSCRIPTION_STATE_ACTIVE", true)).Sandbox,
            "null Google testPurchase is production, object is sandbox");
        foreach (var state in new[] { "REFUNDED", "PARTIALLY_REFUNDED", "CANCELED", "PENDING_REFUND" })
            check(!PlayPurchaseClient.OrderPaid(JsonSerializer.Serialize(new { orderId = "GPA.order", purchaseToken = "token", state }),
                "GPA.order", "token"), "Google order " + state + " revokes even with future subscription expiry");
        check(PlayPurchaseClient.OrderPaid("""{"orderId":"GPA.order","purchaseToken":"token","state":"PROCESSED"}""",
            "GPA.order", "token"), "only processed matching order grants access");
        var resubscribe = System.Text.Json.Nodes.JsonNode.Parse(GoogleJson("SUBSCRIPTION_STATE_ACTIVE"))!;
        resubscribe["externalAccountIdentifiers"] = null;
        resubscribe["outOfAppPurchaseContext"] = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(new
        {
            expiredExternalAccountIdentifiers = new { obfuscatedExternalAccountId = TableBillingStore.Hash(owner.AppUserId) }
        }));
        check(PlayPurchaseClient.Parse(resubscribe.ToJsonString()).AccountId == TableBillingStore.Hash(owner.AppUserId),
            "Google out-of-app resubscribe recovers original identity from its expired account metadata");
        foreach (var json in new[] { GoogleJson("SUBSCRIPTION_STATE_ACTIVE", product: "other"),
            GoogleJson("SUBSCRIPTION_STATE_ACTIVE", basePlan: "weekly"), GoogleJson("SUBSCRIPTION_STATE_ACTIVE", offer: "trial"),
            GoogleJson("UNRECOGNIZED"), "{}" })
        {
            try { PlayPurchaseClient.Parse(json); check(false, "unsupported Google response"); }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException)
            { check(true, "unsupported Google purchase response fails closed"); }
        }
        await BillingReviewRegression.Run(check);
    }

    internal sealed class Client : IPlayPurchaseClient
    {
        public PlayPurchase Purchase { get; set; } = null!;
        public string? ImportedOwner { get; private set; }
        public bool ImportFails { get; set; }
        public int Acknowledgements { get; private set; }
        public Dictionary<string, PlayPurchase> Purchases { get; } = new();
        public List<string> VerifiedTokens { get; } = new();
        public Func<string, Exception?>? Failure { get; set; }
        public Func<string, Exception?>? AcknowledgmentFailure { get; set; }
        public Action<string>? BeforeAcknowledge { get; set; }
        public Task<PlayPurchase> GetAsync(string token, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            VerifiedTokens.Add(token);
            if (Failure?.Invoke(token) is { } failure) throw failure;
            return Task.FromResult(Purchases.TryGetValue(token, out var purchase) ? purchase : Purchase);
        }
        public Task AcknowledgeAsync(string token, string? accountId, string? profileId, CancellationToken ct)
        {
            BeforeAcknowledge?.Invoke(token);
            Acknowledgements++;
            if (AcknowledgmentFailure?.Invoke(token) is { } failure) throw failure;
            return Task.CompletedTask;
        }
        public Task ImportAsync(string token, string id, CancellationToken ct)
        {
            if (ImportFails) throw new BillingException(503, "billing_provider_unavailable", "Injected mirror outage");
            ImportedOwner = id;
            return Task.CompletedTask;
        }
    }

    internal sealed class Store : IPlayPurchaseStore
    {
        public Dictionary<string, BillingAccount> Accounts { get; } = new();
        public Dictionary<string, PlayReceipt> Receipts { get; } = new();
        public Dictionary<string, string> Claims { get; } = new();
        public int PageSize { get; set; } = 32;
        public List<string?> Cursors { get; } = new();
        public bool FailNextPage { get; set; }
        public Func<PlayReceipt, bool>? SaveAllowed { get; set; }
        private int _version;
        private static PlayReceipt Copy(PlayReceipt receipt)
        {
            var copy = JsonSerializer.Deserialize<PlayReceipt>(JsonSerializer.Serialize(receipt))!;
            copy.ETag = receipt.ETag;
            return copy;
        }
        public Task<PlayReceipt> ReceiveAsync(string token, CancellationToken ct)
        {
            if (!Receipts.TryGetValue(token, out var receipt)) receipt = new PlayReceipt { Token = token, ETag = new ETag((++_version).ToString()) };
            receipt.Completed = false;
            receipt.RetryAt = DateTimeOffset.UtcNow;
            Receipts[token] = Copy(receipt);
            return Task.FromResult(Copy(receipt));
        }
        public Task<PlayReceipt?> ReadReceiptAsync(string token, CancellationToken ct)
            => Task.FromResult(Receipts.TryGetValue(token, out var receipt) ? Copy(receipt) : null);
        public Task<PlayReceipt?> ReadReceiptKeyAsync(string key, CancellationToken ct)
            => Task.FromResult(Receipts.Values.FirstOrDefault(r => TableBillingStore.Hash(r.Token) == key) is { } receipt ? Copy(receipt) : null);
        public Task<BillingAccount?> FindPlayAccountAsync(string id, CancellationToken ct)
            => Task.FromResult(Accounts.TryGetValue(id, out var account) ? account : null);
        public Task<bool> SaveReceiptAsync(PlayReceipt receipt, CancellationToken ct)
        {
            if (SaveAllowed?.Invoke(receipt) == false) return Task.FromResult(false);
            if (Receipts.TryGetValue(receipt.Token, out var old) && old.ETag != receipt.ETag) return Task.FromResult(false);
            receipt.ETag = new ETag((++_version).ToString());
            Receipts[receipt.Token] = Copy(receipt);
            return Task.FromResult(true);
        }
        public Task ClaimAsync(PlayReceipt receipt, CancellationToken ct)
        {
            var root = receipt.Order!.Split("..")[0];
            if (Claims.TryGetValue(root, out var existing) && existing != receipt.AccountKey)
                throw new BillingException(409, "billing_ownership_conflict", "Claimed");
            Claims[root] = receipt.AccountKey!;
            return Task.CompletedTask;
        }
        public Task<PlayReceipt?> VerifiedOrderAsync(string order, string key, CancellationToken ct)
            => Task.FromResult(Receipts.Values.FirstOrDefault(r => r.Order == order && r.AccountKey == key));
        public Task<BillingPage<PlayReceipt>> ReceiptsAsync(string? cursor, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Cursors.Add(cursor);
            if (FailNextPage)
            {
                FailNextPage = false;
                throw new RequestFailedException(503, "Injected storage failure");
            }
            var due = Receipts.Values.Where(r => (!r.Completed || r.OwnerAlertPending) && r.RetryAt <= DateTimeOffset.UtcNow
                && (cursor == null || string.CompareOrdinal(r.Token, cursor) > 0)).OrderBy(r => r.Token, StringComparer.Ordinal).ToArray();
            var page = due.Take(PageSize).Select(Copy).ToArray();
            return Task.FromResult(new BillingPage<PlayReceipt>(page, due.Length > PageSize ? page.Last().Token : null));
        }
        public Task<IReadOnlyList<PlayReceipt>> AccountReceiptsAsync(string key, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<PlayReceipt>>(Receipts.Values.Where(r => r.AccountKey == key).Select(Copy).ToArray());
        public Task SupersedeAsync(string token, string replacement, string accountKey, CancellationToken ct)
        {
            if (!Receipts.TryGetValue(token, out var receipt)) receipt = new PlayReceipt { Token = token, AccountKey = accountKey };
            receipt.SupersededByToken = replacement;
            receipt.ExpiresAt = null;
            receipt.Completed = true;
            receipt.State = "replaced";
            Receipts[token] = receipt;
            return Task.CompletedTask;
        }
    }
}
