using System.Security.Cryptography;
using Newtonsoft.Json;
using TeamsGenerator.API;
using TeamsGenerator.Orchestration;
using TeamsGeneratorWebAPI.Invitations;
using TeamsGeneratorWebAPI.Premium;
using TeamsGeneratorWebAPI.PlayerProfiles;

namespace TeamsGeneratorWebAPI.GroupJoins;

public sealed class GroupJoinService
{
    private readonly IInvitationDocumentStore _store;
    private readonly IInvitationGroupSource _groups;
    private readonly IInvitationClock _clock;
    private readonly GroupJoinRoster _roster;
    private readonly IGroupJoinAnalyzer _analyzer;
    private readonly IAccountEntitlementService _entitlements;
    private readonly bool _allowRosterEnrollment;
    public GroupJoinService(IInvitationDocumentStore store, IInvitationGroupSource groups, IInvitationClock clock,
        GroupJoinRoster roster, IGroupJoinAnalyzer analyzer, IAccountEntitlementService entitlements,
        IConfiguration? configuration = null)
    {
        (_store, _groups, _clock, _roster, _analyzer) = (store, groups, clock, roster, analyzer);
        _entitlements = entitlements;
        _allowRosterEnrollment = configuration?.GetValue<bool?>("GroupJoins:AllowRosterEnrollment") ?? true;
    }

    internal static string StateKey(InvitationGroup group) =>
        "group-joins/v1/groups/" + InvitationKeys.Hash(JsonConvert.SerializeObject(new[] { group.OwnerId, group.GroupId }));
    private static string TokenKey(string token) => "group-joins/v1/tokens/" + InvitationKeys.Hash(token);
    private static void Enabled(InvitationGroup group)
    {
        if (!group.GroupJoinRequestsEnabled) throw new InvitationException(403, "Group join requests are not enabled.");
    }
    private async Task<InvitationGroup> Organizer(string user, string groupId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(user)) throw new InvitationException(401, "Sign in to manage group requests.");
        var group = await _groups.ResolveAsync(user, groupId, ct);
        Enabled(group);
        return group;
    }

    public async Task<GroupJoinLink> LinkAsync(string user, string groupId, bool create, CancellationToken ct)
    {
        var group = await Organizer(user, groupId, ct);
        var key = StateKey(group);
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var stored = await _store.ReadAsync<GroupJoinState>(key, ct);
            if (stored?.Value.Token != null || !create) return new(stored?.Value.Token, _allowRosterEnrollment);
            var state = stored?.Value ?? new GroupJoinState { OwnerId = group.OwnerId, GroupId = group.GroupId };
            state.Token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            if (!await _store.WriteAsync(TokenKey(state.Token), new GroupJoinCapability(key), null, ct)) continue;
            if (await _store.WriteAsync(key, state, stored?.ETag, ct)) return new(state.Token, _allowRosterEnrollment);
        }
        throw Changed();
    }

    public async Task RevokeAsync(string user, string groupId, CancellationToken ct)
    {
        var group = await Organizer(user, groupId, ct);
        var key = StateKey(group);
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var stored = await _store.ReadAsync<GroupJoinState>(key, ct);
            if (stored?.Value.Token == null) return;
            stored.Value.Token = null;
            if (await _store.WriteAsync(key, stored.Value, stored.ETag, ct)) return;
        }
        throw Changed();
    }

    private async Task<(InvitationGroup Group, string Key, StoredDocument<GroupJoinState> Stored)> PublicState(string token, CancellationToken ct)
    {
        if (token.Length != 64 || !token.All(Uri.IsHexDigit)) throw Unavailable();
        var pointer = await _store.ReadAsync<GroupJoinCapability>(TokenKey(token), ct) ?? throw Unavailable();
        var stored = await _store.ReadAsync<GroupJoinState>(pointer.Value.StateKey, ct) ?? throw Unavailable();
        if (stored.Value.Token != token) throw Unavailable();
        var group = await _groups.LoadAsync(stored.Value.OwnerId, stored.Value.GroupId, ct);
        Enabled(group);
        return (group, pointer.Value.StateKey, stored);
    }

    public async Task<PublicGroupJoin> PublicAsync(string token, CancellationToken ct)
    {
        var (group, _, _) = await PublicState(token, ct);
        var config = group.Configuration;
        var repeating = config.ScheduleType == "repeating";
        return new(group.Name, group.MatchName, group.Location, group.Logo, group.Language,
            config.ScheduleType, repeating ? config.RepeatDay : null, repeating ? config.RepeatTime : null,
            repeating ? null : config.EventDate.ToString("yyyy-MM-dd"), repeating ? null : config.EventTime.ToString("HH:mm"),
            group.EventDurationMinutes, config.MatchdayRules?.Clone(), JoinSkills(config), config.SelectedAlgoKey);
    }

    public async Task SubmitProfileAsync(string user, string token, ProfileGroupJoinSubmission input, CancellationToken ct)
    {
        var profile = await new PlayerProfileService(_store, _clock).GetAsync(user, ct);
        if (input == null || profile.Revision != input.ProfileRevision)
            throw new InvitationException(409, "Your player profile changed. Reload it before saving or requesting to join.");
        await SubmitAsync(token, new(input.SubmissionId, input.Name ?? profile.Name, input.IsGoalKeeper ?? profile.IsGoalKeeper,
            input.Background ?? profile.Background, input.Ratings, input.AlgorithmKey, input.Positions, input.Skills,
            input.IsRecoveringFromInjury ?? profile.IsRecoveringFromInjury, input.GoalkeeperSkill), ct, profile);
    }

    public async Task SubmitAsync(string token, GroupJoinSubmission input, CancellationToken ct, PlayerProfile? profile = null)
    {
        if (input == null || !Guid.TryParseExact(input.SubmissionId, "D", out _)
            || string.IsNullOrWhiteSpace(input.Name) || input.Name.Trim().Length > 80
            || input.Name.Any(char.IsControl) || input.Background is { Length: > 2000 })
            throw new InvitationException(400, "Supply a name (up to 80 characters), optional football background (up to 2000), and a valid submission ID.");
        if (input.GoalkeeperSkill is double goalkeeperSkill
            && (!double.IsFinite(goalkeeperSkill) || goalkeeperSkill < 1 || goalkeeperSkill > 10))
            throw new InvitationException(400, "The goalkeeping rating must be between 1 and 10.");
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var (group, key, stored) = await PublicState(token, ct);
            var skills = JoinSkills(group.Configuration);
            if ((input.AlgorithmKey ?? (int)AlgoType.SkillWise) != group.Configuration.SelectedAlgoKey
                || (input.Skills != null && !SameSkills(input.Skills, skills)))
                throw SkillsChanged();
            ValidateRatings(input.Ratings, skills);
            ValidatePositions(input.Positions, group.Configuration.SelectedAlgoKey);
            var id = InvitationKeys.Hash(key + ":" + (profile == null ? "" : profile.Id + ":") + input.SubmissionId);
            var state = stored.Value;
            var memberships = new PlayerMembershipService(_store);
            if (state.Requests.Any(request => request.Id == id))
            {
                await memberships.TrackAsync(group, profile?.Id, ct);
                return;
            }
            if (profile != null && await memberships.ResolveProfileAsync(profile.Id, group, ct) != null)
                throw new InvitationException(409, "Your account is already a member of this group.");
            if (profile != null && state.Requests.Any(request => request.ProfileId == profile.Id
                && request.Status is "pending" or "approving"))
                throw new InvitationException(409, "Your profile already has a request waiting for review in this group.");
            if (state.Requests.Count(request => request.Status is "pending" or "approving") >= 100)
                throw new InvitationException(429, "This group has too many pending requests. Contact the organizer.");
            if (state.Requests.Any(request => request.Status is "pending" or "approving"
                && string.Equals(request.Name, input.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
                throw new InvitationException(409, "A request with this name is already waiting for review.");
            state.Requests.RemoveAll(request => request.Status is "approved" or "rejected"
                && request.ReviewedAt < _clock.UtcNow.AddDays(-90));
            if (state.Requests.Count >= 500) throw new InvitationException(429, "This group's request limit has been reached. Contact the organizer.");
            await memberships.TrackAsync(group, profile?.Id, ct);
            state.Requests.Add(new GroupJoinRequest
            {
                Id = id, ProfileId = profile?.Id, Name = input.Name.Trim(),
                IsGoalKeeper = group.Configuration.SelectedAlgoKey == 2
                    ? input.Positions is { Count: 1 } && input.Positions[0] == 0 : input.IsGoalKeeper,
                IsRecoveringFromInjury = input.IsRecoveringFromInjury,
                GoalkeeperSkill = input.GoalkeeperSkill.HasValue
                    ? Math.Round(input.GoalkeeperSkill.Value, MidpointRounding.AwayFromZero) : null,
                Background = (input.Background ?? "").Trim(),
                SubmittedAlgorithm = group.Configuration.SelectedAlgoKey, Positions = input.Positions?.ToList() ?? new(),
                Skills = skills, SelfRatings = input.Ratings.ToDictionary(rating => rating.Key,
                    rating => Math.Round(rating.Value, MidpointRounding.AwayFromZero), StringComparer.Ordinal), CreatedAt = _clock.UtcNow
            });
            if (await _store.WriteAsync(key, state, stored.ETag, ct))
            {
                await memberships.TrackAsync(group, profile?.Id, ct);
                return;
            }
        }
        throw Changed();
    }

    public async Task<IReadOnlyList<GroupJoinRequest>> RequestsAsync(string user, string groupId, CancellationToken ct)
    {
        var group = await Organizer(user, groupId, ct);
        var stored = await _store.ReadAsync<GroupJoinState>(StateKey(group), ct);
        return stored?.Value.Requests.OrderByDescending(request => request.CreatedAt).ToArray() ?? Array.Empty<GroupJoinRequest>();
    }

    public async Task<GroupJoinRequest> AnalyzeAsync(string user, string groupId, string id, int revision, CancellationToken ct)
    {
        var group = await Organizer(user, groupId, ct);
        await RequirePremiumAnalysis(user, ct);
        var key = StateKey(group);
        var stored = await ReadState(key, ct);
        var request = Find(stored.Value, id);
        Pending(request, revision);
        if (request.Skills.Count == 0)
            throw new InvitationException(400, "This group uses player descriptions, not numeric skill ratings.");
        var suggestions = await _analyzer.AnalyzeAsync(request,
            await _roster.ContextAsync(group, request.Skills, group.OwnerId == user, ct), group.Language, ct);
        // Recheck live authorization after the external call and never revive a decided request.
        await Organizer(user, groupId, ct);
        await RequirePremiumAnalysis(user, ct);
        request.Suggestions = suggestions;
        request.Revision++;
        if (!await _store.WriteAsync(key, stored.Value, stored.ETag, ct)) throw Changed();
        return request;
    }

    private async Task RequirePremiumAnalysis(string user, CancellationToken ct)
    {
        if (!(await _entitlements.GetAsync(user, cancellationToken: ct)).IsPremium)
            throw new InvitationException(403, "AI skill analysis requires a Premium account.");
    }

    public async Task<GroupJoinRequest> RejectAsync(string user, string groupId, string id, int revision, CancellationToken ct)
    {
        var group = await Organizer(user, groupId, ct);
        var key = StateKey(group);
        var stored = await ReadState(key, ct);
        var request = Find(stored.Value, id);
        if (request.Status == "rejected")
        {
            await new PlayerMembershipService(_store).TrackAsync(group, request.ProfileId, ct);
            return request;
        }
        Pending(request, revision);
        request.Status = "rejected";
        request.ReviewedAt = _clock.UtcNow;
        request.ReviewedBy = user;
        request.Revision++;
        if (!await _store.WriteAsync(key, stored.Value, stored.ETag, ct)) throw Changed();
        await new PlayerMembershipService(_store).TrackAsync(group, request.ProfileId, ct);
        return request;
    }

    public async Task<GroupJoinRequest> ApproveAsync(string user, string groupId, string id, GroupJoinDecision input, CancellationToken ct)
    {
        var group = await Organizer(user, groupId, ct);
        if (!_allowRosterEnrollment)
            throw new InvitationException(409, "Final approval is disabled in this preview until the production backend supports the new roster format. Requests and AI review remain available.");
        var key = StateKey(group);
        var stored = await ReadState(key, ct);
        var request = Find(stored.Value, id);
        if (request.Status == "approved")
        {
            await new PlayerMembershipService(_store).RecordAsync(group, request, ct);
            await new PlayerMembershipService(_store).TrackAsync(group, request.ProfileId, ct);
            return request;
        }
        if (request.Status != "approving")
        {
            Pending(request, input.Revision);
            if (request.ProfileId != null
                && await new PlayerMembershipService(_store).ResolveProfileAsync(request.ProfileId, group, ct) != null)
                throw new InvitationException(409, "This account is already a member of this group.");
            ValidateRatings(input.Ratings, request.Skills);
            var currentSkills = JoinSkills(group.Configuration);
            if ((request.SubmittedAlgorithm ?? (int)AlgoType.SkillWise) != group.Configuration.SelectedAlgoKey
                || !SameSkills(request.Skills, currentSkills))
                throw SkillsChanged();
            ValidatePositions(request.Positions, group.Configuration.SelectedAlgoKey);
            await _roster.CheckAdditionAsync(group, request, ct);
            request.FinalRatings = input.Ratings.ToDictionary(rating => rating.Key,
                rating => Math.Round(rating.Value, MidpointRounding.AwayFromZero), StringComparer.Ordinal);
            request.ApprovedAlgorithm = group.Configuration.SelectedAlgoKey;
            request.Status = "approving";
            request.ReviewedAt = _clock.UtcNow;
            request.ReviewedBy = user;
            request.Revision++;
            if (!await _store.WriteAsync(key, stored.Value, stored.ETag, ct)) throw Changed();
        }
        group = await Organizer(user, groupId, ct);
        await _roster.AddAsync(group, request, ct);
        await new PlayerMembershipService(_store).RecordAsync(group, request, ct);
        for (var attempt = 0; attempt < 8; attempt++)
        {
            stored = await ReadState(key, ct);
            request = Find(stored.Value, id);
            if (request.Status == "approved")
            {
                await new PlayerMembershipService(_store).TrackAsync(group, request.ProfileId, ct);
                return request;
            }
            if (request.Status != "approving") throw Changed();
            request.Status = "approved";
            request.Revision++;
            if (await _store.WriteAsync(key, stored.Value, stored.ETag, ct))
            {
                await new PlayerMembershipService(_store).TrackAsync(group, request.ProfileId, ct);
                return request;
            }
        }
        throw Changed();
    }

    public static void ValidateRatings(Dictionary<string, double>? ratings, IReadOnlyList<SkillDefinition> skills)
    {
        if (ratings == null || ratings.Count != skills.Count || skills.Any(skill => !ratings.ContainsKey(skill.Id))
            || ratings.Any(rating => !double.IsFinite(rating.Value) || rating.Value < 1 || rating.Value > 10))
            throw new InvitationException(400, "Supply a rating from 1 to 10 for every configured skill, without extra fields.");
    }
    private static List<SkillDefinition> JoinSkills(UserConfigResponse config) => (AlgoType)config.SelectedAlgoKey switch
    {
        AlgoType.SkillWise or AlgoType.Positions => SkillDefinition.Normalize(config.SkillDefinitions),
        AlgoType.BackAndForth => new() { new SkillDefinition { Id = "rank", Name = config.Language == "he" ? "דירוג" : "Rank" } },
        AlgoType.Ai => new(),
        _ => throw new InvitationException(409, "This group's algorithm is not supported. Contact the organizer.")
    };
    private static bool SameSkills(IReadOnlyList<SkillDefinition> left, IReadOnlyList<SkillDefinition> right) =>
        left.Count == right.Count && left.Zip(right).All(pair => pair.First != null
            && pair.First.Id == pair.Second.Id && pair.First.Name?.Trim() == pair.Second.Name.Trim());
    private static void ValidatePositions(List<int>? positions, int algorithm)
    {
        if (algorithm == (int)AlgoType.Positions
            ? positions == null || positions.Count == 0 || positions.Any(position => position is < 0 or > 5)
                || positions.Distinct().Count() != positions.Count
            : positions is { Count: > 0 })
            throw new InvitationException(400, "Choose at least one valid position for the positions algorithm, and no positions for other algorithms.");
    }
    private static InvitationException SkillsChanged() => new(409,
        "Group skills or algorithm changed. Refresh the group link, or reject this request and ask the player to resubmit.");
    private async Task<StoredDocument<GroupJoinState>> ReadState(string key, CancellationToken ct) =>
        await _store.ReadAsync<GroupJoinState>(key, ct) ?? throw Unavailable();
    private static GroupJoinRequest Find(GroupJoinState state, string id) =>
        state.Requests.FirstOrDefault(request => request.Id == id) ?? throw new InvitationException(404, "Join request not found.");
    private static void Pending(GroupJoinRequest request, int revision)
    {
        if (request.Status != "pending" || request.Revision != revision) throw Changed();
    }
    private static InvitationException Unavailable() => new(404, "This group join link is unavailable.");
    private static InvitationException Changed() => new(409, "The request changed. Refresh before continuing.");
}
