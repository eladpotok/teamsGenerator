using Newtonsoft.Json;
using TeamsGenerator.API;
using TeamsGeneratorWebAPI.GroupJoins;
using TeamsGeneratorWebAPI.Invitations;

namespace TeamsGeneratorWebAPI.PlayerProfiles;

public sealed record PlayerSkillProfile(int AlgorithmKey, string Name, string Background,
    List<SkillDefinition> Skills, Dictionary<string, double> Ratings, List<int> Positions)
{
    public bool IsGoalKeeper { get; init; }
    public bool IsRecoveringFromInjury { get; init; }
    public double? GoalkeeperSkill { get; init; }
}
public sealed record PlayerJoinStatus(string Id, string Status, int Revision, PlayerSkillProfile Profile);
public sealed record PlayerMembership(string OwnerId, string GroupId, string GroupName, List<string> PlayerKeys)
{
    public string? GroupLogoDataUrl { get; init; }
    public PlayerJoinStatus? LastRequest { get; init; }
    public Dictionary<string, PlayerSkillProfile> ApprovedProfiles { get; init; } = new();
}
public sealed record PlayerGroupMembership(string GroupKey, string GroupName, string? GroupLogoDataUrl,
    string Status, PlayerSkillProfile? Profile);
public sealed record PlayerMembershipBinding(string ProfileId);

public sealed class PlayerMembershipService(IInvitationDocumentStore store)
{
    private static string Prefix(string profileId) => "player-memberships/v1/profiles/" + profileId + "/";
    internal static string GroupKey(string owner, string group) =>
        InvitationKeys.Hash(JsonConvert.SerializeObject(new[] { owner, group }));
    internal static string Key(string profileId, InvitationGroup group) =>
        Prefix(profileId) + GroupKey(group.OwnerId, group.GroupId);
    private static string BindingKey(InvitationGroup group, string playerKey) =>
        "player-membership-links/v1/groups/" + GroupKey(group.OwnerId, group.GroupId) + "/" + playerKey;

    private static PlayerSkillProfile Skills(GroupJoinRequest request) => new(
        request.ApprovedAlgorithm ?? request.SubmittedAlgorithm ?? 0, request.Name, request.Background,
        request.Skills, request.Status == "approved" ? request.FinalRatings ?? new() : request.SelfRatings, request.Positions)
    {
        IsGoalKeeper = request.IsGoalKeeper, IsRecoveringFromInjury = request.IsRecoveringFromInjury,
        GoalkeeperSkill = request.GoalkeeperSkill
    };
    private static PlayerJoinStatus Snapshot(GroupJoinRequest request) =>
        new(request.Id, request.Status, request.Revision, Skills(request));

    // Write the discoverable pointer before the request, then refresh it after each canonical transition.
    internal async Task TrackAsync(InvitationGroup group, string? profileId, CancellationToken ct)
    {
        if (profileId == null) return;
        var key = Key(profileId, group);
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var stored = await store.ReadAsync<PlayerMembership>(key, ct);
            var state = await store.ReadAsync<GroupJoinState>(GroupJoinService.StateKey(group), ct);
            var request = state?.Value.Requests.LastOrDefault(item => item.ProfileId == profileId);
            var membership = (stored?.Value ?? new(group.OwnerId, group.GroupId, group.Name, new())) with
            {
                GroupName = group.Name, GroupLogoDataUrl = group.Logo,
                LastRequest = request == null ? stored?.Value.LastRequest : Snapshot(request)
            };
            if (stored != null && JsonConvert.SerializeObject(stored.Value) == JsonConvert.SerializeObject(membership)) return;
            if (await store.WriteAsync(key, membership, stored?.ETag, ct)) return;
        }
        throw new InvitationException(409, "The account membership changed. Please retry.");
    }

    internal async Task RecordAsync(InvitationGroup group, GroupJoinRequest request, CancellationToken ct)
    {
        if (request.ProfileId == null) return;
        if (request.Status is not ("approved" or "approving")
            || !await new GroupJoinRoster(store).HasReceiptAsync(group, request, ct))
            throw new InvitationException(409, "The roster approval must finish before linking this account.");
        var key = Key(request.ProfileId, group);
        var playerKey = "join_" + request.Id;
        await BindAsync(group, playerKey, request.ProfileId, ct);
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var stored = await store.ReadAsync<PlayerMembership>(key, ct);
            var membership = stored?.Value ?? new(group.OwnerId, group.GroupId, group.Name, new());
            if (membership.PlayerKeys.Contains(playerKey, StringComparer.Ordinal)
                && membership.ApprovedProfiles.ContainsKey(playerKey)) return;
            if (!membership.PlayerKeys.Contains(playerKey, StringComparer.Ordinal)) membership.PlayerKeys.Add(playerKey);
            membership.ApprovedProfiles[playerKey] = Skills(request) with { Ratings = request.FinalRatings ?? new() };
            if (await store.WriteAsync(key, membership, stored?.ETag, ct)) return;
        }
        throw new InvitationException(409, "The account membership changed. Retry the approval.");
    }

    internal async Task BindAsync(InvitationGroup group, string playerKey, string profileId, CancellationToken ct)
    {
        var key = BindingKey(group, playerKey);
        var existing = await store.ReadAsync<PlayerMembershipBinding>(key, ct);
        if (existing?.Value.ProfileId == profileId) return;
        if (existing != null || !await store.WriteAsync(key, new PlayerMembershipBinding(profileId), null, ct))
        {
            existing = await store.ReadAsync<PlayerMembershipBinding>(key, ct);
            if (existing?.Value.ProfileId != profileId)
                throw new InvitationException(409, "This roster entry is linked to another account.");
        }
    }

    public async Task<bool> RequiresAccountAsync(InvitationGroup group, string playerKey, CancellationToken ct)
    {
        if (await store.ReadAsync<PlayerMembershipBinding>(BindingKey(group, playerKey), ct) != null) return true;
        if (!playerKey.StartsWith("join_", StringComparison.Ordinal)) return false;
        var state = await store.ReadAsync<GroupJoinState>(GroupJoinService.StateKey(group), ct);
        var request = state?.Value.Requests.FirstOrDefault(item => "join_" + item.Id == playerKey);
        // Unknown historical enrollments fail closed; explicit pre-account requests retain the legacy flow.
        return request == null || request.ProfileId != null;
    }

    public Task<string?> ResolveAsync(string uid, InvitationGroup group, CancellationToken ct) =>
        ResolveProfileAsync(PlayerProfileService.Id(uid), group, ct);

    internal async Task<string?> ResolveProfileAsync(string profileId, InvitationGroup group, CancellationToken ct)
    {
        var membership = await store.ReadAsync<PlayerMembership>(Key(profileId, group), ct);
        var keys = membership?.Value.PlayerKeys.ToHashSet(StringComparer.Ordinal) ?? new();
        // Older, retained approvals can identify the account without scanning other groups or matching names.
        var state = await store.ReadAsync<GroupJoinState>(GroupJoinService.StateKey(group), ct);
        foreach (var request in state?.Value.Requests ?? Enumerable.Empty<GroupJoinRequest>())
            if (request.ProfileId == profileId && request.Status == "approved"
                && !keys.Contains("join_" + request.Id)
                && await new GroupJoinRoster(store).HasReceiptAsync(group, request, ct))
                keys.Add("join_" + request.Id);
        var current = group.Players.Where(player => keys.Contains(player.Key)).ToArray();
        if (current.Length > 1)
            throw new InvitationException(409, "More than one roster entry is linked to your account. Ask the organizer to resolve the duplicate.");
        return current.SingleOrDefault()?.Key;
    }

    public async Task<IReadOnlyList<PlayerMembership>> ListAsync(string uid, CancellationToken ct)
    {
        var memberships = new List<PlayerMembership>();
        string? continuation = null;
        do
        {
            var page = await store.ListAsync(Prefix(PlayerProfileService.Id(uid)), continuation, 100, ct);
            foreach (var key in page.Keys)
            {
                var stored = await store.ReadAsync<PlayerMembership>(key, ct);
                if (stored != null) memberships.Add(stored.Value);
            }
            continuation = page.Continuation;
        } while (continuation != null);
        return memberships;
    }

    public async Task<IReadOnlyList<PlayerGroupMembership>> SummariesAsync(string uid, IInvitationGroupSource groups, CancellationToken ct)
    {
        var profileId = PlayerProfileService.Id(uid);
        var results = new List<PlayerGroupMembership>();
        foreach (var membership in await ListAsync(uid, ct))
        {
            InvitationGroup group;
            try { group = await groups.LoadAsync(membership.OwnerId, membership.GroupId, ct); }
            catch (InvitationException error) when (error.StatusCode == 404)
            {
                results.Add(new(GroupKey(membership.OwnerId, membership.GroupId), membership.GroupName,
                    membership.GroupLogoDataUrl, "unavailable", null));
                continue;
            }
            var state = await store.ReadAsync<GroupJoinState>(GroupJoinService.StateKey(group), ct);
            var request = state?.Value.Requests.LastOrDefault(item => item.ProfileId == profileId);
            var latest = request == null ? membership.LastRequest : Snapshot(request);
            var current = await ResolveProfileAsync(profileId, group, ct);
            var approved = current == null ? null : membership.ApprovedProfiles.GetValueOrDefault(current);
            if (approved == null && current != null)
            {
                var retained = state?.Value.Requests.FirstOrDefault(item => "join_" + item.Id == current && item.Status == "approved");
                if (retained != null) approved = Skills(retained);
            }
            var status = current != null ? "approved" : latest?.Status switch
            {
                "pending" => "pending", "approving" => "approving", "rejected" => "rejected",
                _ => membership.PlayerKeys.Count > 0 ? "removed" : null
            };
            if (status != null) results.Add(new(GroupKey(group.OwnerId, group.GroupId), group.Name, group.Logo, status,
                current != null ? approved : latest?.Status is "pending" or "rejected" ? latest.Profile : null));
        }
        return results.OrderBy(group => group.GroupName, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
