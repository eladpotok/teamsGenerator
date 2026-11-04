using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using TeamsGeneratorWebAPI.Telemetry;
using TeamsGeneratorWebAPI.Premium;

internal static class BillingReviewRegression
{
    public static async Task Run(Action<bool, string> check)
    {
        await Worker(check);
        await CanceledReplacement(check);
        await Resubscription(check);
        await AcknowledgmentMonitoring(check);
    }

    private static async Task Worker(Action<bool, string> check)
    {
        var options = Options.Create(new BillingOptions { Enabled = true, RecoveryEnabled = true });
        var billingStore = new BillingRegression.MemoryStore();
        var owner = await billingStore.BindAsync("worker-owner", default);
        var store = new PlayBillingRegression.Store { PageSize = 2 };
        store.Accounts[TableBillingStore.Hash(owner.AppUserId)] = owner;
        var google = new PlayBillingRegression.Client
        {
            Purchase = new(TableBillingStore.Hash(owner.AppUserId), null, "GPA.worker",
                "SUBSCRIPTION_STATE_ACTIVE", DateTimeOffset.UtcNow.AddDays(20), false, false)
        };
        var billing = new BillingService(billingStore, options, store);
        var logger = new WorkerLogger();
        var telemetry = new WorkerEvents();
        var worker = new BillingWorker(billingStore, billing, logger,
            store, new PlayPurchaseRecovery(store, google, options), telemetry);
        var tick = 0;
        using var cancellation = new CancellationTokenSource();
        await worker.RunAsync(async ct =>
        {
            tick++;
            switch (tick)
            {
                case 1:
                    check(store.Receipts.Count == 0, "worker first tick starts with empty RTDN inbox");
                    break;
                case 2:
                    check(store.Cursors.Count == 1, "worker actually polls receipt inbox on its first tick");
                    await store.ReceiveAsync("a-provider-failure", ct);
                    await store.ReceiveAsync("b-paid", ct);
                    await store.ReceiveAsync("c-paid", ct);
                    google.Failure = token => token == "a-provider-failure"
                        ? new BillingException(503, "billing_provider_unavailable", "Injected") : null;
                    break;
                case 3:
                    check(google.Acknowledgements == 1 && store.Receipts["b-paid"].VerifiedAt != null,
                        "RTDN received after startup is verified and acknowledged despite a failed neighbor");
                    check(store.Receipts["b-paid"].RetryAt > DateTimeOffset.UtcNow,
                        "worker schedules future paid receipt verification");
                    store.FailNextPage = true;
                    break;
                case 4:
                    check(store.Cursors.Last() == "b-paid", "worker retains pagination cursor across a failed fetch");
                    break;
                case 5:
                    check(store.Receipts["c-paid"].VerifiedAt != null && store.Cursors.Last() == "b-paid",
                        "cursor progresses fairly to receipts beyond the first page after storage recovery");
                    store.Receipts["b-paid"].RetryAt = DateTimeOffset.UtcNow.AddSeconds(-1);
                    store.Receipts["c-paid"].RetryAt = DateTimeOffset.UtcNow.AddSeconds(-1);
                    google.Purchase = google.Purchase with { Acknowledged = true };
                    google.Failure = token => token == "b-paid" ? new RequestFailedException(503, "Injected") : null;
                    break;
                case 6:
                    check(google.VerifiedTokens.Count(t => t == "c-paid") == 2,
                        "scheduled re-verification survives another receipt's storage failure");
                    store.Receipts["b-paid"].RetryAt = DateTimeOffset.UtcNow.AddSeconds(-1);
                    google.Failure = null;
                    break;
                case 7:
                    check(google.VerifiedTokens.Count(t => t == "b-paid") == 3,
                        "a failed receipt is retried on a later worker tick");
                    cancellation.Cancel();
                    break;
                default:
                    throw new Exception("Worker failed to stop on cancellation.");
            }
            return true;
        }, cancellation.Token);
        check(tick == 7 && store.Cursors.Count == 6, "worker honors cancellation before any more receipt I/O");
        check(telemetry.Events.Count == 6 && telemetry.Events[0].Outcome == "succeeded"
            && telemetry.Events[0].Metrics["receipt_attempted"] == 0,
            "empty receipt tick emits successful liveness with explicit zero work");
        check(telemetry.Events[1].Outcome == "degraded"
            && telemetry.Events[1].Metrics["receipt_failed"] == 1
            && telemetry.Events[1].Metrics["receipt_returned"] == 1,
            "swallowed per-receipt failure cannot report a healthy tick");
        check(telemetry.Events[2].Outcome == "degraded"
            && telemetry.Events[2].Metrics["receipt_page_read"] == 0
            && telemetry.Events[2].Metrics["receipt_storage_failure"] == 1,
            "page-fetch failure is distinguished from an empty page");
        check(telemetry.Events[1].Metrics["receipt_scan_completed"] == 0
            && telemetry.Events[2].Metrics["receipt_scan_completed"] == 0
            && telemetry.Events[3].Metrics["receipt_scan_completed"] == 1,
            "scan progress includes partial and failed pages, without claiming global completeness");
        check(!telemetry.Events[0].Metrics.ContainsKey("receipt_ack_deadline_estimate_remaining_seconds_min")
            && !telemetry.Events[0].Metrics.ContainsKey("receipt_ack_observed_age_lower_bound_seconds_max"),
            "empty ticks omit unknown acknowledgment ages rather than inventing a healthy zero");
        check(telemetry.Events[5].Outcome == "succeeded"
            && telemetry.Events[5].Metrics["receipt_returned"] == 1,
            "later successful retry restores healthy tick outcome");
        check(logger.Messages.Count == 6 && logger.Messages.All(m => !m.Contains("a-provider-failure")
            && !m.Contains("b-paid") && !m.Contains(owner.AppUserId) && !m.Contains("Injected")
            && !m.Contains(owner.RowKey) && !m.Contains("GPA.")),
            "tick log projection excludes receipt identifiers, identities and exception text");
        await MonitoringEdges(check, billingStore, store, google, options, billing, logger, telemetry);
        var disabled = new BillingWorker(billingStore, new BillingService(billingStore, Options.Create(new BillingOptions())),
            NullLogger<BillingWorker>.Instance, store, new PlayPurchaseRecovery(store, google, options));
        await disabled.RunAsync(_ => throw new Exception("Disabled worker ticked"), default);
        check(true, "feature-off worker never polls providers or storage");
    }

    private static async Task MonitoringEdges(Action<bool, string> check, BillingRegression.MemoryStore billingStore,
        PlayBillingRegression.Store store, PlayBillingRegression.Client google, IOptions<BillingOptions> options,
        BillingService billing, WorkerLogger logger, WorkerEvents telemetry)
    {
        foreach (var receipt in store.Receipts.Values) receipt.Completed = true;
        var receiptToRetry = await store.ReceiveAsync("monitor-private-token", default);
        google.Failure = null;
        var acknowledgementsBeforePending = google.Acknowledgements;
        google.Purchase = google.Purchase with { State = "SUBSCRIPTION_STATE_PENDING", ExpiresAt = null, Order = null };
        async Task Tick()
        {
            var remaining = 1;
            await new BillingWorker(billingStore, billing, logger, store,
                new PlayPurchaseRecovery(store, google, options), telemetry)
                .RunAsync(_ => ValueTask.FromResult(remaining-- > 0), default);
        }
        await Tick();
        check(telemetry.Events.Last().Outcome == "succeeded"
            && telemetry.Events.Last().Metrics["receipt_pending"] == 1
            && google.Acknowledgements == acknowledgementsBeforePending,
            "normal pending payment is observed without marking tick failed or acknowledging it");
        await store.ReceiveAsync(receiptToRetry.Token, default);
        google.Purchase = google.Purchase with
        {
            State = "SUBSCRIPTION_STATE_ACTIVE",
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
            Order = "GPA.monitor"
        };
        google.ImportFails = true;
        await Tick();
        check(telemetry.Events.Last().Outcome == "degraded"
            && telemetry.Events.Last().Metrics["receipt_mirror_retry"] == 1
            && telemetry.Events.Last().Metrics["receipt_returned"] == 1,
            "internally handled mirror failure degrades tick despite returned verified receipt");
        google.ImportFails = false;
        store.Receipts[receiptToRetry.Token].RetryAt = DateTimeOffset.UtcNow.AddHours(-2);
        store.Receipts[receiptToRetry.Token].VerifiedAt = DateTimeOffset.UtcNow.AddHours(-25);
        await Tick();
        check(telemetry.Events.Last().Metrics["receipt_verification_age_seconds_max"] >= 25 * 3600
            && telemetry.Events.Last().Metrics["receipt_observed_due_age_seconds_max"] >= 2 * 3600,
            "sampled ages use pre-attempt due time and verified current receipt evidence");
        store.Receipts[receiptToRetry.Token].RetryAt = DateTimeOffset.UtcNow;
        using var cancellation = new CancellationTokenSource();
        google.Failure = _ => { cancellation.Cancel(); return new OperationCanceledException(cancellation.Token); };
        await new BillingWorker(billingStore, billing, logger, store,
            new PlayPurchaseRecovery(store, google, options), telemetry)
            .RunAsync(_ => ValueTask.FromResult(true), cancellation.Token);
        check(telemetry.Events.Last().Outcome == "cancelled", "mid-tick shutdown emits cancelled, not success");
        google.Failure = _ => new InvalidOperationException("private unexpected failure");
        store.Receipts[receiptToRetry.Token].RetryAt = DateTimeOffset.UtcNow;
        try { await Tick(); check(false, "unexpected worker failure propagates"); }
        catch (InvalidOperationException)
        {
            check(telemetry.Events.Last().Outcome == "failed",
            "unexpected worker exception emits failed and propagates instead of becoming a success fallback");
        }
        foreach (var receipt in store.Receipts.Values) receipt.Completed = true;
        google.Failure = null;
        var inbox = new BillingInbox { RowKey = "private-event", CustomerIdsJson = "[]" };
        billingStore.Inbox[inbox.RowKey] = inbox;
        billingStore.InboxWrite = _ => false;
        await Tick();
        check(telemetry.Events.Last().Outcome == "degraded"
            && telemetry.Events.Last().Metrics["event_conflict"] == 1
            && telemetry.Events.Last().Metrics["event_returned"] == 0,
            "failed event reservation cannot be counted as processed");
        billingStore.InboxWrite = item => !item.Completed;
        await Tick();
        check(telemetry.Events.Last().Outcome == "degraded"
            && telemetry.Events.Last().Metrics["event_returned"] == 0,
            "failed event completion persistence cannot be counted as processed");
        billingStore.InboxWrite = null;
        inbox.Completed = false;
        inbox.CustomerIdsJson = JsonSerializer.Serialize(new[] { billingStore.Account!.AppUserId });
        billingStore.Account.RetryAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        billingStore.FailSave = true;
        await Tick();
        check(telemetry.Events.Last().Outcome == "degraded"
            && telemetry.Events.Last().Metrics["event_storage_failure"] == 1
            && telemetry.Events.Last().Metrics["account_storage_failure"] == 1,
            "handled webhook and account billing exceptions both appear as degraded work");
        billingStore.FailSave = false;
    }

    private sealed class WorkerLogger : ILogger<BillingWorker>
    {
        public List<string> Messages { get; } = new();
        public bool Fail { get; set; }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (Fail) throw new InvalidOperationException("Injected log sink outage");
            Messages.Add(formatter(state, exception));
        }
    }

    private sealed class WorkerEvents : IUsageTelemetry
    {
        public List<(string Outcome, Dictionary<string, double> Metrics)> Events { get; } = new();
        public bool Fail { get; set; }
        public void Track(string eventName, string? clientVersion = null, string? userId = null,
            IReadOnlyDictionary<string, string?>? properties = null, IReadOnlyDictionary<string, double>? measurements = null)
        {
            if (Fail) throw new InvalidOperationException("Injected event sink outage");
            if (eventName != "BillingWorkerTick" || userId != null) throw new Exception("Unexpected worker telemetry identity.");
            Events.Add((properties!["outcome"]!, new(measurements!)));
        }
    }

    private static async Task AcknowledgmentMonitoring(Action<bool, string> check)
    {
        var now = DateTimeOffset.UtcNow;
        var options = Options.Create(new BillingOptions { Enabled = true, RecoveryEnabled = true });
        var billingStore = new BillingRegression.MemoryStore();
        var owner = await billingStore.BindAsync("ack-owner", default);
        var store = new PlayBillingRegression.Store();
        var accountId = TableBillingStore.Hash(owner.AppUserId);
        store.Accounts[accountId] = owner;
        var google = new PlayBillingRegression.Client
        {
            Purchase = new(accountId, null, "GPA.ack", "SUBSCRIPTION_STATE_ACTIVE", now.AddDays(20),
                false, false, StartedAt: now.AddHours(-60))
        };
        var logger = new WorkerLogger();
        var telemetry = new WorkerEvents();
        PlayPurchaseRecovery Recovery() => new(store, google, options, monitor: logger);
        async Task<PlayReceipt> Process(string token) =>
            await Recovery().ProcessAsync(await Recovery().ReceiveAsync(token, default), default);
        async Task Tick()
        {
            var remaining = 1;
            await new BillingWorker(billingStore, new BillingService(billingStore, options, store), logger,
                store, Recovery(), telemetry).RunAsync(_ => ValueTask.FromResult(remaining-- > 0), default);
        }
        google.BeforeAcknowledge = token =>
        {
            var durable = store.Receipts[token];
            check(durable.AcknowledgmentState == "pending" && durable.AcknowledgmentAttempts > 0
                && durable.AcknowledgmentFirstAttemptAt != null && durable.AcknowledgmentLastAttemptAt != null
                && durable.AccountKey == owner.RowKey && durable.VerifiedAt != null,
                "ack dispatch follows durable attempt reservation and immutable verified owner");
        };
        google.AcknowledgmentFailure = _ => new BillingException(503, "billing_provider_timeout", "private timeout");
        try { await Process("ack-timeout"); check(false, "ack timeout must propagate"); }
        catch (BillingException ex) { check(ex.Code == "billing_provider_timeout", "ack timeout preserves original failure"); }
        var failed = store.Receipts["ack-timeout"];
        check(failed.AcknowledgmentState == "pending" && failed.AcknowledgmentConfirmedAt == null
            && failed.AcknowledgmentDeadlineEstimateAt == now.AddHours(12) && failed.RecoveryCycleAttempts == 1,
            "timeout has a durable estimated deadline and ambiguous attempt, not false acknowledgment success");
        var observed = failed.EligiblePurchaseObservedAt;
        var retryStarted = failed.RecoveryCycleStartedAt;
        var firstAttempt = failed.AcknowledgmentFirstAttemptAt;
        await Recovery().ReceiveAsync("ack-timeout", default);
        await Tick();
        failed = store.Receipts["ack-timeout"];
        check(failed.EligiblePurchaseObservedAt == observed && failed.RecoveryCycleStartedAt == retryStarted
            && failed.AcknowledgmentFirstAttemptAt == firstAttempt && failed.AcknowledgmentAttempts == 2
            && failed.RecoveryCycleAttempts == 2, "redelivery and restart retain eligible and retry clocks");
        var metrics = telemetry.Events.Last().Metrics;
        check(metrics["receipt_ack_pending_observed"] == 1 && metrics["receipt_ack_deadline_known_observed"] == 1
            && metrics["receipt_ack_provider_start_age_seconds_max"] >= 60 * 3600
            && metrics["receipt_ack_deadline_estimate_remaining_seconds_min"] <= 12 * 3600
            && metrics["receipt_ack_unconfirmed"] == 1 && metrics["receipt_ack_attempt_reserved"] == 1
            && metrics["receipt_ack_response_succeeded"] == 0 && metrics["receipt_retry_cycle_observed"] == 1,
            "worker observes deadline and retry history before failure and distinguishes dispatch from response");
        google.AcknowledgmentFailure = null;
        google.Purchase = google.Purchase with { Acknowledged = true };
        var before = google.Acknowledgements;
        var confirmed = await Process("ack-timeout");
        check(google.Acknowledgements == before && confirmed.AcknowledgmentState == "acknowledged"
            && confirmed.AcknowledgmentConfirmationSource == "provider_read" && confirmed.AcknowledgmentConfirmedAt != null
            && confirmed.RecoveryCycleStartedAt == null && confirmed.RecoveryCycleAttempts == 0,
            "provider read resolves ack response loss without duplicate acknowledge and clears completed retry cycle");
        check(confirmed.EligiblePurchaseObservedAt == observed && confirmed.AcknowledgmentFirstAttemptAt == firstAttempt,
            "confirmation preserves first eligible and attempt timestamps");
        google.Purchase = google.Purchase with { Acknowledged = false, StartedAt = null };
        google.ImportFails = true;
        var mirrored = await Process("mirror-after-ack");
        check(mirrored.AcknowledgmentConfirmationSource == "ack_response"
            && mirrored.AcknowledgmentState == "acknowledged" && mirrored.AcknowledgmentConfirmedAt != null
            && mirrored.MirrorError != null && mirrored.RecoveryCycleStartedAt != null
            && mirrored.AcknowledgmentDeadlineEstimateAt == null,
            "ack success persists before mirror failure; absent provider start never becomes an invented deadline");
        google.ImportFails = false;
        store.SaveAllowed = r => r.Token != "ack-save-failure" || r.AcknowledgmentState != "acknowledged";
        try { await Process("ack-save-failure"); check(false, "ack confirmation save must fail"); }
        catch (BillingException ex) { check(ex.Code == "billing_sync_conflict", "ack persistence conflict propagates"); }
        check(store.Receipts["ack-save-failure"].AcknowledgmentConfirmedAt == null,
            "failed ack confirmation save does not fabricate durable confirmation");
        store.SaveAllowed = null;
        google.Purchase = google.Purchase with { Acknowledged = true };
        before = google.Acknowledgements;
        confirmed = await Process("ack-save-failure");
        check(google.Acknowledgements == before && confirmed.AcknowledgmentConfirmationSource == "provider_read",
            "restart resolves successful ack followed by storage failure from authoritative GET");
        google.Purchase = google.Purchase with { Acknowledged = false, AcknowledgmentKnown = false };
        before = google.Acknowledgements;
        try { await Process("unknown-ack-state"); check(false, "unknown acknowledgment cannot dispatch"); }
        catch (BillingException ex) { check(ex.Code == "billing_provider_response", "unknown ack schema remains retryable"); }
        check(google.Acknowledgements == before && store.Receipts["unknown-ack-state"].AcknowledgmentState == "unknown",
            "missing provider acknowledgment state is not silently coerced to pending");
        google.Purchase = google.Purchase with
        {
            AcknowledgmentKnown = true,
            State = "SUBSCRIPTION_STATE_PENDING",
            StartedAt = now.AddDays(-5)
        };
        var pending = await Process("pending-no-clock");
        check(pending.EligiblePurchaseObservedAt == null && pending.ProviderStartedAt == null
            && pending.AcknowledgmentDeadlineEstimateAt == null && pending.AcknowledgmentAttempts == 0
            && pending.RecoveryCycleStartedAt == null, "pending payment never starts acknowledgment clock, even with stray future expiry or start");
        google.Purchase = google.Purchase with { State = "SUBSCRIPTION_STATE_PENDING_PURCHASE_CANCELED", ExpiresAt = null };
        pending = await Process("pending-no-clock");
        check(pending.Completed && pending.AcknowledgmentConfirmedAt == null, "canceled pending payment is terminal without invented ack");
        google.Purchase = google.Purchase with
        {
            State = "SUBSCRIPTION_STATE_ACTIVE",
            ExpiresAt = now.AddDays(20),
            StartedAt = now.AddMinutes(-1),
            Sandbox = true
        };
        before = google.Acknowledgements;
        try { await Process("wrong-environment-ack"); check(false, "wrong environment"); }
        catch (BillingException ex) { check(ex.Code == "billing_wrong_environment", "wrong environment rejected before ack metadata"); }
        check(store.Receipts["wrong-environment-ack"].EligiblePurchaseObservedAt == null
            && google.Acknowledgements == before, "wrong environment neither starts eligible clock nor acknowledges");
        google.Purchase = google.Purchase with { Sandbox = false, AccountId = "invalid-owner" };
        try { await Process("wrong-owner-ack"); check(false, "wrong owner"); }
        catch (BillingException ex) { check(ex.Code == "billing_ownership_unknown", "unknown owner rejected before ack metadata"); }
        check(store.Receipts["wrong-owner-ack"].EligiblePurchaseObservedAt == null
            && google.Acknowledgements == before, "unknown ownership never produces durable eligible evidence");
        google.Purchase = google.Purchase with { AccountId = accountId, Sandbox = true };
        var sandboxRecovery = new PlayPurchaseRecovery(store, google,
            Options.Create(new BillingOptions { Enabled = true, RecoveryEnabled = true, Environment = "SANDBOX" }));
        var sandbox = await sandboxRecovery.ProcessAsync(await store.ReceiveAsync("sandbox-clock", default), default);
        check(sandbox.AcknowledgmentDeadlineEstimateAt == google.Purchase.StartedAt!.Value.AddMinutes(3),
            "license-test deadline estimate is three minutes, not production three days");
        foreach (var receipt in store.Receipts.Values) receipt.Completed = true;
        var legacy = await store.ReceiveAsync("legacy", default);
        legacy.State = "active";
        legacy.VerifiedAt = now.AddDays(-1);
        legacy.ExpiresAt = now.AddDays(1);
        await store.SaveReceiptAsync(legacy, default);
        google.Purchase = google.Purchase with { Sandbox = false, Acknowledged = true, StartedAt = now.AddDays(-2) };
        await Tick();
        check(telemetry.Events.Last().Metrics["receipt_ack_unknown_observed"] == 1
            && telemetry.Events.Last().Metrics["receipt_retry_history_unknown_observed"] == 1
            && store.Receipts["legacy"].AcknowledgmentState == "acknowledged",
            "legacy rows migrate lazily from current provider evidence, with explicitly unknown history");
        google.Purchase = google.Purchase with { Acknowledged = false, StartedAt = now.AddDays(-4) };
        google.AcknowledgmentFailure = _ => new BillingException(503, "billing_provider_unavailable", "Injected");
        try { await Process("overdue"); check(false, "provider ack failure"); }
        catch (BillingException) { }
        await store.ReceiveAsync("overdue", default);
        await Tick();
        check(telemetry.Events.Last().Metrics["receipt_ack_deadline_estimate_overdue_observed"] == 1
            && telemetry.Events.Last().Metrics["receipt_ack_deadline_estimate_remaining_seconds_min"] < 0,
            "overdue deadline is a real negative remaining age, not an omitted unknown sentinel");
        var originalDeadline = store.Receipts["overdue"].AcknowledgmentDeadlineEstimateAt;
        google.Purchase = google.Purchase with { StartedAt = now };
        try { await Process("overdue"); check(false, "retry still fails"); }
        catch (BillingException) { }
        check(store.Receipts["overdue"].AcknowledgmentDeadlineEstimateAt == originalDeadline,
            "redelivery with later provider start cannot extend the original deadline estimate");
        google.Purchase = google.Purchase with { State = "SUBSCRIPTION_STATE_EXPIRED", ExpiresAt = null };
        await Process("overdue");
        await store.ReceiveAsync("overdue", default);
        await Tick();
        check(telemetry.Events.Last().Metrics["receipt_ack_pending_observed"] == 0,
            "terminal receipts preserve history but do not count as outstanding acknowledgment risk");
        google.Purchase = google.Purchase with
        {
            State = "SUBSCRIPTION_STATE_ACTIVE",
            ExpiresAt = now.AddDays(20),
            StartedAt = null
        };
        try { await Process("unknown-deadline"); check(false, "ack outage without start"); }
        catch (BillingException) { }
        await store.ReceiveAsync("unknown-deadline", default);
        await Tick();
        check(telemetry.Events.Last().Metrics["receipt_ack_deadline_unknown_observed"] == 1
            && telemetry.Events.Last().Metrics.ContainsKey("receipt_ack_observed_age_lower_bound_seconds_max")
            && !telemetry.Events.Last().Metrics.ContainsKey("receipt_ack_deadline_estimate_remaining_seconds_min"),
            "missing start exposes lower-bound observation age and explicit unknown deadline, not zero");
        await store.SupersedeAsync("unknown-deadline", "new-token", owner.RowKey, default);
        await store.ReceiveAsync("unknown-deadline", default);
        await Tick();
        check(telemetry.Events.Last().Metrics["receipt_ack_pending_observed"] == 0,
            "superseded acknowledgment history is excluded from outstanding risk after redelivery");
        google.AcknowledgmentFailure = null;
        logger.Fail = true;
        telemetry.Fail = true;
        google.Purchase = google.Purchase with { Acknowledged = false };
        await store.ReceiveAsync("sink-failure", default);
        await Tick();
        check(store.Receipts["sink-failure"].AcknowledgmentState == "acknowledged",
            "known monitoring sink failures cannot break acknowledgment or worker processing");
        check(logger.Messages.All(m => !m.Contains(accountId) && !m.Contains("GPA.") && !m.Contains("ack-timeout")
            && !m.Contains(owner.RowKey) && !m.Contains("private timeout")), "ack logs and tick aggregates contain no identifiers or exception content");

        var json = JsonNode.Parse("""
                {"subscriptionState":"SUBSCRIPTION_STATE_ACTIVE","acknowledgementState":"ACKNOWLEDGEMENT_STATE_PENDING",
                 "startTime":"2026-09-23T10:00:00Z","lineItems":[{"productId":"teamify_premium","offerDetails":{"basePlanId":"monthly"},
                 "autoRenewingPlan":{},"expiryTime":"2026-10-23T10:00:00Z","latestSuccessfulOrderId":"GPA.schema"}]}
                """)!;
        var parsed = PlayPurchaseClient.Parse(json.ToJsonString());
        check(parsed.AcknowledgmentKnown && !parsed.Acknowledged && parsed.StartedAt == DateTimeOffset.Parse("2026-09-23T10:00:00Z"),
            "parser reads Google's documented acknowledgementState spelling and independent startTime");
        json.AsObject().Remove("acknowledgementState");
        json["acknowledgmentState"] = "ACKNOWLEDGEMENT_STATE_PENDING";
        check(!PlayPurchaseClient.Parse(json.ToJsonString()).AcknowledgmentKnown, "American-spelled non-provider field cannot masquerade as acknowledgment evidence");
        json["startTime"] = "invalid";
        try { PlayPurchaseClient.Parse(json.ToJsonString()); check(false, "malformed start"); }
        catch (JsonException) { check(true, "malformed explicit startTime fails closed"); }
    }

    private static async Task CanceledReplacement(Action<bool, string> check)
    {
        var options = Options.Create(new BillingOptions { Enabled = true, RecoveryEnabled = true });
        var billingStore = new BillingRegression.MemoryStore();
        var owner = await billingStore.BindAsync("replacement-owner", default);
        var accountId = TableBillingStore.Hash(owner.AppUserId);
        var store = new PlayBillingRegression.Store();
        store.Accounts[accountId] = owner;
        var expiry = DateTimeOffset.UtcNow.AddDays(20);
        var google = new PlayBillingRegression.Client();
        google.Purchases["original"] = new(accountId, null, "GPA.original", "SUBSCRIPTION_STATE_ACTIVE", expiry, false, false);
        google.Purchases["replacement"] = new(accountId, "original", null,
            "SUBSCRIPTION_STATE_PENDING_PURCHASE_CANCELED", null, false, false);
        var recovery = new PlayPurchaseRecovery(store, google, options);
        await recovery.ProcessAsync(await recovery.ReceiveAsync("original", default), default);
        var replacement = await recovery.ProcessAsync(await recovery.ReceiveAsync("replacement", default), default);
        var original = (await store.ReadReceiptAsync("original", default))!;
        check(replacement.Completed && replacement.ExpiresAt == null && original.ExpiresAt == expiry
            && original.SupersededByToken == null && !original.Completed && original.RetryAt <= DateTimeOffset.UtcNow,
            "canceled pending replacement retains all 20 paid days and requeues original without superseding it");
        google.Failure = token => token == "original" ? new BillingException(503, "billing_provider_unavailable", "Injected") : null;
        try { await recovery.ProcessAsync(original, default); check(false, "original outage"); }
        catch (BillingException ex) { check(ex.Code == "billing_provider_unavailable", "original provider failure stays retryable"); }
        var billing = new BillingService(billingStore, options, store);
        check(billing.IsActive(await billing.SyncAsync(owner, default), DateTimeOffset.UtcNow),
            "original remains Premium during canceled-replacement retry outage");
        google.Failure = null;
        original = await recovery.ProcessAsync((await store.ReadReceiptAsync("original", default))!, default);
        check(original.ExpiresAt == expiry && original.State == "active" && original.SupersededByToken == null,
            "retrying original after canceled replacement restores normal verification without losing expiry");
        google.Purchases["replacement"] = google.Purchases["replacement"] with { State = "SUBSCRIPTION_STATE_PENDING" };
        await recovery.ProcessAsync(await recovery.ReceiveAsync("replacement", default), default);
        check(store.Receipts["original"].SupersededByToken == null, "still-pending replacement also cannot supersede original");
    }

    private static async Task Resubscription(Action<bool, string> check)
    {
        var options = Options.Create(new BillingOptions { Enabled = true, RecoveryEnabled = true });
        var billingStore = new BillingRegression.MemoryStore();
        var owner = await billingStore.BindAsync("resubscription-owner", default);
        var accountId = TableBillingStore.Hash(owner.AppUserId);
        var store = new PlayBillingRegression.Store();
        store.Accounts[accountId] = owner;
        var subscription = JsonNode.Parse(JsonSerializer.Serialize(new
        {
            subscriptionState = "SUBSCRIPTION_STATE_ACTIVE",
            acknowledgementState = "ACKNOWLEDGEMENT_STATE_PENDING",
            outOfAppPurchaseContext = new
            {
                expiredExternalAccountIdentifiers = new
                { obfuscatedExternalAccountId = accountId, obfuscatedExternalProfileId = "verified-profile" }
            },
            lineItems = new[] { new { productId = "teamify_premium", autoRenewingPlan = new { autoRenewEnabled = true },
                offerDetails = new { basePlanId = "monthly" }, expiryTime = DateTimeOffset.UtcNow.AddDays(20).ToString("O"),
                latestSuccessfulOrderId = "GPA.resubscription" } }
        }))!;
        string? currentToken = null, acknowledgeBody = null;
        var acknowledges = 0;
        using var handler = new Handler(async request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/subscriptionsv2/tokens/"))
            {
                currentToken = Uri.UnescapeDataString(path.Split('/').Last());
                return Json(subscription.ToJsonString());
            }
            if (path.Contains("/orders/"))
                return Json(JsonSerializer.Serialize(new { orderId = "GPA.resubscription", purchaseToken = currentToken, state = "PROCESSED" }));
            if (path.EndsWith(":acknowledge"))
            {
                check(request.Method == HttpMethod.Post && path.Contains("/subscriptions/teamify_premium/tokens/"),
                    "real Google client uses documented subscription acknowledgment endpoint");
                acknowledgeBody = await request.Content!.ReadAsStringAsync();
                acknowledges++;
                subscription["outOfAppPurchaseContext"] = null;
                subscription["acknowledgementState"] = "ACKNOWLEDGEMENT_STATE_ACKNOWLEDGED";
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            if (path.EndsWith("/receipts"))
            {
                using var import = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                check(import.RootElement.GetProperty("app_user_id").GetString() == owner.AppUserId
                    && import.RootElement.GetProperty("product_id").GetString() == "teamify_premium",
                    "real RevenueCat /receipts mirror retains verified owner and teamify_premium");
                return Json(JsonSerializer.Serialize(new
                {
                    request_date_ms = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    subscriber = new { original_app_user_id = owner.AppUserId, entitlements = new { }, subscriptions = new { } }
                }));
            }
            throw new Exception("Unexpected provider request.");
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://provider.invalid/") };
        var client = new PlayPurchaseClient(new Factory(http), options, GoogleCredential.FromAccessToken("regression-only"));
        var recovery = new PlayPurchaseRecovery(store, client, options);
        var receipt = await recovery.ProcessAsync(await recovery.ReceiveAsync("out-of-app", default), default);
        using (var body = JsonDocument.Parse(acknowledgeBody!))
        {
            var ids = body.RootElement.GetProperty("externalAccountIds");
            check(ids.GetProperty("obfuscatedAccountId").GetString() == accountId
                && ids.GetProperty("obfuscatedProfileId").GetString() == "verified-profile"
                && !ids.TryGetProperty("obfuscatedExternalAccountId", out _),
                "actual acknowledgment body persists original verified account/profile using Google's exact write-field names");
        }
        check(receipt.VerifiedAccountId == accountId && receipt.VerifiedProfileId == "verified-profile",
            "resubscription identifiers are durable before Google discards context");
        store.Receipts[receipt.Token].VerifiedAt = DateTimeOffset.UtcNow.AddHours(-25);
        receipt = await new PlayPurchaseRecovery(store, client, options).ProcessAsync(
            (await store.ReadReceiptAsync(receipt.Token, default))!, default);
        var billing = new BillingService(billingStore, options, store);
        check(acknowledges == 1 && receipt.VerifiedAt > DateTimeOffset.UtcNow.AddMinutes(-1)
            && billing.IsActive(await billing.SyncAsync(owner, default), DateTimeOffset.UtcNow),
            "real client post-ack verification with no account metadata refreshes stale evidence and preserves paid Premium");
        subscription["externalAccountIdentifiers"] = JsonNode.Parse(JsonSerializer.Serialize(new
        { obfuscatedExternalAccountId = new string('B', 64) }));
        try { await recovery.ProcessAsync(receipt, default); check(false, "changed owner"); }
        catch (BillingException ex) { check(ex.Code == "billing_ownership_conflict", "explicit post-ack owner mismatch cannot use durable fallback"); }
        subscription["externalAccountIdentifiers"] = JsonNode.Parse(JsonSerializer.Serialize(new
        { obfuscatedExternalAccountId = accountId, obfuscatedExternalProfileId = "different-profile" }));
        try { await recovery.ProcessAsync((await store.ReadReceiptAsync(receipt.Token, default))!, default); check(false, "changed profile"); }
        catch (BillingException ex) { check(ex.Code == "billing_ownership_conflict", "explicit post-ack profile mismatch cannot use durable fallback"); }
        subscription["externalAccountIdentifiers"] = null;
        foreach (var token in new[] { "never-verified", "candidate-only" })
        {
            var unknown = await recovery.ReceiveAsync(token, default);
            if (token == "candidate-only")
            {
                unknown.AccountKey = owner.RowKey;
                unknown.VerifiedAccountId = accountId;
                await store.SaveReceiptAsync(unknown, default);
            }
            try { await recovery.ProcessAsync(unknown, default); check(false, "unknown owner"); }
            catch (BillingException ex)
            {
                check(ex.Code == "billing_ownership_unknown",
                "missing metadata cannot assign unknown or unverified candidate receipt: " + token);
            }
        }
        check(acknowledges == 1, "ownership rejection never acknowledges additional receipts");
        await client.AcknowledgeAsync("ordinary-in-app", null, null, default);
        check(acknowledgeBody == "{}", "ordinary in-app acknowledgment omits resubscription-only externalAccountIds");
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class Handler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _send;
        public Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) => _send = send;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => _send(request);
    }

    private sealed class Factory : IHttpClientFactory
    {
        private readonly HttpClient _client;
        public Factory(HttpClient client) => _client = client;
        public HttpClient CreateClient(string name) => _client;
    }
}
