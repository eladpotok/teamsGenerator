using Azure;
using Microsoft.Extensions.Options;

namespace TeamsGeneratorWebAPI.OwnerEmail;

public sealed class OwnerEmailProcessor(IOwnerEmailStore store, IOwnerEmailTransport transport)
{
    public async Task ProcessAsync(OwnerEmailItem item, CancellationToken ct)
    {
        if (item.Completed) return;
        ct.ThrowIfCancellationRequested();
        item.Attempts++;
        item.RetryAt = DateTimeOffset.UtcNow.AddMinutes(Math.Min(60, Math.Pow(2, Math.Min(item.Attempts, 6))));
        // A lease longer than HTTP timeout, with optimistic completion, excludes simultaneous workers.
        if (!await store.SaveAsync(item, ct)) return;
        OwnerEmailTransportResult result;
        if (item.DispatchStartedAt.HasValue)
        {
            result = await transport.PollAsync(item.OperationId, ct);
            if (result.Status == "missing")
            {
                // Dispatch may have succeeded before a timeout/crash. Never resend an ambiguous operation.
                if (item.DispatchStartedAt < DateTimeOffset.UtcNow.AddHours(-24))
                {
                    item.Status = "needs_review";
                    item.Completed = true;
                    item.Body = "";
                }
                await SaveRequiredAsync(item, ct);
                return;
            }
        }
        else
        {
            item.DispatchStartedAt = DateTimeOffset.UtcNow;
            item.Status = "dispatching";
            if (!await store.SaveAsync(item, ct)) return;
            try { result = await transport.SendAsync(item, ct); }
            catch (OwnerEmailException ex) when (ex.SafeToRetry)
            {
                item.DispatchStartedAt = null;
                await SaveRequiredAsync(item, ct);
                throw;
            }
            // A definitive provider throttle has not accepted a send; a retry can safely dispatch.
            if (result.Status == "throttled") item.DispatchStartedAt = null;
        }
        if (result.Status == "failed" && item.Attempts < 8)
        {
            // A definitive failed operation did not send. Retry under a fresh durable operation ID.
            item.DispatchStartedAt = null;
            item.OperationId = Guid.NewGuid().ToString();
            item.Status = "queued";
        }
        else if (result.Status is "succeeded" or "failed")
        {
            item.Completed = true;
            item.Status = result.Status == "succeeded" ? "provider_accepted" : "needs_review";
            // Keep the idempotency tombstone, not the user's message or verified reply address.
            item.Body = "";
        }
        await SaveRequiredAsync(item, ct);
    }

    private async Task SaveRequiredAsync(OwnerEmailItem item, CancellationToken ct)
    {
        if (!await store.SaveAsync(item, ct)) throw new OwnerEmailException(503, "email_storage_conflict");
    }
}

public sealed class OwnerEmailWorker(IOwnerEmailStore store, OwnerEmailProcessor processor,
    IOptions<OwnerEmailOptions> options, ILogger<OwnerEmailWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) return;
        string? cursor = null;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    var page = await store.DueAsync(cursor, stoppingToken);
                    cursor = page.Cursor;
                    foreach (var item in page.Items)
                    {
                        try
                        {
                            await processor.ProcessAsync(item, stoppingToken);
                            if (item.Completed)
                                logger.LogInformation("Owner email outbox terminal state: {Status}.", item.Status);
                        }
                        catch (OwnerEmailException) { logger.LogWarning("Owner email delivery deferred; durable operation retained."); }
                        catch (RequestFailedException) { logger.LogWarning("Owner email persistence deferred."); }
                    }
                }
                catch (RequestFailedException) { logger.LogWarning("Owner email outbox storage unavailable."); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
