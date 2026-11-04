using System.Security.Claims;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TeamsGeneratorWebAPI.Authentication;
using TeamsGeneratorWebAPI.Clients;
using TeamsGeneratorWebAPI.Collaboration;

namespace TeamsGeneratorWebAPI.Polls;

public sealed class PollService(AzureTableStorageService matches, IGroupCollaborationService collaboration, TablePollStore store)
{
    public async Task<PollSnapshot> SaveEventAsync(string matchdayKey, ClaimsPrincipal user, PollEventDetails? details, CancellationToken ct)
    {
        var snapshot = await OrganizerAsync(matchdayKey, user, false, false, ct);
        if (details == null) throw new PollException(400, "invalid_event_details", "Match event details are required.");
        await store.SaveEventAsync(snapshot.Token, details, ct);
        return await OrganizerAsync(matchdayKey, user, false, false, ct);
    }

    public async Task<PollSnapshot> OrganizerAsync(string matchdayKey, ClaimsPrincipal user, bool create, bool close, CancellationToken ct)
    {
        var uid = RequestUserId.ResolveOptional(user, null);
        if (user.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(uid))
            throw new PollException(401, "authentication_required", "Sign in as a matchday organizer.");
        if (string.IsNullOrWhiteSpace(matchdayKey) || matchdayKey.Length > 256
            || matchdayKey.Any(c => char.IsControl(c) || c is '/' or '\\' or '#' or '?'))
            throw new PollException(400, "invalid_matchday", "A valid matchday key is required.");
        var identity = await matches.GetMatchdayIdentity(matchdayKey);
        if (identity == null)
            throw new PollException(404, "matchday_not_found", "The matchday was not found.");
        if (string.IsNullOrWhiteSpace(identity.OwnerId) || string.IsNullOrWhiteSpace(identity.GroupId))
            throw new PollException(400, "matchday_identity_unavailable",
                "This legacy matchday has no verified organizer identity and cannot have a poll.");
        if (uid != identity.OwnerId)
        {
            var access = await collaboration.ResolveAccessAsync(uid,
                GroupCollaborationService.CreateSharedGroupId(identity.OwnerId, identity.GroupId));
            if (access == null || access.OwnerId != identity.OwnerId || access.GroupId != identity.GroupId)
                throw new PollException(403, "organizer_required", "Only this matchday's owner or co-organizers can manage its poll.");
        }
        var poll = await store.FindAsync(matchdayKey, ct);
        if (poll == null && create)
        {
            if (!await matches.IsClosed(matchdayKey))
                throw new PollException(409, "matchday_not_finished", "Finish the matchday before opening its poll.");
            var candidates = PollParticipants.Extract(await matches.GetAllMatchesAsync(matchdayKey),
                await matches.GetAllPlayerSwapsAsync(matchdayKey));
            poll = await store.CreateAsync(matchdayKey, candidates, ct);
        }
        if (poll == null) throw new PollException(404, "poll_not_found", "There is no poll for this matchday.");
        var snapshot = await store.ReadAsync(poll.Token, null, close, ct);
        // The persisted matchday start is independent of poll creation/closure and next-event settings.
        return snapshot with
        {
            EventStartedAt = identity.CreatedAt > DateTime.UnixEpoch
                ? new DateTimeOffset(DateTime.SpecifyKind(identity.CreatedAt, DateTimeKind.Utc)) : null
        };
    }
}

public static class PollParticipants
{
    public static IReadOnlyList<PollCandidate> Extract(IReadOnlyList<MatchEntity> matches, IReadOnlyList<PlayerSwapEntity> swaps)
    {
        var candidates = new Dictionary<string, PollCandidate>(StringComparer.OrdinalIgnoreCase);
        var recorded = new Dictionary<string, JObject>(StringComparer.Ordinal);
        try
        {
            foreach (var match in matches)
            {
                if (string.IsNullOrWhiteSpace(match.RowKey) || string.IsNullOrWhiteSpace(match.SerializedMatch)) throw Unavailable();
                var json = JObject.Parse(match.SerializedMatch);
                recorded.Add(match.RowKey, json);
                foreach (var teamName in new[] { "teamA", "teamB" })
                {
                    var team = Value(json, teamName) as JObject;
                    if (team == null || Value(team, "players") is not JArray players || players.Count == 0)
                        throw Unavailable();
                    foreach (var player in players) Add(player, candidates);
                }
            }
            // Substitution formats without explicit player identities cannot be reconstructed
            // from a current roster: doing so could nominate players who never participated.
            foreach (var swap in swaps)
            {
                if (string.IsNullOrWhiteSpace(swap.SerializedSwap)) throw Unavailable();
                AddSwap(JObject.Parse(swap.SerializedSwap), recorded, candidates);
            }
        }
        catch (JsonException) { throw Unavailable(); }
        if (candidates.Count < 2) throw Unavailable();
        return candidates.Values.OrderBy(c => c.Key, StringComparer.Ordinal).ToArray();
    }

    private static void AddSwap(JObject swap, IReadOnlyDictionary<string, JObject> matches,
        IDictionary<string, PollCandidate> candidates)
    {
        var players = new Dictionary<string, PollCandidate>(StringComparer.OrdinalIgnoreCase);
        Add(Value(swap, "playerA") ?? throw Unavailable(), players);
        Add(Value(swap, "playerB") ?? throw Unavailable(), players);
        var matchKeyToken = Value(swap, "activeMatchKey");
        if (matchKeyToken != null && matchKeyToken.Type is not (JTokenType.String or JTokenType.Null)) throw Unavailable();
        var matchKey = matchKeyToken?.Value<string>();
        if (string.IsNullOrWhiteSpace(matchKey))
        {
            if (players.Keys.All(candidates.ContainsKey)) return;
            throw Unavailable();
        }
        // A draft/deleted game or a swap between two resting teams is not participation.
        if (!matches.TryGetValue(matchKey, out var match)) return;
        var from = TeamId(swap, "fromTeamId");
        var to = TeamId(swap, "toTeamId");
        var playing = new[] { "teamA", "teamB" }.Select(side =>
            TeamId((JObject)Value(match, side)!, "teamId")).ToArray();
        if (!playing.Contains(from) && !playing.Contains(to)) return;
        foreach (var player in players.Values)
            if (!candidates.ContainsKey(player.Key)) candidates.Add(player.Key, player);
    }

    private static string TeamId(JObject source, string name)
    {
        var value = Value(source, name);
        if (value?.Type is not (JTokenType.String or JTokenType.Integer)
            || string.IsNullOrWhiteSpace(value.ToString())) throw Unavailable();
        return value.ToString();
    }

    private static void Add(JToken token, IDictionary<string, PollCandidate> candidates)
    {
        if (token is not JObject player) throw Unavailable();
        var keyToken = Value(player, "key") ?? Value(player, "id");
        var nameToken = Value(player, "name");
        if (keyToken?.Type != JTokenType.String || nameToken?.Type != JTokenType.String) throw Unavailable();
        var key = keyToken.Value<string>()!.Trim();
        var name = new string(nameToken.Value<string>()!.Where(c => !char.IsControl(c) && c is not '<' and not '>').ToArray()).Trim();
        if (key.Length is 0 or > 200 || key.Any(char.IsControl) || name.Length is 0 or > 100) throw Unavailable();
        if (!candidates.ContainsKey(key)) candidates.Add(key, new(key, name));
    }

    private static JToken? Value(JObject source, string name) => source.GetValue(name, StringComparison.OrdinalIgnoreCase);
    private static PollException Unavailable() => new(400, "participant_identity_unavailable",
        "This matchday's recorded lineups do not contain sufficient participant identities for a poll.");
}
