using System.Diagnostics;
using System.Text.Json;
using TeamsGeneratorWebAPI.Telemetry;

namespace TeamsGeneratorWebAPI.Premium;

// Values describe work observed by this tick, not queue-wide totals or purchase age.
internal sealed class BillingWorkerTelemetry
{
    private readonly long _started = Stopwatch.GetTimestamp();
    private readonly Dictionary<string, double> _measurements = new();
    public bool HasFailures { get; private set; }

    public BillingWorkerTelemetry()
    {
        foreach (var queue in new[] { "receipt", "event", "account" })
            foreach (var field in new[] { "attempted", "returned", "failed", "conflict", "storage_failure",
                "billing_failure", "credential_failure", "provider_failure", "ownership_failure",
                "page_read", "page_has_more", "observed_due_age_seconds_max" })
                _measurements[$"{queue}_{field}"] = 0;
        foreach (var field in new[] { "receipt_pending", "receipt_mirror_retry",
            "receipt_current_verified_observed", "receipt_verification_age_seconds_max",
            "receipt_ack_pending_observed", "receipt_ack_unknown_observed", "receipt_ack_confirmed_observed",
            "receipt_ack_deadline_known_observed", "receipt_ack_deadline_unknown_observed",
            "receipt_ack_deadline_estimate_overdue_observed", "receipt_ack_attempt_reserved",
            "receipt_ack_response_succeeded", "receipt_ack_confirmation_persisted", "receipt_ack_unconfirmed",
            "receipt_ack_provider_read_confirmed", "receipt_retry_cycle_observed",
            "receipt_retry_history_unknown_observed" })
            _measurements[field] = 0;
    }

    public void Count(string name) => _measurements[name] = Math.Min(1_000_000, _measurements[name] + 1);
    public void Page(string queue, bool more)
    {
        Count($"{queue}_page_read");
        _measurements[$"{queue}_page_has_more"] = more ? 1 : 0;
    }
    public void Attempt(string queue, DateTimeOffset due)
    {
        Count($"{queue}_attempted");
        Age($"{queue}_observed_due_age_seconds_max", due);
    }
    public void Receipt(PlayReceipt receipt)
    {
        Attempt("receipt", receipt.RetryAt);
        if (receipt.RecoveryCycleStartedAt is { } cycle)
        {
            Count("receipt_retry_cycle_observed");
            Age("receipt_retry_cycle_age_seconds_max", cycle);
            Maximum("receipt_retry_cycle_attempts_max", receipt.RecoveryCycleAttempts);
        }
        else if (receipt.RecoveryAttempts == 0) Count("receipt_retry_history_unknown_observed");
        if (receipt.AcknowledgmentEligible)
        {
            if (receipt.AcknowledgmentState == "acknowledged") Count("receipt_ack_confirmed_observed");
            else if (receipt.AcknowledgmentState != "pending") Count("receipt_ack_unknown_observed");
            else
            {
                Count("receipt_ack_pending_observed");
                if (receipt.EligiblePurchaseObservedAt is { } observed)
                    Age("receipt_ack_observed_age_lower_bound_seconds_max", observed);
                if (receipt.AcknowledgmentFirstAttemptAt is { } first)
                    Age("receipt_ack_retry_age_seconds_max", first);
                Maximum("receipt_ack_attempts_max", receipt.AcknowledgmentAttempts);
                if (receipt.AcknowledgmentDeadlineEstimateAt is { } deadline && receipt.ProviderStartedAt is { } started)
                {
                    Count("receipt_ack_deadline_known_observed");
                    Age("receipt_ack_provider_start_age_seconds_max", started);
                    if (deadline <= DateTimeOffset.UtcNow) Count("receipt_ack_deadline_estimate_overdue_observed");
                    var remaining = Math.Clamp((deadline - DateTimeOffset.UtcNow).TotalSeconds, -31_536_000, 31_536_000);
                    const string key = "receipt_ack_deadline_estimate_remaining_seconds_min";
                    _measurements[key] = _measurements.TryGetValue(key, out var prior) ? Math.Min(prior, remaining) : remaining;
                }
                else Count("receipt_ack_deadline_unknown_observed");
            }
        }
        if (receipt.VerifiedAt is { } verified && receipt.ExpiresAt > DateTimeOffset.UtcNow
            && receipt.SupersededByToken == null)
        {
            Count("receipt_current_verified_observed");
            Age("receipt_verification_age_seconds_max", verified);
        }
    }
    public void ReceiptReturned(PlayReceipt receipt)
    {
        Count("receipt_returned");
        if (receipt.State == "pending") Count("receipt_pending");
        if (receipt.MirrorError != null)
        {
            Count("receipt_mirror_retry");
            HasFailures = true;
        }
    }
    public void Failure(string queue, bool storage = false, bool conflict = false)
    {
        HasFailures = true;
        Count($"{queue}_failed");
        Count($"{queue}_{(storage ? "storage_failure" : conflict ? "conflict" : "billing_failure")}");
    }
    public void Failure(string queue, BillingException exception)
    {
        Failure(queue, storage: exception.Code is "billing_storage_unavailable" or "billing_storage_response",
            conflict: exception.Code == "billing_sync_conflict");
        var category = exception.Code switch
        {
            "billing_google_credentials" => "credential_failure",
            "billing_provider_unavailable" or "billing_provider_timeout" or "billing_provider_response" => "provider_failure",
            "billing_ownership_conflict" or "billing_ownership_unknown" or "billing_provider_ownership_conflict" => "ownership_failure",
            _ => null
        };
        if (category != null) Count($"{queue}_{category}");
    }
    internal void Maximum(string name, double value) =>
        _measurements[name] = Math.Max(_measurements.GetValueOrDefault(name), value);
    private void Age(string name, DateTimeOffset timestamp) =>
        Maximum(name, Math.Clamp((DateTimeOffset.UtcNow - timestamp).TotalSeconds, 0, 31_536_000));

    internal void Coverage(string queue, DateTimeOffset scanStarted, DateTimeOffset? lastCompleted, bool completed)
    {
        _measurements[$"{queue}_scan_completed"] = completed ? 1 : 0;
        Age($"{queue}_scan_elapsed_seconds", scanStarted);
        if (lastCompleted is { } last) Age($"{queue}_last_scan_completed_age_seconds", last);
    }

    internal static void WriteLog(ILogger<BillingWorker> logger, LogLevel level, EventId eventId,
        string message, params object?[] args)
    {
        try { logger.Log(level, eventId, message, args); }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or AggregateException)
        {
            Trace.TraceError("Billing monitoring log sink failed.");
        }
    }

    public void Emit(string outcome, string environment, ILogger<BillingWorker> logger, IUsageTelemetry? telemetry)
    {
        var billingEnvironment = environment is "SANDBOX" or "PRODUCTION" ? environment : "unknown";
        _measurements["duration_ms"] = Math.Min(86_400_000, Stopwatch.GetElapsedTime(_started).TotalMilliseconds);
        var properties = new Dictionary<string, string?> { ["outcome"] = outcome, ["worker_schema"] = "2",
            ["billing_environment"] = billingEnvironment, ["coverage"] = "observed_pages_not_population" };
        var summary = JsonSerializer.Serialize(new { outcome, worker_schema = "2",
            coverage = "observed_pages_not_population",
            billing_environment = billingEnvironment, measurements = _measurements });
        WriteLog(logger, outcome is "failed" or "degraded" ? LogLevel.Warning : LogLevel.Information,
            new EventId(4200, "BillingWorkerTick"), "BillingWorkerTick {Summary}", summary);
        try { telemetry?.Track("BillingWorkerTick", properties: properties, measurements: _measurements); }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or AggregateException)
        {
            Trace.TraceError("Billing monitoring event sink failed.");
        }
    }
}
