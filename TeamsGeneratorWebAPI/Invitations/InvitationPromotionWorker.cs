using Azure;
using TeamsGeneratorWebAPI.PlayerProfiles;

namespace TeamsGeneratorWebAPI.Invitations;

public sealed class InvitationPromotionProcessor(IInvitationDocumentStore store, IInvitationGroupSource groups,
    NotificationAccountService accounts, IInvitationPromotionPushSender sender, IInvitationClock clock)
{
    public async Task ProcessAsync(string key, CancellationToken ct)
    {
        if (!sender.Enabled) return;
        var stored = await store.ReadAsync<InvitationState>(key, ct);
        if (stored == null) return;
        foreach (var id in stored.Value.Promotions.Select(item => item.Id).Take(64).ToArray())
            await ProcessNotificationAsync(key, id, ct);
    }

    private async Task<bool> EligibleAsync(InvitationState state, PromotionNotification promotion, CancellationToken ct)
    {
        if (state.Revoked || state.ScheduledAt <= clock.UtcNow || state.ExpiresAt <= clock.UtcNow
            || state.Responses.GetValueOrDefault(promotion.PlayerKey) is not { Status: "in" } response
            || response.Order != promotion.ResponseOrder) return false;
        InvitationGroup group;
        try { group = await groups.LoadAsync(state.OwnerId, state.GroupId, ct); }
        catch (InvitationException ex) when (ex.StatusCode == 404) { return false; }
        return group.OwnerId == state.OwnerId && group.GroupId == state.GroupId && group.InvitationsEnabled
            && InvitationService.Project(state, group.Players, clock.UtcNow).Players.Any(player =>
                player.Key == promotion.PlayerKey && player.Status == "in" && !player.WaitingListPosition.HasValue)
            && await new PlayerMembershipService(store).LinkedProfileAsync(group, promotion.PlayerKey, ct) == promotion.ProfileId;
    }

    private async Task ProcessNotificationAsync(string key, string id, CancellationToken ct)
    {
        var stored = await store.ReadAsync<InvitationState>(key, ct);
        var state = stored?.Value;
        var promotion = state?.Promotions.SingleOrDefault(item => item.Id == id);
        if (state == null || promotion == null || promotion.LeaseUntil > clock.UtcNow || promotion.RetryAt > clock.UtcNow)
            return;
        if (!await EligibleAsync(state, promotion, ct))
        {
            state.Promotions.Remove(promotion);
            await store.WriteAsync(key, state, stored!.ETag, ct);
            return;
        }
        var userId = await accounts.PlayerRecipientAsync(promotion.ProfileId, ct);
        if (userId == null) return;
        var account = await accounts.GetAsync(userId, ct);
        promotion.TargetDevices ??= account.Devices.Where(item => item.Value.UpdatedAt.AddDays(90) > clock.UtcNow)
            .Select(item => item.Key).ToHashSet(StringComparer.Ordinal);
        if (promotion.TargetDevices.Count == 0) return;
        var lease = Guid.NewGuid().ToString("N");
        promotion.LeaseOwner = lease;
        promotion.LeaseUntil = clock.UtcNow.AddMinutes(2);
        if (!await store.WriteAsync(key, state, stored!.ETag, ct)) return;

        foreach (var deviceKey in promotion.TargetDevices.Except(promotion.CompletedDevices).ToArray())
        {
            state = await ChangeLeasedAsync(key, id, lease,
                (_, item) => item.LeaseUntil = clock.UtcNow.AddMinutes(2), ct);
            promotion = state?.Promotions.SingleOrDefault(item => item.Id == id);
            if (state == null || promotion == null) return;
            if (!await EligibleAsync(state, promotion, ct))
            {
                await ChangeLeasedAsync(key, id, lease, (current, item) => current.Promotions.Remove(item), ct);
                return;
            }
            account = await accounts.GetAsync(userId, ct);
            account.Devices.TryGetValue(deviceKey, out var device);
            var result = PushResult.InvalidDevice;
            if (device != null && device.UpdatedAt.AddDays(90) > clock.UtcNow
                && await accounts.IsOwnedAsync(userId, deviceKey, ct))
            {
                var group = await groups.LoadAsync(state.OwnerId, state.GroupId, ct);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(30));
                try
                {
                    result = await sender.SendPromotionAsync(device.Token, userId, state, promotion, group.Language, timeout.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested) { result = PushResult.Retry; }
            }
            if (result is PushResult.Retry or PushResult.Unavailable)
            {
                await ChangeLeasedAsync(key, id, lease, (_, item) =>
                {
                    item.Failures = Math.Min(10, item.Failures + 1);
                    item.RetryAt = clock.UtcNow.AddSeconds(Math.Min(3600, 15 * Math.Pow(2, item.Failures)));
                    item.LeaseOwner = null;
                    item.LeaseUntil = null;
                }, ct);
                return;
            }
            if (result == PushResult.InvalidDevice && device != null)
                await accounts.RemoveByKeyAsync(userId, deviceKey, device, ct);
            if (await ChangeLeasedAsync(key, id, lease, (_, item) => item.CompletedDevices.Add(deviceKey), ct) == null)
                return;
        }
        await ChangeLeasedAsync(key, id, lease, (current, item) => current.Promotions.Remove(item), ct);
    }

    private async Task<InvitationState?> ChangeLeasedAsync(string key, string id, string lease,
        Action<InvitationState, PromotionNotification> change, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 12; attempt++)
        {
            var stored = await store.ReadAsync<InvitationState>(key, ct);
            var promotion = stored?.Value.Promotions.SingleOrDefault(item => item.Id == id);
            if (stored == null || promotion == null || promotion.LeaseOwner != lease || promotion.LeaseUntil <= clock.UtcNow)
                return null;
            change(stored.Value, promotion);
            if (await store.WriteAsync(key, stored.Value, stored.ETag, ct)) return stored.Value;
        }
        throw new InvitationException(409, "Promotion notification changed concurrently. Delivery will retry.");
    }
}

public sealed class InvitationPromotionWorker(IInvitationDocumentStore store, InvitationPromotionProcessor processor,
    ILogger<InvitationPromotionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string? cursor = null;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    var page = await store.ListAsync(InvitationKeys.StatePrefix, cursor, 64, stoppingToken);
                    cursor = page.Continuation;
                    foreach (var key in page.Keys)
                    {
                        try { await processor.ProcessAsync(key, stoppingToken); }
                        catch (InvitationException ex)
                        {
                            logger.LogWarning("Promotion notification deferred (status {Status}).", ex.StatusCode);
                        }
                        catch (RequestFailedException ex)
                        {
                            logger.LogWarning("Promotion notification storage failed (status {Status}).", ex.Status);
                        }
                        catch (InvalidDataException)
                        {
                            logger.LogError("Promotion notification has invalid stored data; delivery was skipped.");
                        }
                        catch (HttpRequestException)
                        {
                            logger.LogWarning("Promotion notification transport failed; delivery will retry.");
                        }
                    }
                }
                catch (RequestFailedException ex)
                {
                    logger.LogWarning("Promotion notification polling failed (status {Status}).", ex.Status);
                }
                catch (HttpRequestException)
                {
                    logger.LogWarning("Promotion notification polling transport failed.");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
