using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Azure;
using Azure.Core;
using Google.Apis.Auth.OAuth2;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using TeamsGeneratorWebAPI.Controllers;
using TeamsGeneratorWebAPI.OwnerEmail;
using TeamsGeneratorWebAPI.Premium;

internal static class OwnerEmailRegression
{
    public static async Task Run(Action<bool, string> check)
    {
        var now = DateTimeOffset.UtcNow;
        var settings = Options.Create(new OwnerEmailOptions
        {
            Enabled = true, FeedbackEnabled = true, PaidAlertsEnabled = true, PaidAlertsNotBefore = now.AddMinutes(-5),
            Endpoint = "https://example.communication.azure.com/", SenderAddress = "service@example.invalid",
            RecipientAddress = "owner@example.invalid"
        });
        var table = new BillingStorageRegression.MemoryTable();
        TableOwnerEmailStore Store() => new(new BillingStorageRegression.MemoryTables(table), settings);
        OwnerEmailService Service() => new(Store(), settings);
        var key = Guid.NewGuid().ToString();
        await Service().AcceptFeedbackAsync("verified-user", "reply@example.invalid", "feedback", "Useful feedback", key, default);
        var rowKey = "feedback-" + TableBillingStore.Hash("verified-user\n" + key);
        async Task<OwnerEmailItem> Read() => (await table.GetEntityIfExistsAsync<OwnerEmailItem>("owner-email-v1", rowKey)).Value;
        check((await Read()).Body.Contains("reply@example.invalid"), "safe verified reply identity is only in server-side mail content");
        await Service().AcceptFeedbackAsync("verified-user", null, "feedback", "Useful feedback", key, default);
        check((await table.GetEntityIfExistsAsync<Azure.Data.Tables.TableEntity>("owner-email-v1", "quota-global")).Value.GetInt32("Count") == 1,
            "restart and retry preserves immutable outbox and charges quota once");
        try { await Service().AcceptFeedbackAsync("verified-user", null, "feedback", "Different content", key, default); check(false, "conflicting retry"); }
        catch (OwnerEmailException ex) { check(ex.Status == 409, "same idempotency key cannot change accepted content"); }
        for (var i = 0; i < 2; i++)
            await Service().AcceptFeedbackAsync("verified-user", null, "question", "Question " + i, null, default);
        try { await Service().AcceptFeedbackAsync("verified-user", null, "question", "Fourth", null, default); check(false, "per-user quota"); }
        catch (OwnerEmailException ex) { check(ex.Status == 429 && ex.RetryAfter > 0, "persistent per-user quota returns retry delay"); }
        check((await table.GetEntityIfExistsAsync<Azure.Data.Tables.TableEntity>("owner-email-v1", "quota-global")).Value.GetInt32("Count") == 3,
            "rate rejection does not partially increment global quota");
        await Task.WhenAll(Enumerable.Range(0, 97).Select(i => Service().AcceptFeedbackAsync("global-" + i, null, "feedback", "Global message", null, default)));
        try { await Service().AcceptFeedbackAsync("another-user", null, "feedback", "Over global quota", null, default); check(false, "global quota"); }
        catch (OwnerEmailException ex) { check(ex.Status == 429, "multiple instances share atomic global quota"); }
        foreach (var message in new[] { "", "  ", new string('x', 4001), "bad\0control" })
        {
            try { await Service().AcceptFeedbackAsync("input-user", null, "feedback", message, null, default); check(false, "invalid message"); }
            catch (OwnerEmailException ex) { check(ex.Status == 400, "message validation rejects empty, oversized or control input"); }
        }
        check(!OwnerEmailOptions.SafeAddress("reply@example.invalid\r\nBcc: other@example.invalid")
            && !OwnerEmailOptions.SafeAddress("Name <reply@example.invalid>"), "verified address cannot inject email headers");
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            try { await Service().AcceptFeedbackAsync("cancelled", null, "feedback", "message", null, cancelled.Token); check(false, "cancelled"); }
            catch (OperationCanceledException) { check(true, "request cancellation is not converted into accepted"); }
        }
        var controller = new FeedbackController(Service())
        {
            ControllerContext = new() { HttpContext = new DefaultHttpContext() }
        };
        check(typeof(FeedbackController).IsDefined(typeof(AuthorizeAttribute), true), "both feedback routes require Firebase authentication");
        check(((ObjectResult)controller.Capabilities()).StatusCode == 401, "capabilities rejects unverified identity");
        check(((ObjectResult)await controller.Send(new("feedback", "text"), default)).StatusCode == 401, "send rejects unverified identity");
        controller.HttpContext.User = new(new ClaimsIdentity(new[] { new Claim("user_id", "user"),
            new Claim("firebase", """{"sign_in_provider":"anonymous"}""") }, "test"));
        check(((ObjectResult)controller.Capabilities()).StatusCode == 401, "Firebase anonymous account cannot send owner email");
        controller.HttpContext.User = new(new ClaimsIdentity(new[] { new Claim("user_id", "user") }, "test"));
        var capabilities = JsonSerializer.Serialize(((OkObjectResult)controller.Capabilities()).Value);
        check(capabilities == """{"enabled":true,"ownerName":"Teamify"}""", "public capabilities never contains recipient or configuration");
        settings.Value.Enabled = false;
        var disabled = (ObjectResult)await controller.Send(new("feedback", "hello"), default);
        check(disabled.StatusCode == 503 && !JsonSerializer.Serialize(disabled.Value).Contains("@"), "disabled feature returns only safe structured error");
        settings.Value.Enabled = true;
        var accepting = new MemoryOutbox();
        var acceptedController = new FeedbackController(new(accepting, settings))
        {
            ControllerContext = controller.ControllerContext
        };
        var accepted = (ObjectResult)await acceptedController.Send(new("feedback", "hello"), default);
        check(accepted.StatusCode == 202 && accepting.Items.Count == 1
            && JsonSerializer.Serialize(accepted.Value) == """{"accepted":true,"status":"queued","ownerName":"Teamify"}""",
            "202 means durable queued acceptance, never claims delivery or exposes private recipient");
        accepting.Fail = true;
        check(((ObjectResult)await acceptedController.Send(new("feedback", "another"), default)).StatusCode == 503,
            "failed durable write never returns accepted");
        accepting.Fail = false;
        acceptedController.HttpContext.User = new(new ClaimsIdentity(new[] { new Claim("user_id", "free-user"),
            new Claim("email", "unverified@example.invalid"), new Claim("email_verified", "false"),
            new Claim("firebase", """{"sign_in_provider":"password"}""") }, "test"));
        check(((ObjectResult)await acceptedController.Send(new("question", "Unverified contact"), default)).StatusCode == 202
            && accepting.Items.Values.All(i => !i.Body.Contains("unverified@example.invalid")),
            "signed-in free account may send without verified email, but unverified contact is never forwarded");
        acceptedController.HttpContext.User = new(new ClaimsIdentity(new[] { new Claim("user_id", "verified-user"),
            new Claim("email", "verified@example.invalid"), new Claim("email_verified", "true"),
            new Claim("firebase", """{"sign_in_provider":"google.com"}""") }, "test"));
        check(((ObjectResult)await acceptedController.Send(new("question", "Verified contact"), default)).StatusCode == 202
            && accepting.Items.Values.Any(i => i.Body.Contains("Verified reply email: verified@example.invalid")),
            "only trusted verified Firebase email becomes a server-side reply contact");
        acceptedController.HttpContext.User = new(new ClaimsIdentity(new[] { new Claim("user_id", "anonymous-user"),
            new Claim("email", "anonymous@example.invalid"), new Claim("email_verified", "true"),
            new Claim("firebase", """{"sign_in_provider":"anonymous"}""") }, "test"));
        check(((ObjectResult)await acceptedController.Send(new("question", "Anonymous contact"), default)).StatusCode == 401,
            "anonymous Firebase session cannot submit even when other email claims are present");

        var transport = new Transport { Throttle = true };
        await new OwnerEmailProcessor(Store(), transport).ProcessAsync(await Read(), default);
        check((await Read()).DispatchStartedAt == null && transport.Sends == 1, "definitive provider throttle remains retryable after restart");
        transport.Throttle = false;
        transport.FailAfterSend = true;
        try { await new OwnerEmailProcessor(Store(), transport).ProcessAsync(await Read(), default); check(false, "provider timeout"); }
        catch (OwnerEmailException) { check(true, "ambiguous provider timeout surfaces and leaves durable operation"); }
        var pending = await Read();
        check(pending.DispatchStartedAt != null && !pending.Completed, "provider operation reserved durably before dispatch");
        transport.FailAfterSend = false;
        await new OwnerEmailProcessor(Store(), transport).ProcessAsync(await Read(), default);
        check(transport.Sends == 2 && (await Read()).Completed && (await Read()).Body.Length == 0,
            "retry polls same operation instead of sending duplicate and purges completed message body");
        await new OwnerEmailProcessor(Store(), transport).ProcessAsync(await Read(), default);
        check(transport.Sends == 2, "completed outbox replay never sends another email");
        await new OwnerEmailProcessor(Store(), transport).ProcessAsync(pending, default);
        check(transport.Sends == 2, "stale worker cannot dispatch or overwrite a completed operation");
        var failureTable = new BillingStorageRegression.MemoryTable();
        TableOwnerEmailStore FailureStore() => new(new BillingStorageRegression.MemoryTables(failureTable), settings);
        await FailureStore().AcceptAsync(new OwnerEmailItem { RowKey = "failure", ContentHash = "synthetic", Body = "synthetic body" }, null, default);
        async Task<OwnerEmailItem> ReadFailure() => (await failureTable.GetEntityIfExistsAsync<OwnerEmailItem>("owner-email-v1", "failure")).Value;
        var failingTransport = new Transport { SendStatus = "failed" };
        var originalOperation = (await ReadFailure()).OperationId;
        await new OwnerEmailProcessor(FailureStore(), failingTransport).ProcessAsync(await ReadFailure(), default);
        check(!(await ReadFailure()).Completed && (await ReadFailure()).DispatchStartedAt == null
            && (await ReadFailure()).OperationId != originalOperation, "definitive provider failure retries under a new persisted operation");
        failingTransport.SendStatus = "running";
        await new OwnerEmailProcessor(FailureStore(), failingTransport).ProcessAsync(await ReadFailure(), default);
        failingTransport.PollStatus = "missing";
        var ambiguous = await ReadFailure();
        ambiguous.DispatchStartedAt = DateTimeOffset.UtcNow.AddHours(-25);
        await FailureStore().SaveAsync(ambiguous, default);
        await new OwnerEmailProcessor(FailureStore(), failingTransport).ProcessAsync(await ReadFailure(), default);
        check((await ReadFailure()).Completed && (await ReadFailure()).Status == "needs_review"
            && (await ReadFailure()).Body.Length == 0 && failingTransport.Sends == 2,
            "unresolvable dispatch ambiguity is quarantined, not resent, and sensitive content is removed");

        var outbox = new MemoryOutbox();
        var alerts = new OwnerPaidAlertService(outbox, settings);
        PlayReceipt Paid() => new()
        {
            AccountKey = "account", Order = "GPA.1234-5678-9012-34567", ProviderStartedAt = now,
            VerifiedAt = now, ExpiresAt = now.AddDays(30), NewPurchaseEventAt = now, PaidInitialOrder = true
        };
        await alerts.ObserveAsync(Paid(), default);
        await alerts.ObserveAsync(Paid(), default);
        check(outbox.Items.Count == 1, "Google and RevenueCat paid evidence converges to one durable order alert");
        var excluded = new List<PlayReceipt>();
        var receipt = Paid(); receipt.Sandbox = true; excluded.Add(receipt);
        receipt = Paid(); receipt.PaidInitialOrder = false; excluded.Add(receipt);
        receipt = Paid(); receipt.NewPurchaseEventAt = null; excluded.Add(receipt);
        receipt = Paid(); receipt.ProviderStartedAt = now.AddDays(-2); excluded.Add(receipt);
        receipt = Paid(); receipt.NewPurchaseEventAt = now.AddDays(-2); excluded.Add(receipt);
        receipt = Paid(); receipt.OutOfAppResubscription = true; excluded.Add(receipt);
        receipt = Paid(); receipt.SupersededByToken = "replacement"; excluded.Add(receipt);
        receipt = Paid(); receipt.AccountKey = null; excluded.Add(receipt);
        receipt = Paid(); receipt.ExpiresAt = null; excluded.Add(receipt);
        foreach (var item in excluded) { item.Order += "-excluded"; await alerts.ObserveAsync(item, default); }
        check(outbox.Items.Count == 1, "sandbox, unpaid/trial/renewal, restore, historical, replacement and unknown-owner cases never email");
        outbox.Fail = true;
        receipt = Paid(); receipt.Order += "-next";
        try { await alerts.ObserveAsync(receipt, default); check(false, "alert store outage"); }
        catch (RequestFailedException) { check(true, "alert queue failure propagates to provider retry rather than dropping alert"); }
        outbox.Fail = false;
        await new OwnerPaidAlertService(outbox, settings).ObserveAsync(receipt, default);
        check(outbox.Items.Count == 2, "failed paid alert is durably accepted after restart and recovery");

        var providerOutbox = new MemoryOutbox();
        var providerAlerts = new OwnerPaidAlertService(providerOutbox, settings);
        var playStore = new PlayBillingRegression.Store();
        var accounts = new BillingRegression.MemoryStore();
        var owner = await accounts.BindAsync("provider-owner", default);
        var ownerId = TableBillingStore.Hash(owner.AppUserId);
        playStore.Accounts[ownerId] = owner;
        var google = new PlayBillingRegression.Client
        {
            Purchase = new(ownerId, null, "GPA.provider", "SUBSCRIPTION_STATE_ACTIVE", now.AddDays(30),
                true, false, StartedAt: now, PaidInitialOrder: true)
        };
        var billingOptions = Options.Create(new BillingOptions { Enabled = true, RecoveryEnabled = true });
        PlayPurchaseRecovery Recovery() => new(playStore, google, billingOptions, alerts: providerAlerts);
        await Recovery().ProcessAsync(await Recovery().ReceiveAsync("synthetic-restored-token", default), default);
        check(providerOutbox.Items.Count == 0, "client receipt recovery alone cannot produce a new-purchase email");
        await Recovery().ReceiveNewPurchaseNotificationAsync("synthetic-restored-token", now, default);
        await Recovery().ProcessAsync((await playStore.ReadReceiptAsync("synthetic-restored-token", default))!, default);
        check(providerOutbox.Items.Count == 1, "authenticated new-purchase notification plus Google paid verification produces alert");
        var rc = new BillingInbox
        {
            RowKey = "rc-initial", CustomerIdsJson = JsonSerializer.Serialize(new[] { owner.AppUserId }),
            NewPurchaseOrder = google.Purchase.Order, NewPurchaseAt = now
        };
        await new BillingWorker(accounts, new BillingService(accounts, billingOptions, playStore),
            NullLogger<BillingWorker>.Instance, playStore, alerts: providerAlerts).ProcessEventAsync(rc, default);
        check(rc.Completed && providerOutbox.Items.Count == 1, "RevenueCat INITIAL_PURCHASE retry after Google notification does not double email");
        google.Purchase = google.Purchase with { Order = "GPA.rc-only" };
        await Recovery().ProcessAsync(await Recovery().ReceiveAsync("synthetic-rc-only-token", default), default);
        var rcOnly = new BillingInbox
        {
            RowKey = "rc-only", CustomerIdsJson = rc.CustomerIdsJson,
            NewPurchaseOrder = google.Purchase.Order, NewPurchaseAt = now
        };
        await new BillingWorker(accounts, new BillingService(accounts, billingOptions, playStore),
            NullLogger<BillingWorker>.Instance, playStore, alerts: providerAlerts).ProcessEventAsync(rcOnly, default);
        check(rcOnly.Completed && providerOutbox.Items.Count == 2, "RevenueCat new-purchase signal independently uses Google-verified order evidence");
        google.Purchase = google.Purchase with { Order = "GPA.renewal", PaidInitialOrder = false };
        await Recovery().ReceiveNewPurchaseNotificationAsync("synthetic-renewal-token", now, default);
        await Recovery().ProcessAsync((await playStore.ReadReceiptAsync("synthetic-renewal-token", default))!, default);
        check(providerOutbox.Items.Count == 2, "even a purchase-like notification cannot label verified renewal as a new paid purchase");

        var purchase = new PlayPurchase("account", null, "GPA.order", "SUBSCRIPTION_STATE_ACTIVE",
            now.AddMonths(1), true, false, StartedAt: now);
        string Order(string units = "5", string phase = "BASE", DateTimeOffset? start = null) => JsonSerializer.Serialize(new
        {
            total = new { units, currencyCode = "USD", nanos = 0 }, createTime = now,
            lineItems = new[] { new { subscriptionDetails = new { offerPhase = phase, servicePeriodStartTime = start ?? now } } }
        });
        check(PlayPurchaseClient.IsPaidInitialOrder(Order(), purchase), "Google positive amount, base phase and first funded period prove new paid purchase");
        check(PlayPurchaseClient.IsPaidInitialOrder(Order(), purchase with { State = "SUBSCRIPTION_STATE_CANCELED" }),
            "turning off auto-renew does not erase a genuine already-paid first subscription period");
        check(PlayPurchaseClient.IsPaidInitialOrder(JsonSerializer.Serialize(new {
                total = new { currencyCode = "USD", nanos = 500000000 },
                createTime = now.AddHours(-3),
                lineItems = new[] { new { subscriptionDetails = new { offerPhaseDetails = new { baseDetails = new { } },
                    servicePeriodStartTime = now } } }
            }), purchase), "paid initial evidence does not rely on order creation time or integer-only prices");
        check(!PlayPurchaseClient.IsPaidInitialOrder(Order("0"), purchase)
            && !PlayPurchaseClient.IsPaidInitialOrder(Order(phase: "FREE_TRIAL"), purchase)
            && !PlayPurchaseClient.IsPaidInitialOrder(Order(start: now.AddMonths(1)), purchase)
            && !PlayPurchaseClient.IsPaidInitialOrder(Order(), purchase with { Sandbox = true })
            && !PlayPurchaseClient.IsPaidInitialOrder(Order(), purchase with { LinkedToken = "previous" })
            && !PlayPurchaseClient.IsPaidInitialOrder("{}", purchase),
            "zero payment, trial, renewal, sandbox, replacement and absent payment evidence cannot trigger paid alerts");
        var handler = new CaptureHandler();
        var client = new AcsOwnerEmailTransport(new Factory(handler), new Credential(), settings);
        await client.SendAsync(new OwnerEmailItem { Subject = "Teamify - app question", Body = "hello" }, default);
        check(handler.RequestUri == new Uri(settings.Value.Endpoint + "emails:send?api-version=2023-03-31")
            && handler.Method == HttpMethod.Post,
            "ACS colon send action remains an HTTPS path, not an unsupported emails URI scheme");
        using var captured = JsonDocument.Parse(handler.Body!);
        check(captured.RootElement.GetProperty("recipients").GetProperty("to").GetArrayLength() == 1
            && !captured.RootElement.TryGetProperty("replyTo", out _) && !captured.RootElement.TryGetProperty("headers", out _)
            && captured.RootElement.GetProperty("senderAddress").GetString() == settings.Value.SenderAddress
            && handler.OperationId != null, "transport fixes service sender and private recipient, no user-controlled headers or reply routing");
        var operationId = Guid.NewGuid().ToString();
        await client.PollAsync(operationId, default);
        check(handler.RequestUri == new Uri(settings.Value.Endpoint + "emails/operations/" + operationId + "?api-version=2023-03-31")
            && handler.Method == HttpMethod.Get && handler.Body == null && handler.OperationId == null,
            "ACS polling remains a read-only HTTPS request under the original operation identifier");
        await OptionalObserverIsolation(check, settings, now);
        await OptionalProjectionCompatibility(check, now);
    }

    private static async Task OptionalObserverIsolation(Action<bool, string> check, IOptions<OwnerEmailOptions> settings, DateTimeOffset now)
    {
        foreach (var failureMode in Enumerable.Range(0, 3))
        {
            var accounts = new BillingRegression.MemoryStore();
            var account = await accounts.BindAsync("outage-owner", default);
            var receipts = new PlayBillingRegression.Store();
            var accountId = TableBillingStore.Hash(account.AppUserId);
            receipts.Accounts[accountId] = account;
            var provider = new PlayBillingRegression.Client
            {
                Purchase = new(accountId, null, "GPA.outage", "SUBSCRIPTION_STATE_ACTIVE", now.AddMonths(1),
                    false, false, StartedAt: now, PaidInitialOrder: true)
            };
            var outbox = new MemoryOutbox { Fail = failureMode == 0, FailWithOwnerEmailException = failureMode == 1 };
            if (failureMode == 2)
                receipts.SaveAllowed = r => r.OwnerAlertKey == null || r.OwnerAlertPending;
            var alerts = new OwnerPaidAlertService(outbox, settings);
            var options = Options.Create(new BillingOptions { Enabled = true, RecoveryEnabled = true });
            var logger = new RecoveryLogger();
            PlayPurchaseRecovery Recovery() => new(receipts, provider, options, logger, alerts: alerts);
            await Recovery().ReceiveNewPurchaseNotificationAsync("synthetic-outage-token", now, default);
            var controller = new PlayBillingController(Recovery(), options)
            {
                ControllerContext = new() { HttpContext = new DefaultHttpContext() }
            };
            controller.HttpContext.User = new(new ClaimsIdentity(new[] { new Claim("user_id", "outage-owner") }, "test"));
            check(await controller.Recover(new("synthetic-outage-token"), default) is OkObjectResult,
                "optional outbox outage cannot turn successful purchase recovery into a user-visible failure");
            var saved = (await receipts.ReadReceiptAsync("synthetic-outage-token", default))!;
            check(saved.AccountKey == account.RowKey && saved.ExpiresAt > now && saved.AcknowledgmentState == "acknowledged"
                && provider.Acknowledgements == 1 && saved.NewPurchaseEventAt == now && saved.PaidInitialOrder && !saved.Completed
                && saved.OwnerAlertPending && saved.OwnerAlertKey != null,
                "outbox outage preserves entitlement, acknowledgment and durable retryable new-purchase evidence");
            check(logger.Messages.SequenceEqual(new[] { "Optional owner notification deferred; durable purchase signal retained." }),
                "optional notification failure emits only a bounded sanitized warning");
            outbox.Fail = false;
            outbox.FailWithOwnerEmailException = false;
            receipts.SaveAllowed = null;
            provider.Purchase = provider.Purchase with { Acknowledged = true };
            if (failureMode == 1)
            {
                // A long outage may outlive both the first paid period and billing's terminal transition.
                receipts.Receipts["synthetic-outage-token"].Completed = true;
                receipts.Receipts["synthetic-outage-token"].State = "expired";
                receipts.Receipts["synthetic-outage-token"].ExpiresAt = now.AddDays(-1);
                receipts.Receipts["synthetic-outage-token"].PaidInitialOrder = false;
            }
            async Task Tick()
            {
                receipts.Receipts["synthetic-outage-token"].RetryAt = DateTimeOffset.UtcNow;
                var ticks = 1;
                await new BillingWorker(accounts, new BillingService(accounts, options, receipts),
                    NullLogger<BillingWorker>.Instance, receipts, Recovery(), alerts: alerts)
                    .RunAsync(_ => ValueTask.FromResult(ticks-- > 0), default);
            }
            await Tick();
            await Tick();
            check(outbox.Items.Count == 1 && provider.Acknowledgements == 1,
                "background reconciliation after outbox recovery queues exactly one alert without repeating acknowledgment");
            check(!receipts.Receipts["synthetic-outage-token"].OwnerAlertPending
                && (failureMode != 1 || provider.VerifiedTokens.Count == 1),
                "durable first-payment intent drains even after terminal expiry without requiring another Google verification");
            var transport = new Transport { SendStatus = "succeeded" };
            var item = outbox.Items.Values.Single();
            await new OwnerEmailProcessor(outbox, transport).ProcessAsync(item, default);
            await new OwnerEmailProcessor(outbox, transport).ProcessAsync(item, default);
            check(transport.Sends == 1, "recovered optional notification is dispatched once across duplicate processing");
            receipts.SaveAllowed = _ => false;
            try
            {
                await Recovery().ProcessAsync((await receipts.ReadReceiptAsync("synthetic-outage-token", default))!, default);
                check(false, "real receipt failure must propagate");
            }
            catch (BillingException ex) { check(ex.Code == "billing_sync_conflict", "genuine billing persistence failure is not swallowed by optional observer isolation"); }
        }
    }

    private static async Task OptionalProjectionCompatibility(Action<bool, string> check, DateTimeOffset now)
    {
        var subscription = JsonSerializer.Serialize(new
        {
            subscriptionState = "SUBSCRIPTION_STATE_ACTIVE", acknowledgementState = "ACKNOWLEDGEMENT_STATE_ACKNOWLEDGED",
            startTime = now, externalAccountIdentifiers = new { obfuscatedExternalAccountId = "verified-owner" },
            lineItems = new[] { new { productId = "teamify_premium", offerDetails = new { basePlanId = "monthly" },
                autoRenewingPlan = new { autoRenewEnabled = true }, expiryTime = now.AddMonths(1),
                latestSuccessfulOrderId = "GPA.projection" } }
        });
        var handler = new ProjectionHandler { Subscription = subscription };
        var client = new PlayPurchaseClient(new Factory(handler), Options.Create(new BillingOptions()),
            GoogleCredential.FromAccessToken("synthetic-regression-token"));
        var normal = """{"total":{"currencyCode":"USD","units":"5","nanos":0},"lineItems":[{"subscriptionDetails":{"offerPhase":"BASE","servicePeriodStartTime":"START"}}]}"""
            .Replace("START", now.ToString("O"));
        var valid = System.Text.Json.Nodes.JsonNode.Parse(normal)!;
        var optionalVariants = new List<string> { "{}" };
        foreach (var (path, bad) in new (string, string?)[]
        {
            ("total", null), ("total", "[]"), ("total/currencyCode", "7"),
            ("total/units", "5"), ("total/units", "\"not-money\""),
            ("total/nanos", "\"0\""), ("total/nanos", "1000000000"),
            ("lineItems", "false"), ("lineItems/0", "null"),
            ("lineItems/0/subscriptionDetails", "[]"),
            ("lineItems/0/subscriptionDetails/offerPhase", "true"),
            ("lineItems/0/subscriptionDetails/offerPhaseDetails", "[]"),
            ("lineItems/0/subscriptionDetails/servicePeriodStartTime", "42"),
            ("lineItems/0/subscriptionDetails/servicePeriodStartTime", "\"invalid-date\"")
        })
        {
            var parts = path.Split('/');
            var changed = valid.DeepClone();
            var cursor = changed;
            foreach (var part in parts[..^1]) cursor = part == "0" ? cursor[0]! : cursor[part]!;
            var value = bad == null ? null : System.Text.Json.Nodes.JsonNode.Parse(bad);
            if (parts[^1] == "0") cursor[0] = value; else cursor[parts[^1]] = value;
            optionalVariants.Add(changed.ToJsonString());
        }
        foreach (var optional in optionalVariants)
        {
            var order = System.Text.Json.Nodes.JsonNode.Parse(optional)!;
            order["orderId"] = "GPA.projection"; order["purchaseToken"] = "synthetic-projection-token"; order["state"] = "PROCESSED";
            handler.Order = order.ToJsonString();
            var purchase = await client.GetAsync("synthetic-projection-token", default);
            check(purchase.State == "SUBSCRIPTION_STATE_ACTIVE" && purchase.ExpiresAt > now && purchase.Acknowledged
                && !purchase.PaidInitialOrder && PlayPurchaseClient.OrderPaid(handler.Order, "GPA.projection", "synthetic-projection-token"),
                "missing or malformed optional email projection preserves existing paid-order and entitlement behavior");
        }
        handler.Order = """{"orderId":"GPA.projection","purchaseToken":"synthetic-projection-token","state":"REFUNDED","total":[]}""";
        check((await client.GetAsync("synthetic-projection-token", default)).ExpiresAt == null,
            "optional email compatibility does not weaken authoritative refund handling");
        handler.Order = """{"orderId":"different-order","purchaseToken":"synthetic-projection-token","state":"PROCESSED"}""";
        try { await client.GetAsync("synthetic-projection-token", default); check(false, "order mismatch"); }
        catch (BillingException ex) { check(ex.Code == "billing_provider_response", "authoritative order identity mismatch still rejects purchase"); }
    }

    private sealed class MemoryOutbox : IOwnerEmailStore
    {
        public Dictionary<string, OwnerEmailItem> Items { get; } = new();
        public bool Fail { get; set; }
        public bool FailWithOwnerEmailException { get; set; }
        public Task AcceptAsync(OwnerEmailItem item, string? userKey, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (Fail) throw new RequestFailedException(503, "synthetic storage unavailable");
            if (FailWithOwnerEmailException) throw new OwnerEmailException(503, "feedback_storage_busy");
            Items.TryAdd(item.RowKey, item);
            return Task.CompletedTask;
        }
        public Task<BillingPage<OwnerEmailItem>> DueAsync(string? cursor, CancellationToken ct)
            => Task.FromResult(new BillingPage<OwnerEmailItem>(Items.Values.ToArray(), null));
        public Task<bool> SaveAsync(OwnerEmailItem item, CancellationToken ct) => Task.FromResult(true);
    }
    private sealed class Transport : IOwnerEmailTransport
    {
        public int Sends { get; private set; }
        public bool Throttle { get; set; }
        public bool FailAfterSend { get; set; }
        public string SendStatus { get; set; } = "running";
        public string PollStatus { get; set; } = "succeeded";
        public Task<OwnerEmailTransportResult> PollAsync(string operationId, CancellationToken ct) => Task.FromResult(new OwnerEmailTransportResult(PollStatus));
        public Task<OwnerEmailTransportResult> SendAsync(OwnerEmailItem item, CancellationToken ct)
        {
            Sends++;
            if (FailAfterSend) throw new OwnerEmailException(503, "email_provider_timeout");
            return Task.FromResult(new OwnerEmailTransportResult(Throttle ? "throttled" : SendStatus));
        }
    }
    private sealed class Credential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext context, CancellationToken ct) => new("synthetic", DateTimeOffset.UtcNow.AddHours(1));
        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext context, CancellationToken ct) => ValueTask.FromResult(GetToken(context, ct));
    }
    private sealed class RecoveryLogger : ILogger<PlayPurchaseRecovery>
    {
        public List<string> Messages { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception));
    }
    private sealed class ProjectionHandler : HttpMessageHandler
    {
        public string Subscription { get; set; } = "";
        public string Order { get; set; } = "";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(request.RequestUri!.AbsolutePath.Contains("/orders/") ? Order : Subscription) });
    }
    private sealed class CaptureHandler : HttpMessageHandler
    {
        public string? Body { get; private set; }
        public string? OperationId { get; private set; }
        public Uri? RequestUri { get; private set; }
        public HttpMethod? Method { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            RequestUri = request.RequestUri;
            Method = request.Method;
            Body = request.Content == null ? null : await request.Content.ReadAsStringAsync(ct);
            OperationId = request.Headers.TryGetValues("Operation-Id", out var values) ? values.Single() : null;
            return new(HttpStatusCode.Accepted) { Content = new StringContent("""{"status":"Running"}""") };
        }
    }
    private sealed class Factory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public Factory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler, false) { BaseAddress = new Uri("https://synthetic.example.invalid/") };
    }
}
