using System.Globalization;
using System.Security.Cryptography;
using TeamsGeneratorWebAPI.Telemetry;
using TeamsGeneratorWebAPI.PlayerProfiles;

namespace TeamsGeneratorWebAPI.Invitations;

public sealed class InvitationService
{
    private readonly IInvitationDocumentStore _store;
    private readonly IInvitationGroupSource _groups;
    private readonly IInvitationClock _clock;
    private readonly IUsageTelemetry? _telemetry;
    private readonly bool _promotionsEnabled;
    public InvitationService(IInvitationDocumentStore store, IInvitationGroupSource groups, IInvitationClock clock,
        IUsageTelemetry? telemetry = null, IConfiguration? configuration = null)
    {
        (_store, _groups, _clock, _telemetry) = (store, groups, clock, telemetry);
        _promotionsEnabled = configuration?.GetValue<bool>("Notifications:Promotions:Enabled") == true;
    }

    public async Task<OrganizerInvitation> CreateAsync(string requester, CreateInvitationRequest request, CancellationToken ct)
    {
        var at = ParseScheduledAt(request.ScheduledAt);
        if (at <= _clock.UtcNow) throw new InvitationException(400, "scheduledAt must be in the future.");
        if (at > DateTimeOffset.MaxValue.AddDays(-30))
            throw new InvitationException(400, "scheduledAt is outside the supported date range.");
        ValidateTimeZone(request.TimeZone);
        var group = await _groups.ResolveAsync(requester, request.GroupId, ct);
        if (!group.InvitationsEnabled)
            throw new InvitationException(403, "Match invitations must be enabled by the group owner.");
        var id = InvitationKeys.Id(group.OwnerId, group.GroupId, at);
        var key = InvitationKeys.State(id);
        var existing = await _store.ReadAsync<InvitationState>(key, ct);
        var created = false;
        if (existing == null)
        {
            var state = new InvitationState
            {
                Id = id, OwnerId = group.OwnerId, GroupId = group.GroupId,
                Token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_'),
                GroupName = group.Name, MatchName = group.MatchName, Location = group.Location, GroupLogoDataUrl = group.Logo,
                ScheduledAt = at, ExpiresAt = at.AddDays(30), TimeZone = request.TimeZone, Capacity = group.Capacity,
                EventDurationMinutes = group.EventDurationMinutes
            };
            created = await _store.WriteAsync(key, state, null, ct);
            existing = await _store.ReadAsync<InvitationState>(key, ct)
                ?? throw new InvitationException(503, "Invitation could not be created.");
        }
        if (existing.Value.Revoked) throw new InvitationException(410, "This occurrence's invitation has been revoked.");
        // Repair a missing index after a crash between creating the state and its capability pointer.
        await _store.WriteAsync(InvitationKeys.Token(existing.Value.Token), new TokenPointer(id), null, ct);
        _telemetry?.Track(created ? "InvitationLinkCreated" : "InvitationLinkReused", userId: requester,
            properties: new Dictionary<string, string?> { ["actor_type"] = "organizer", ["outcome"] = "succeeded" },
            measurements: new Dictionary<string, double> { ["capacity"] = existing.Value.Capacity });
        return (OrganizerInvitation)Project(existing.Value, group, _clock.UtcNow, true);
    }

    public async Task<OrganizerInvitation?> CurrentAsync(string requester, string groupId, string scheduledAt, CancellationToken ct)
    {
        var at = ParseScheduledAt(scheduledAt);
        var group = await _groups.ResolveAsync(requester, groupId, ct);
        var state = await _store.ReadAsync<InvitationState>(
            InvitationKeys.State(InvitationKeys.Id(group.OwnerId, group.GroupId, at)), ct);
        return state == null || state.Value.Revoked ? null
            : (OrganizerInvitation)Project(state.Value, group, _clock.UtcNow, true);
    }

    public async Task<OrganizerInvitation> GetForRecipientAsync(string requester, string id, CancellationToken ct)
    {
        if (id.Length != 64 || id.Any(c => !(c is >= '0' and <= '9' or >= 'a' and <= 'f')))
            throw new InvitationException(404, "Invitation not found.");
        var stored = await _store.ReadAsync<InvitationState>(InvitationKeys.State(id), ct);
        // Digests currently target only the canonical owner; group membership alone is not recipient authority.
        if (stored == null || !string.Equals(stored.Value.OwnerId, requester, StringComparison.Ordinal))
            throw new InvitationException(404, "Invitation not found.");
        var state = stored.Value;
        var group = await _groups.ResolveAsync(requester, state.GroupId, ct);
        if (group.OwnerId != state.OwnerId || group.GroupId != state.GroupId)
            throw new InvitationException(403, "Group access denied.");
        if (state.Revoked) throw new InvitationException(410, "This occurrence's invitation has been revoked.");
        return (OrganizerInvitation)Project(state, group, _clock.UtcNow, true);
    }

    public async Task<PublicInvitation> PublicAsync(string token, CancellationToken ct)
    {
        var state = await GetCapabilityAsync(token, ct);
        var group = await _groups.LoadAsync(state.Value.OwnerId, state.Value.GroupId, ct);
        return Project(state.Value, group, _clock.UtcNow);
    }

    public async Task<OwnInvitation> OwnAsync(string user, string token, CancellationToken ct)
    {
        var state = await GetCapabilityAsync(token, ct);
        var group = await _groups.LoadAsync(state.Value.OwnerId, state.Value.GroupId, ct);
        var playerKey = await new PlayerMembershipService(_store).ResolveAsync(user, group, ct);
        return new(Project(state.Value, group, _clock.UtcNow), playerKey);
    }

    public Task<PublicInvitation> RespondOwnAsync(string user, string token, OwnInvitationResponseRequest request, CancellationToken ct) =>
        RespondCoreAsync(token, new("", request.Status), ct, user);

    public Task<PublicInvitation> RespondAsync(string token, InvitationResponseRequest request, CancellationToken ct) =>
        RespondCoreAsync(token, request, ct);

    private async Task<PublicInvitation> RespondCoreAsync(string token, InvitationResponseRequest request, CancellationToken ct,
        string? user = null)
    {
        if (request.Status is not ("in" or "out")
            || (user == null && (string.IsNullOrEmpty(request.PlayerKey) || request.PlayerKey.Length > 256)))
            throw new InvitationException(400, "An existing playerKey and status 'in' or 'out' are required.");
        for (var attempt = 0; attempt < 12; attempt++)
        {
            var stored = await GetCapabilityAsync(token, ct);
            var state = stored.Value;
            if (state.ScheduledAt <= _clock.UtcNow) throw new InvitationException(409, "This occurrence is closed.");
            var group = await _groups.LoadAsync(state.OwnerId, state.GroupId, ct);
            if (user != null)
                request = request with { PlayerKey = await new PlayerMembershipService(_store).ResolveAsync(user, group, ct)
                    ?? throw new InvitationException(403, "Your account is not linked to a current player in this group.") };
            if (!group.InvitationsEnabled)
                throw new InvitationException(409, "This occurrence is closed.");
            if (!group.Players.Any(player => player.Key == request.PlayerKey))
                throw new InvitationException(400, "The player is no longer in this group.");
            if (user == null && await new PlayerMembershipService(_store).RequiresAccountAsync(group, request.PlayerKey, ct))
                throw new InvitationException(403, "Sign in with this player's linked account to respond.");
            var previous = state.Responses.GetValueOrDefault(request.PlayerKey);
            if (previous?.Status == request.Status || (previous == null && request.Status == "out"))
                return Project(state, group, _clock.UtcNow);
            var now = _clock.UtcNow;
            if (state.ScheduledAt <= now) throw new InvitationException(409, "This occurrence is closed.");
            var before = _telemetry == null && !_promotionsEnabled ? null : Project(state, group, now);
            state.Revision++;
            state.Responses[request.PlayerKey] = new Rsvp(request.Status, state.Revision);
            state.Events.Add(new(state.Revision, request.PlayerKey, request.Status, now));
            RecomputePending(state, group.Players);
            var result = Project(state, group, now);
            if (_promotionsEnabled && before != null)
            {
                var confirmed = result.Players.Where(player => player.Status == "in" && !player.WaitingListPosition.HasValue)
                    .Select(player => player.Key).ToHashSet(StringComparer.Ordinal);
                state.Promotions.RemoveAll(item => !confirmed.Contains(item.PlayerKey)
                    || state.Responses.GetValueOrDefault(item.PlayerKey)?.Order != item.ResponseOrder);
                foreach (var promoted in before.Players.Where(player => player.WaitingListPosition.HasValue
                    && confirmed.Contains(player.Key)))
                {
                    var profileId = await new PlayerMembershipService(_store).LinkedProfileAsync(group, promoted.Key, ct);
                    if (profileId == null) continue;
                    state.Promotions.Add(new PromotionNotification
                    {
                        Id = InvitationKeys.Hash($"{state.Id}:{state.Revision}:{promoted.Key}"),
                        PlayerKey = promoted.Key, ProfileId = profileId,
                        ResponseOrder = state.Responses[promoted.Key].Order
                    });
                }
            }
            if (await _store.WriteAsync(InvitationKeys.State(state.Id), state, stored.ETag, ct))
            {
                if (_telemetry != null && before != null)
                {
                    var waiting = before.Players.Where(player => player.WaitingListPosition.HasValue)
                        .Select(player => player.Key).ToHashSet(StringComparer.Ordinal);
                    _telemetry!.Track("InvitationResponseChanged",
                        properties: new Dictionary<string, string?>
                        {
                            ["actor_type"] = "participant",
                            ["outcome"] = "succeeded",
                            ["response_status"] = request.Status,
                            ["previous_status"] = EffectiveStatus(before.Players.Single(player => player.Key == request.PlayerKey)),
                            ["new_status"] = EffectiveStatus(result.Players.Single(player => player.Key == request.PlayerKey))
                        },
                        measurements: new Dictionary<string, double>
                        {
                            ["capacity"] = result.Capacity,
                            ["in_count"] = result.Counts.In,
                            ["waiting_count"] = result.Counts.Waiting,
                            ["out_count"] = result.Counts.Out,
                            ["promoted_count"] = result.Players.Count(player => waiting.Contains(player.Key)
                                && player.Status == "in" && !player.WaitingListPosition.HasValue)
                        });
                }
                return result;
            }
        }
        throw new InvitationException(409, "Responses changed concurrently. Please retry.");
    }

    public async Task RevokeAsync(string requester, string groupId, string scheduledAt, CancellationToken ct)
    {
        var at = ParseScheduledAt(scheduledAt);
        var group = await _groups.ResolveAsync(requester, groupId, ct);
        var key = InvitationKeys.State(InvitationKeys.Id(group.OwnerId, group.GroupId, at));
        for (var attempt = 0; attempt < 12; attempt++)
        {
            var stored = await _store.ReadAsync<InvitationState>(key, ct);
            if (stored == null || stored.Value.Revoked) return;
            stored.Value.Revoked = true;
            stored.Value.Promotions.Clear();
            if (await _store.WriteAsync(key, stored.Value, stored.ETag, ct))
            {
                _telemetry?.Track("InvitationRevoked", userId: requester,
                    properties: new Dictionary<string, string?> { ["actor_type"] = "organizer", ["outcome"] = "succeeded" });
                return;
            }
        }
        throw new InvitationException(409, "Invitation changed concurrently. Please retry.");
    }

    private static string EffectiveStatus(InvitationPlayer player) =>
        player.WaitingListPosition.HasValue ? "waiting" : player.Status;

    private async Task<StoredDocument<InvitationState>> GetCapabilityAsync(string token, CancellationToken ct)
    {
        if (token.Length != 43 || token.Any(c => !(c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_')))
            throw new InvitationException(404, "Invitation not found.");
        var pointer = await _store.ReadAsync<TokenPointer>(InvitationKeys.Token(token), ct);
        var state = pointer == null ? null
            : await _store.ReadAsync<InvitationState>(InvitationKeys.State(pointer.Value.InvitationId), ct);
        if (state == null || state.Value.Revoked || !string.Equals(state.Value.Token, token, StringComparison.Ordinal))
            throw new InvitationException(404, "Invitation not found.");
        if (state.Value.ExpiresAt <= _clock.UtcNow) throw new InvitationException(410, "Invitation expired.");
        return state;
    }

    public static DateTimeOffset ParseScheduledAt(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 40 || !value.Contains('T')
            || !(value.EndsWith("Z", StringComparison.Ordinal) || value.EndsWith("+00:00", StringComparison.Ordinal))
            || !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var at)
            || at.Offset != TimeSpan.Zero)
            throw new InvitationException(400, "scheduledAt must be an ISO UTC timestamp.");
        return at.ToUniversalTime();
    }

    public static void ValidateTimeZone(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 100
            || (value != "UTC" && !TimeZoneInfo.TryConvertIanaIdToWindowsId(value, out _)))
            throw new InvitationException(400, "timeZone must be an IANA time zone.");
    }

    public static Dictionary<string, bool> Snapshot(InvitationState state, IReadOnlyList<RosterPlayer> roster) =>
        roster.ToDictionary(player => player.Key, player => state.Responses.GetValueOrDefault(player.Key)?.Status == "in");

    public static Dictionary<string, bool> NetChanges(InvitationState state, IReadOnlyList<RosterPlayer> roster) =>
        Snapshot(state, roster).Where(item =>
        {
            var response = state.Responses.GetValueOrDefault(item.Key);
            // A real withdrawal is meaningful even when its preceding In was not yet delivered.
            return item.Value != state.Baseline.GetValueOrDefault(item.Key)
                || (response?.Status == "out" && response.Order > state.BaselineRevision);
        })
            .ToDictionary(item => item.Key, item => item.Value);

    public static void RecomputePending(InvitationState state, IReadOnlyList<RosterPlayer> roster)
    {
        var changes = NetChanges(state, roster);
        if (changes.Count == 0) { state.PendingSince = null; return; }
        state.PendingSince ??= state.Events.Where(item => item.Revision > state.BaselineRevision && changes.ContainsKey(item.PlayerKey))
            .Select(item => (DateTimeOffset?)item.At).Min();
    }

    private static PublicInvitation Project(InvitationState state, InvitationGroup group, DateTimeOffset now,
        bool organizer = false)
    {
        var result = Project(state, group.Players, now, organizer);
        result.Language = group.Language;
        if (!group.InvitationsEnabled) result.Status = "closed";
        return result;
    }

    public static PublicInvitation Project(InvitationState state, IReadOnlyList<RosterPlayer> roster, DateTimeOffset now,
        bool organizer = false)
    {
        PublicInvitation result = organizer
            ? new OrganizerInvitation { Token = state.Token, GroupId = state.GroupId }
            : new PublicInvitation();
        result.Id = state.Id;
        result.GroupName = state.GroupName;
        result.MatchName = state.MatchName;
        result.Location = state.Location;
        result.GroupLogoDataUrl = state.GroupLogoDataUrl;
        result.ScheduledAt = state.ScheduledAt;
        result.TimeZone = state.TimeZone;
        result.EventDurationMinutes = state.EventDurationMinutes;
        result.Capacity = state.Capacity;
        result.Status = state.Revoked || state.ScheduledAt <= now || state.ExpiresAt <= now ? "closed" : "open";
        result.Revision = state.Revision;
        var liveKeys = roster.Select(player => player.Key).ToHashSet(StringComparer.Ordinal);
        var positions = state.Responses.Where(item => liveKeys.Contains(item.Key) && item.Value.Status == "in")
            .OrderBy(item => item.Value.Order).Select((item, index) => (item.Key, Position: index + 1))
            .ToDictionary(item => item.Key, item => item.Position);
        result.Players = roster.Select(player =>
        {
            var position = positions.GetValueOrDefault(player.Key);
            return new InvitationPlayer(player.Key, player.Name, state.Responses.GetValueOrDefault(player.Key)?.Status ?? "unanswered",
                position > state.Capacity ? position - state.Capacity : null);
        }).ToList();
        result.Counts = new(Math.Min(positions.Count, state.Capacity), Math.Max(0, positions.Count - state.Capacity),
            result.Players.Count(player => player.Status == "out"));
        return result;
    }
}
