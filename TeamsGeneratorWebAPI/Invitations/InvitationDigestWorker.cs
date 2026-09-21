using Azure;

namespace TeamsGeneratorWebAPI.Invitations;

public sealed class InvitationDigestProcessor
{
    private readonly IInvitationDocumentStore _store;
    private readonly IInvitationGroupSource _groups;
    private readonly NotificationAccountService _accounts;
    private readonly IInvitationPushSender _sender;
    private readonly IInvitationClock _clock;
    public InvitationDigestProcessor(IInvitationDocumentStore store, IInvitationGroupSource groups,
        NotificationAccountService accounts, IInvitationPushSender sender, IInvitationClock clock)
        => (_store, _groups, _accounts, _sender, _clock) = (store, groups, accounts, sender, clock);

    public async Task ProcessAsync(string key, CancellationToken ct)
    {
        var stored = await _store.ReadAsync<InvitationState>(key, ct);
        if (stored == null) return;
        var state = stored.Value;
        var now = _clock.UtcNow;
        if (state.Revoked || state.ScheduledAt <= now || state.ExpiresAt <= now
            || state.LeaseUntil > now) return;
        var account = await _accounts.GetAsync(state.OwnerId, ct);
        var group = await _groups.LoadAsync(state.OwnerId, state.GroupId, ct);
        if (!account.Preferences.Enabled)
        {
            Drain(state, group.Players);
            await _store.WriteAsync(key, state, stored.ETag, ct);
            return;
        }
        if (state.RetryAt > now) return;
        if (!_sender.Enabled) return;
        InvitationService.RecomputePending(state, group.Players);
        var changes = InvitationService.NetChanges(state, group.Players);
        // Before any successful delivery, retries must reflect the latest net response, not a stale queued join.
        if (state.Batch is { AnyDelivered: false })
        {
            state.Batch = null;
            if (changes.Count == 0)
            {
                state.RetryAt = null;
                state.LeaseOwner = null;
                state.LeaseUntil = null;
                await _store.WriteAsync(key, state, stored.ETag, ct);
                return;
            }
        }
        if (state.Batch == null)
        {
            if (changes.Count == 0 || state.PendingSince == null
                || (state.LastDeliveredAt.HasValue && state.LastDeliveredAt.Value.AddMinutes(10) > now)
                || (changes.Count < account.Preferences.PlayerThreshold
                    && state.PendingSince.Value.AddMinutes(account.Preferences.IntervalMinutes) > now)) return;
            foreach (var expired in account.Devices.Where(item => item.Value.UpdatedAt.AddDays(90) <= now).ToArray())
                await _accounts.RemoveByKeyAsync(state.OwnerId, expired.Key, expired.Value, ct);
            var targets = account.Devices.Where(item => item.Value.UpdatedAt.AddDays(90) > now)
                .Select(item => item.Key).ToHashSet(StringComparer.Ordinal);
            if (targets.Count == 0) return;
            var view = InvitationService.Project(state, group.Players, now);
            state.Batch = new DigestBatch
            {
                Revision = state.Revision,
                Snapshot = InvitationService.Snapshot(state, group.Players), TargetDevices = targets,
                Summary = $"{changes.Count(item => item.Value)} joined, {changes.Count(item => !item.Value)} withdrew; {view.Counts.In}/{state.Capacity} places filled"
            };
        }
        var lease = Guid.NewGuid().ToString("N");
        state.LeaseOwner = lease;
        state.LeaseUntil = now.AddMinutes(2);
        if (!await _store.WriteAsync(key, state, stored.ETag, ct)) return;

        foreach (var deviceKey in state.Batch.TargetDevices.Except(state.Batch.CompletedDevices).ToArray())
        {
            state = await ChangeLeasedAsync(key, lease, current =>
            {
                current.LeaseUntil = _clock.UtcNow.AddMinutes(2);
            }, ct);
            if (state == null) return;
            if (state.Revoked || state.ScheduledAt <= _clock.UtcNow) return;
            account = await _accounts.GetAsync(state.OwnerId, ct);
            if (!account.Preferences.Enabled)
            {
                var currentGroup = await _groups.LoadAsync(state.OwnerId, state.GroupId, ct);
                await ChangeLeasedAsync(key, lease, current => Drain(current, currentGroup.Players), ct);
                return;
            }
            var result = PushResult.InvalidDevice;
            if (account.Devices.TryGetValue(deviceKey, out var device) && device.UpdatedAt.AddDays(90) > _clock.UtcNow
                && await _accounts.IsOwnedAsync(state.OwnerId, deviceKey, ct))
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(30));
                try { result = await _sender.SendAsync(device.Token, state, state.Batch!, timeout.Token); }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested) { result = PushResult.Retry; }
            }
            if (result is PushResult.Retry or PushResult.Unavailable)
            {
                await ChangeLeasedAsync(key, lease, current =>
                {
                    current.Failures = Math.Min(10, current.Failures + 1);
                    current.RetryAt = _clock.UtcNow.AddSeconds(Math.Min(3600, 30 * Math.Pow(2, current.Failures)));
                    current.LeaseOwner = null;
                    current.LeaseUntil = null;
                }, ct);
                return;
            }
            if (result == PushResult.InvalidDevice && device != null)
                await _accounts.RemoveByKeyAsync(state.OwnerId, deviceKey, device, ct);
            state = await ChangeLeasedAsync(key, lease, current =>
            {
                current.Batch!.CompletedDevices.Add(deviceKey);
                current.Batch.AnyDelivered |= result == PushResult.Delivered;
            }, ct);
            if (state == null) return;
        }

        var latestGroup = await _groups.LoadAsync(state.OwnerId, state.GroupId, ct);
        await ChangeLeasedAsync(key, lease, current =>
        {
            var batch = current.Batch!;
            if (batch.AnyDelivered)
            {
                current.Baseline = batch.Snapshot;
                current.BaselineRevision = batch.Revision;
                current.LastDeliveredAt = _clock.UtcNow;
                current.Failures = 0;
                current.RetryAt = null;
            }
            else
            {
                // No successful delivery means the baseline must not advance.
                current.RetryAt = _clock.UtcNow.AddMinutes(15);
            }
            current.Batch = null;
            current.LeaseOwner = null;
            current.LeaseUntil = null;
            current.PendingSince = null;
            InvitationService.RecomputePending(current, latestGroup.Players);
        }, ct);
    }

    private async Task<InvitationState?> ChangeLeasedAsync(string key, string lease, Action<InvitationState> change,
        CancellationToken ct)
    {
        for (var attempt = 0; attempt < 12; attempt++)
        {
            var stored = await _store.ReadAsync<InvitationState>(key, ct);
            if (stored == null || stored.Value.LeaseOwner != lease || stored.Value.LeaseUntil <= _clock.UtcNow) return null;
            change(stored.Value);
            if (await _store.WriteAsync(key, stored.Value, stored.ETag, ct)) return stored.Value;
        }
        throw new InvitationException(409, "Digest update conflicted with concurrent responses.");
    }

    private static void Drain(InvitationState state, IReadOnlyList<RosterPlayer> roster)
    {
        state.Baseline = InvitationService.Snapshot(state, roster);
        state.BaselineRevision = state.Revision;
        state.Batch = null;
        state.PendingSince = null;
        state.LeaseOwner = null;
        state.LeaseUntil = null;
        state.RetryAt = null;
        state.Failures = 0;
    }
}

public sealed class InvitationDigestWorker : BackgroundService
{
    private readonly IInvitationDocumentStore _store;
    private readonly InvitationDigestProcessor _processor;
    private readonly ILogger<InvitationDigestWorker> _logger;
    public InvitationDigestWorker(IInvitationDocumentStore store, InvitationDigestProcessor processor,
        ILogger<InvitationDigestWorker> logger) => (_store, _processor, _logger) = (store, processor, logger);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string? cursor = null;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        try
        {
            // No startup flush: each occurrence must independently meet threshold/deadline and cooldown.
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    var page = await _store.ListAsync(InvitationKeys.StatePrefix, cursor, 64, stoppingToken);
                    cursor = page.Continuation;
                    foreach (var key in page.Keys)
                    {
                        try { await _processor.ProcessAsync(key, stoppingToken); }
                        catch (InvitationException ex)
                        {
                            _logger.LogWarning("Invitation digest processing deferred (status {Status}).", ex.StatusCode);
                        }
                        catch (RequestFailedException ex)
                        {
                            _logger.LogWarning("Invitation digest storage operation failed (status {Status}).", ex.Status);
                        }
                    }
                }
                catch (RequestFailedException ex)
                {
                    _logger.LogWarning("Invitation digest polling failed (status {Status}).", ex.Status);
                }
                catch (HttpRequestException)
                {
                    _logger.LogWarning("Invitation digest polling transport failed.");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
