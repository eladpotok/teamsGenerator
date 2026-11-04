using System.Text.Json;
using Azure;
using TeamsGeneratorWebAPI.Telemetry;
using TeamsGeneratorWebAPI.OwnerEmail;

namespace TeamsGeneratorWebAPI.Premium;

public sealed class BillingWorker : BackgroundService
{
    private readonly IBillingStore _store;
    private readonly BillingService _billing;
    private readonly ILogger<BillingWorker> _logger;
    private readonly IPlayPurchaseStore? _play;
    private readonly PlayPurchaseRecovery? _recovery;
    private readonly IUsageTelemetry? _telemetry;
    private readonly OwnerPaidAlertService? _alerts;
    public BillingWorker(IBillingStore store, BillingService billing, ILogger<BillingWorker> logger,
        IPlayPurchaseStore? play = null, PlayPurchaseRecovery? recovery = null, IUsageTelemetry? telemetry = null,
        OwnerPaidAlertService? alerts = null)
        => (_store, _billing, _logger, _play, _recovery, _telemetry, _alerts) = (store, billing, logger, play, recovery, telemetry, alerts);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        await RunAsync(timer.WaitForNextTickAsync, stoppingToken);
    }

    public async Task RunAsync(Func<CancellationToken, ValueTask<bool>> waitForNextTick, CancellationToken stoppingToken)
    {
        if (!_billing.Enabled) return;
        string? eventCursor = null, accountCursor = null, receiptCursor = null;
        var coverage = new Dictionary<string, (DateTimeOffset Started, DateTimeOffset? Completed)>();
        foreach (var queue in new[] { "receipt", "event", "account" }) coverage[queue] = (DateTimeOffset.UtcNow, null);
        try
        {
            while (await waitForNextTick(stoppingToken))
            {
                stoppingToken.ThrowIfCancellationRequested();
                var observation = new BillingWorkerTelemetry();
                var outcome = "failed";
                var scansCompleted = new HashSet<string>();
                void Page(string queue, bool more)
                {
                    observation.Page(queue, more);
                    if (!more)
                    {
                        scansCompleted.Add(queue);
                        coverage[queue] = (coverage[queue].Started, DateTimeOffset.UtcNow);
                    }
                }
                try
                {
                    try
                    {
                        if (_play != null && _recovery?.Enabled == true)
                        {
                            var receipts = await _play.ReceiptsAsync(receiptCursor, stoppingToken);
                            receiptCursor = receipts.Cursor;
                            Page("receipt", receiptCursor != null);
                            foreach (var receipt in receipts.Items)
                            {
                                stoppingToken.ThrowIfCancellationRequested();
                                observation.Receipt(receipt);
                                try
                                {
                                    await _recovery.RetryOwnerAlertAsync(receipt, stoppingToken, observation);
                                    if (!receipt.Completed)
                                        observation.ReceiptReturned(await _recovery.ProcessAsync(receipt, stoppingToken, observation));
                                }
                                catch (BillingException ex) { observation.Failure("receipt", ex); }
                                catch (RequestFailedException) { observation.Failure("receipt", storage: true); }
                            }
                        }
                    }
                    catch (RequestFailedException) { observation.Failure("receipt", storage: true); }
                    catch (BillingException ex) { observation.Failure("receipt", ex); }
                    var queue = "event";
                    try
                    {
                        var events = await _store.EventsAsync(eventCursor, stoppingToken);
                        eventCursor = events.Cursor;
                        Page("event", eventCursor != null);
                        foreach (var item in events.Items)
                        {
                            stoppingToken.ThrowIfCancellationRequested();
                            observation.Attempt("event", item.RetryAt);
                            if (await ProcessEventCoreAsync(item, observation, stoppingToken))
                                observation.Count("event_returned");
                        }
                        queue = "account";
                        var accounts = await _store.AccountsAsync(accountCursor, stoppingToken);
                        accountCursor = accounts.Cursor;
                        Page("account", accountCursor != null);
                        foreach (var account in accounts.Items)
                        {
                            stoppingToken.ThrowIfCancellationRequested();
                            observation.Attempt("account", account.RetryAt);
                            try
                            {
                                var synced = await _billing.SyncAsync(account, stoppingToken);
                                if (synced.Status == "ownership_conflict") observation.Failure("account");
                                else observation.Count("account_returned");
                            }
                            catch (BillingException ex) { observation.Failure("account", ex); }
                        }
                    }
                    catch (RequestFailedException) { observation.Failure(queue, storage: true); }
                    catch (BillingException ex) { observation.Failure(queue, ex); }
                    outcome = observation.HasFailures ? "degraded" : "succeeded";
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    outcome = "cancelled";
                    throw;
                }
                finally
                {
                    foreach (var queue in coverage.Keys.ToArray())
                    {
                        var progress = coverage[queue];
                        observation.Coverage(queue, progress.Started, progress.Completed, scansCompleted.Contains(queue));
                        if (scansCompleted.Contains(queue)) coverage[queue] = (DateTimeOffset.UtcNow, progress.Completed);
                    }
                    observation.Emit(outcome, _billing.Environment, _logger, _telemetry);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    public async Task ProcessEventAsync(BillingInbox item, CancellationToken ct)
        => await ProcessEventCoreAsync(item, null, ct);

    private async Task<bool> ProcessEventCoreAsync(BillingInbox item, BillingWorkerTelemetry? observation, CancellationToken ct)
    {
        item.Attempts++;
        item.RetryAt = DateTimeOffset.UtcNow.AddMinutes(Math.Min(60, Math.Pow(2, Math.Min(item.Attempts, 6))));
        // A conditional retry reservation prevents parallel workers from consuming the same inbox row.
        if (!await _store.SaveInboxAsync(item, ct))
        {
            observation?.Failure("event", conflict: true);
            return false;
        }
        try
        {
            foreach (var id in JsonSerializer.Deserialize<string[]>(item.CustomerIdsJson)!)
            {
                var account = await _store.FindCustomerAsync(id, ct);
                if (account != null)
                {
                    if (_play != null && _recovery?.Enabled == true)
                        foreach (var receipt in (await _play.AccountReceiptsAsync(account.RowKey, ct)).Where(r => !r.Completed))
                        {
                            observation?.Receipt(receipt);
                            var recovered = await _recovery.ProcessAsync(receipt, ct, observation);
                            observation?.ReceiptReturned(recovered);
                        }
                    var synced = await _billing.SyncAsync(account, ct);
                    if (synced.Status == "ownership_conflict") observation?.Failure("event");
                    if (_play != null && _alerts?.AcceptsEvent(item.NewPurchaseAt) == true && item.NewPurchaseOrder != null)
                    {
                        var receipt = await _play.VerifiedOrderAsync(item.NewPurchaseOrder, account.RowKey, ct);
                        if (receipt == null)
                            throw new BillingException(503, "billing_owner_alert_deferred", "Waiting for verified purchase evidence.");
                        if (receipt.Order?.Split(new[] { ".." }, StringSplitOptions.None)[0]
                            == item.NewPurchaseOrder.Split(new[] { ".." }, StringSplitOptions.None)[0])
                        {
                            receipt.NewPurchaseEventAt ??= item.NewPurchaseAt;
                            _alerts.Prepare(receipt);
                            if (receipt.OwnerAlertPending && !await _play.SaveReceiptAsync(receipt, ct))
                                throw new BillingException(409, "billing_sync_conflict", "Purchase notification changed. Retry.");
                            await _alerts.ObserveAsync(receipt, ct);
                        }
                    }
                }
            }
            item.Completed = true;
            if (!await _store.SaveInboxAsync(item, ct))
            {
                observation?.Failure("event", conflict: true);
                return false;
            }
            return true;
        }
        catch (BillingException ex)
        {
            observation?.Failure("event", ex);
            if (observation == null) _logger.LogWarning("Billing webhook processing deferred.");
            return false;
        }
        catch (OwnerEmailException)
        {
            observation?.Failure("event");
            if (observation == null) _logger.LogWarning("Billing owner alert deferred.");
            return false;
        }
    }
}
