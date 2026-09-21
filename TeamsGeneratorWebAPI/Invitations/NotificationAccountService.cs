namespace TeamsGeneratorWebAPI.Invitations;

public sealed class NotificationAccountService
{
    private readonly IInvitationDocumentStore _store;
    private readonly IInvitationClock _clock;
    public NotificationAccountService(IInvitationDocumentStore store, IInvitationClock clock) => (_store, _clock) = (store, clock);

    public async Task<NotificationAccount> GetAsync(string userId, CancellationToken ct) =>
        (await _store.ReadAsync<NotificationAccount>(InvitationKeys.Account(userId), ct))?.Value ?? new();

    public async Task<NotificationPreferences> SetPreferencesAsync(string userId, NotificationPreferences preferences, CancellationToken ct)
    {
        if (preferences.PlayerThreshold is < 1 or > 50 || preferences.IntervalMinutes is < 60 or > 1440)
            throw new InvitationException(400, "playerThreshold must be 1..50 and intervalMinutes must be 60..1440.");
        await UpdateAsync(userId, account => account.Preferences = preferences, ct);
        return preferences;
    }

    public async Task RegisterAsync(string userId, DeviceRequest request, CancellationToken ct)
    {
        ValidateToken(request.Token);
        if (request.Platform != "android") throw new InvitationException(400, "Only the android platform is supported.");
        var key = InvitationKeys.Hash(request.Token);
        var existingAccount = await GetAsync(userId, ct);
        if (!existingAccount.Devices.ContainsKey(key) && existingAccount.Devices.Count >= 20)
            throw new InvitationException(409, "Remove an old device before registering another.");
        var ownership = await ClaimDeviceAsync(userId, key, ct);
        for (var attempt = 0; attempt < 12; attempt++)
        {
            await UpdateAsync(userId, account =>
            {
                if (!account.Devices.ContainsKey(key) && account.Devices.Count >= 20)
                    throw new InvitationException(409, "Remove an old device before registering another.");
                account.Devices[key] = new(request.Token, ownership.RegisteredAt, ownership.Version);
            }, ct);
            var current = await _store.ReadAsync<DeviceOwnership>(InvitationKeys.DeviceOwner(key), ct);
            if (current?.Value.OwnerId != userId)
                throw new InvitationException(409, "Device ownership changed during registration. Please retry.");
            if (current.Value.Version == ownership.Version) return;
            ownership = current.Value;
        }
        throw new InvitationException(409, "Device registration changed concurrently. Please retry.");
    }

    public async Task RemoveAsync(string userId, string token, CancellationToken ct)
    {
        ValidateToken(token);
        await RemoveByKeyAsync(userId, InvitationKeys.Hash(token), null, ct);
    }

    public async Task<bool> IsOwnedAsync(string userId, string key, CancellationToken ct) =>
        (await _store.ReadAsync<DeviceOwnership>(InvitationKeys.DeviceOwner(key), ct))?.Value.OwnerId == userId;

    public async Task RemoveByKeyAsync(string userId, string key, RegisteredDevice? expected, CancellationToken ct)
    {
        var removed = false;
        for (var attempt = 0; attempt < 12; attempt++)
        {
            var ownership = await _store.ReadAsync<DeviceOwnership>(InvitationKeys.DeviceOwner(key), ct);
            if (ownership?.Value.OwnerId != userId) { removed = true; break; }
            if (expected != null && ownership.Value.Version != expected.OwnershipVersion) return;
            if (await _store.WriteAsync(InvitationKeys.DeviceOwner(key),
                new DeviceOwnership(null, Guid.NewGuid().ToString("N"), _clock.UtcNow), ownership.ETag, ct))
            { removed = true; break; }
        }
        if (!removed) throw new InvitationException(409, "Device ownership changed during removal. Please retry.");
        await UpdateAsync(userId, account =>
        {
            if (account.Devices.TryGetValue(key, out var device)
                && (expected == null || device == expected))
                account.Devices.Remove(key);
        }, ct);
    }

    private async Task<DeviceOwnership> ClaimDeviceAsync(string userId, string key, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 12; attempt++)
        {
            var previous = await _store.ReadAsync<DeviceOwnership>(InvitationKeys.DeviceOwner(key), ct);
            var ownership = new DeviceOwnership(userId, Guid.NewGuid().ToString("N"), _clock.UtcNow);
            if (!await _store.WriteAsync(InvitationKeys.DeviceOwner(key), ownership, previous?.ETag, ct)) continue;
            // The global ETag record is authority; account device maps are only inventories.
            if (previous?.Value.OwnerId is string oldOwner && oldOwner != userId)
                await UpdateAsync(oldOwner, account =>
                {
                    if (account.Devices.TryGetValue(key, out var device)
                        && device.OwnershipVersion == previous.Value.Version)
                        account.Devices.Remove(key);
                }, ct);
            return ownership;
        }
        throw new InvitationException(409, "Device ownership changed during registration. Please retry.");
    }

    private async Task UpdateAsync(string userId, Action<NotificationAccount> change, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 12; attempt++)
        {
            var stored = await _store.ReadAsync<NotificationAccount>(InvitationKeys.Account(userId), ct);
            var account = stored?.Value ?? new();
            change(account);
            if (await _store.WriteAsync(InvitationKeys.Account(userId), account, stored?.ETag, ct)) return;
        }
        throw new InvitationException(409, "Notification settings changed concurrently. Please retry.");
    }

    private static void ValidateToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 4096 || token.Any(char.IsWhiteSpace))
            throw new InvitationException(400, "A valid device token is required.");
    }
}
