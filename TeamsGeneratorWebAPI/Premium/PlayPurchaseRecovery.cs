using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using TeamsGeneratorWebAPI.OwnerEmail;

namespace TeamsGeneratorWebAPI.Premium;

public sealed class PlayPurchaseRecovery
{
    private readonly IPlayPurchaseStore _store;
    private readonly IPlayPurchaseClient _google;
    private readonly BillingOptions _options;
    private readonly ILogger<PlayPurchaseRecovery> _logger;
    private readonly ILogger<BillingWorker> _monitor;
    private readonly OwnerPaidAlertService? _alerts;
    public PlayPurchaseRecovery(IPlayPurchaseStore store, IPlayPurchaseClient google, IOptions<BillingOptions> options,
        ILogger<PlayPurchaseRecovery>? logger = null, ILogger<BillingWorker>? monitor = null, OwnerPaidAlertService? alerts = null)
        => (_store, _google, _options, _logger, _monitor, _alerts) = (store, google, options.Value,
            logger ?? NullLogger<PlayPurchaseRecovery>.Instance, monitor ?? NullLogger<BillingWorker>.Instance, alerts);

    public bool Enabled => _options.Enabled && _options.RecoveryEnabled;
    public void RequireEnabled()
    {
        if (!Enabled)
            throw new BillingException(503, "billing_disabled", "Google Play purchase recovery is not configured.");
    }

    public Task<PlayReceipt> ReceiveAsync(string token, CancellationToken ct)
    {
        RequireEnabled();
        if (string.IsNullOrWhiteSpace(token) || token.Length > 4096 || token.Any(char.IsControl))
            throw new BillingException(400, "billing_invalid_receipt", "Invalid purchase token.");
        return _store.ReceiveAsync(token, ct);
    }

    public async Task ReceiveNewPurchaseNotificationAsync(string token, DateTimeOffset eventAt, CancellationToken ct)
    {
        var receipt = await ReceiveAsync(token, ct);
        if (_alerts?.AcceptsEvent(eventAt) != true) return;
        for (var attempt = 0; attempt < 6; attempt++)
        {
            receipt.NewPurchaseEventAt ??= eventAt;
            if (await _store.SaveReceiptAsync(receipt, ct)) return;
            receipt = await _store.ReadReceiptAsync(token, ct)
                ?? throw new BillingException(503, "billing_storage_unavailable", "Purchase notification storage is unavailable.");
        }
        throw new BillingException(409, "billing_sync_conflict", "Purchase notification changed. Retry.");
    }

    public async Task<bool> IsResolvedAsync(string uid, string receiptKey, CancellationToken ct)
    {
        RequireEnabled();
        if (receiptKey == null || !Regex.IsMatch(receiptKey, "^[A-F0-9]{64}$"))
            throw new BillingException(400, "billing_invalid_receipt", "Invalid recovery reference.");
        var receipt = await _store.ReadReceiptKeyAsync(receiptKey, ct);
        return receipt?.AccountKey == TableBillingStore.AccountKey(uid) && receipt.VerifiedAt != null
            && receipt.ExpiresAt == null && receipt.Completed && receipt.State is "expired" or "replaced";
    }

    public Task<PlayReceipt> ProcessAsync(PlayReceipt receipt, CancellationToken ct)
        => ProcessAsync(receipt, ct, null);

    internal async Task<PlayReceipt> ProcessAsync(PlayReceipt receipt, CancellationToken ct, BillingWorkerTelemetry? observation)
    {
        RequireEnabled();
        // Reserve before network I/O. A crash/outage leaves the token durable and due for retry.
        receipt.RecoveryCycleStartedAt ??= DateTimeOffset.UtcNow;
        receipt.RecoveryAttempts = (int)Math.Min(int.MaxValue, (long)receipt.RecoveryAttempts + 1);
        receipt.RecoveryCycleAttempts = (int)Math.Min(int.MaxValue, (long)receipt.RecoveryCycleAttempts + 1);
        receipt.RetryAt = DateTimeOffset.UtcNow.AddMinutes(1);
        if (!await _store.SaveReceiptAsync(receipt, ct))
            throw new BillingException(409, "billing_sync_conflict", "Purchase recovery is already in progress. Refresh shortly.");
        if (receipt.SupersededByToken != null)
        {
            receipt.ExpiresAt = null;
            receipt.Completed = true;
            receipt.State = "replaced";
            receipt.RecoveryCycleStartedAt = null;
            receipt.RecoveryCycleAttempts = 0;
            if (!await _store.SaveReceiptAsync(receipt, ct))
                throw new BillingException(409, "billing_sync_conflict", "Purchase recovery changed. Retry.");
            return receipt;
        }
        var purchase = await _google.GetAsync(receipt.Token, ct);
        if (purchase.Sandbox != _options.Sandbox)
        {
            receipt.Completed = true;
            receipt.ExpiresAt = null;
            receipt.State = "wrong_environment";
            if (!await _store.SaveReceiptAsync(receipt, ct))
                throw new BillingException(409, "billing_sync_conflict", "Purchase recovery changed. Retry.");
            throw new BillingException(409, "billing_wrong_environment", "This purchase is for a different environment.");
        }
        // Only this token's previously verified evidence can replace metadata Google removes after acknowledgment.
        var established = receipt.VerifiedAt != null && receipt.AccountKey != null;
        if (established && ((purchase.AccountId != null && receipt.VerifiedAccountId != null
                && purchase.AccountId != receipt.VerifiedAccountId)
            || (purchase.ProfileId != null && receipt.VerifiedProfileId != null
                && purchase.ProfileId != receipt.VerifiedProfileId)))
            throw new BillingException(409, "billing_ownership_conflict", "Purchase ownership cannot change.");
        var accountId = purchase.AccountId ?? (established ? receipt.VerifiedAccountId : null);
        var owner = accountId != null && Regex.IsMatch(accountId, "^[A-F0-9]{64}$")
            ? await _store.FindPlayAccountAsync(accountId, ct) : null;
        if (owner == null)
            throw new BillingException(409, "billing_ownership_unknown", "The purchase's original Teamify account cannot be verified.");
        if (receipt.AccountKey != null && receipt.AccountKey != owner.RowKey)
            throw new BillingException(409, "billing_ownership_conflict", "Purchase ownership cannot change.");
        if (purchase.LinkedToken != null)
        {
            var predecessor = await _store.ReadReceiptAsync(purchase.LinkedToken, ct);
            if (predecessor?.AccountKey != null && predecessor.AccountKey != owner.RowKey)
                throw new BillingException(409, "billing_ownership_conflict", "Subscription chain owner differs.");
            if (predecessor?.AccountKey == null || predecessor.VerifiedAt == null)
            {
                var previous = await _google.GetAsync(purchase.LinkedToken, ct);
                if (previous.AccountId != accountId || previous.Sandbox != purchase.Sandbox)
                    throw new BillingException(409, "billing_ownership_conflict", "Subscription chain owner differs.");
            }
            if (purchase.State is "SUBSCRIPTION_STATE_PENDING" or "SUBSCRIPTION_STATE_PENDING_PURCHASE_CANCELED")
            {
                // A canceled pending replacement never took effect. Requeue, but do not erase the paid original.
                await _store.ReceiveAsync(purchase.LinkedToken, ct);
            }
            else
                await _store.SupersedeAsync(purchase.LinkedToken, receipt.Token, owner.RowKey, ct);
        }
        receipt.AccountKey = owner.RowKey;
        receipt.VerifiedAccountId = accountId;
        receipt.VerifiedProfileId = purchase.ProfileId ?? (established ? receipt.VerifiedProfileId : null);
        receipt.OutOfAppResubscription |= purchase.OutOfAppResubscription;
        receipt.Sandbox = purchase.Sandbox;
        receipt.PaidInitialOrder = purchase.PaidInitialOrder;
        receipt.Order = purchase.Order;
        receipt.ExpiresAt = purchase.ExpiresAt;
        receipt.VerifiedAt = DateTimeOffset.UtcNow;
        receipt.State = purchase.State switch
        {
            "SUBSCRIPTION_STATE_ACTIVE" => "active",
            "SUBSCRIPTION_STATE_CANCELED" => "cancelled",
            "SUBSCRIPTION_STATE_IN_GRACE_PERIOD" => "grace_period",
            "SUBSCRIPTION_STATE_PENDING" => "pending",
            "REFUNDED" => "refunded",
            _ => "expired"
        };
        var eligible = purchase.State is "SUBSCRIPTION_STATE_ACTIVE" or "SUBSCRIPTION_STATE_IN_GRACE_PERIOD"
            or "SUBSCRIPTION_STATE_CANCELED";
        if (eligible)
        {
            receipt.EligiblePurchaseObservedAt ??= receipt.VerifiedAt;
            // Google's grant time is not a receipt arrival or a renewal expiry. Never move the clock forward.
            if (purchase.StartedAt is { } started && started <= receipt.VerifiedAt
                && (receipt.ProviderStartedAt == null || started < receipt.ProviderStartedAt))
            {
                receipt.ProviderStartedAt = started;
                receipt.AcknowledgmentDeadlineEstimateAt = started.Add(_options.Sandbox
                    ? TimeSpan.FromMinutes(3) : TimeSpan.FromDays(3));
            }
        }
        if (purchase.AcknowledgmentKnown)
        {
            receipt.AcknowledgmentState = purchase.Acknowledged ? "acknowledged" : "pending";
            if (purchase.Acknowledged && receipt.AcknowledgmentConfirmedAt == null)
            {
                receipt.AcknowledgmentConfirmedAt = DateTimeOffset.UtcNow;
                receipt.AcknowledgmentConfirmationSource = "provider_read";
            }
        }
        else receipt.AcknowledgmentState = "unknown";
        if (receipt.Order != null) await _store.ClaimAsync(receipt, ct);
        if (!await _store.SaveReceiptAsync(receipt, ct))
            throw new BillingException(409, "billing_sync_conflict", "Purchase recovery changed. Retry.");
        if (purchase.Acknowledged)
            AckPhase("provider_read_confirmed", observation);
        // Entitlement evidence is durable before acknowledgment. RevenueCat attribution cannot strand its rightful owner.
        if (eligible && receipt.ExpiresAt > DateTimeOffset.UtcNow && !purchase.Acknowledged)
        {
            if (!purchase.AcknowledgmentKnown)
                throw new BillingException(502, "billing_provider_response", "Missing Google acknowledgment evidence.");
            receipt.AcknowledgmentFirstAttemptAt ??= DateTimeOffset.UtcNow;
            receipt.AcknowledgmentLastAttemptAt = DateTimeOffset.UtcNow;
            receipt.AcknowledgmentAttempts = (int)Math.Min(int.MaxValue, (long)receipt.AcknowledgmentAttempts + 1);
            if (!await _store.SaveReceiptAsync(receipt, ct))
                throw new BillingException(409, "billing_sync_conflict", "Purchase recovery changed. Retry.");
            AckPhase("attempt_reserved", observation);
            var confirmed = false;
            try
            {
                await _google.AcknowledgeAsync(receipt.Token,
                    receipt.OutOfAppResubscription ? receipt.VerifiedAccountId : null,
                    receipt.OutOfAppResubscription ? receipt.VerifiedProfileId : null, ct);
                AckPhase("response_succeeded", observation);
                receipt.AcknowledgmentState = "acknowledged";
                receipt.AcknowledgmentConfirmedAt ??= DateTimeOffset.UtcNow;
                receipt.AcknowledgmentConfirmationSource ??= "ack_response";
                if (!await _store.SaveReceiptAsync(receipt, ct))
                    throw new BillingException(409, "billing_sync_conflict", "Purchase recovery changed. Retry.");
                confirmed = true;
                AckPhase("confirmation_persisted", observation);
            }
            finally
            {
                // Timeout/crash after dispatch is ambiguous; a later authoritative GET resolves it.
                if (!confirmed) AckPhase("unconfirmed", observation);
            }
        }
        receipt.MirrorError = null;
        if (receipt.Order != null)
        {
            try { await _google.ImportAsync(receipt.Token, owner.AppUserId, ct); }
            catch (BillingException ex)
            {
                receipt.MirrorError = ex.Code;
                _logger.LogWarning("Verified Google purchase mirror requires retry ({Code}).", ex.Code);
            }
        }
        receipt.RetryAt = DateTimeOffset.UtcNow.AddMinutes(receipt.State == "pending" || receipt.MirrorError != null
            ? 1 : _options.ReconcileMinutes);
        receipt.Completed = receipt.MirrorError == null
            && (purchase.State is "SUBSCRIPTION_STATE_EXPIRED" or "SUBSCRIPTION_STATE_PENDING_PURCHASE_CANCELED");
        if (receipt.MirrorError == null)
        {
            receipt.RecoveryCycleStartedAt = null;
            receipt.RecoveryCycleAttempts = 0;
        }
        _alerts?.Prepare(receipt);
        if (!await _store.SaveReceiptAsync(receipt, ct))
            throw new BillingException(409, "billing_sync_conflict", "Purchase recovery changed. Retry.");
        await RetryOwnerAlertAsync(receipt, ct, observation);
        return receipt;
    }

    internal async Task RetryOwnerAlertAsync(PlayReceipt receipt, CancellationToken ct, BillingWorkerTelemetry? observation)
    {
        if (receipt.OwnerAlertPending && _alerts?.AcceptsEvent(receipt.NewPurchaseEventAt) == true)
        {
            try
            {
                await _alerts.ObserveAsync(receipt, ct);
                receipt.OwnerAlertPending = false;
                // This write acknowledges only the optional observer; billing evidence was already committed.
                if (!await _store.SaveReceiptAsync(receipt, ct))
                    throw new OwnerEmailException(503, "email_storage_conflict");
            }
            catch (Exception ex) when (ex is OwnerEmailException or Azure.RequestFailedException or BillingException)
            {
                receipt.OwnerAlertPending = true;
                _logger.LogWarning("Optional owner notification deferred; durable purchase signal retained.");
                observation?.Failure("receipt");
            }
        }
    }

    private void AckPhase(string phase, BillingWorkerTelemetry? observation)
    {
        observation?.Count("receipt_ack_" + phase);
        BillingWorkerTelemetry.WriteLog(_monitor, phase == "unconfirmed" ? LogLevel.Warning : LogLevel.Information,
            new EventId(4201, "BillingAcknowledgment"), "BillingAcknowledgment {Phase} {BillingEnvironment}",
            phase, _options.Environment is "SANDBOX" or "PRODUCTION" ? _options.Environment : "unknown");
    }
}
