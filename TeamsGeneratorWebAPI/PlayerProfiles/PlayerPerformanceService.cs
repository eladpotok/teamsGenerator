using System.Runtime.CompilerServices;
using Azure.Data.Tables;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TeamsGeneratorWebAPI.Clients;
using TeamsGeneratorWebAPI.Invitations;
using TeamsGeneratorWebAPI.Polls;

namespace TeamsGeneratorWebAPI.PlayerProfiles;

public sealed record PlayerGroupPerformance(string GroupKey, string GroupName, int CompletedMatchdays,
    int Goals, int Assists, int RankedMatchdays, double? AveragePollPosition, int MvpWins,
    long FirstChoiceVotes, bool HistoryIncomplete);

public sealed record PlayerPerformanceMatchday(string Key, IReadOnlyList<MatchEntity> Matches,
    IReadOnlyList<PlayerSwapEntity> Swaps, PollSnapshot? Poll);

public interface IPlayerPerformanceSource
{
    IAsyncEnumerable<PlayerPerformanceMatchday> ReadAsync(string ownerId, string groupId, CancellationToken ct);
}

public sealed class PlayerPerformanceSource(TableServiceClient tables, TimeProvider clock)
    : IPlayerPerformanceSource
{
    // Profile reads must not provision tables or block account loading on unrelated storage.
    private readonly AzureTableStorageService matches = new(tables.GetTableClient("Matches"));
    private readonly TablePollStore polls = new(tables.GetTableClient("MatchdayPolls"), clock);

    public async IAsyncEnumerable<PlayerPerformanceMatchday> ReadAsync(string ownerId, string groupId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var batch = new List<string>(8);
        await foreach (var day in matches.GetGroupMatchdaysAsync(ownerId, groupId, ct))
        {
            batch.Add(day.PartitionKey);
            if (batch.Count < 8) continue;
            foreach (var result in await Task.WhenAll(batch.Select(key => ReadDayAsync(key, ct))))
                if (result != null) yield return result;
            batch.Clear();
        }
        foreach (var result in await Task.WhenAll(batch.Select(key => ReadDayAsync(key, ct))))
            if (result != null) yield return result;
    }

    private async Task<PlayerPerformanceMatchday?> ReadDayAsync(string key, CancellationToken ct)
    {
        if (!await matches.IsClosed(key, ct)) return null;
        var poll = polls.FindAsync(key, ct);
        var events = matches.GetMatchdayEventsAsync(key, ct);
        await Task.WhenAll(poll, events);
        var history = await events;
        return new(key, history.Matches, history.Swaps, ClosedResult(await poll, clock.GetUtcNow()));
    }

    internal static PollSnapshot? ClosedResult(PollEntity? poll, DateTimeOffset now)
    {
        if (poll == null || (!poll.Closed && poll.ClosesAt > now)) return null;
        // Expired polls can be projected as closed without mutating their persisted state on a profile GET.
        return TablePollStore.Snapshot(new PollEntity { Token = poll.Token, MatchdayKey = poll.MatchdayKey,
            Closed = true, ClosesAt = poll.ClosesAt, BallotCount = poll.BallotCount, TalliesJson = poll.TalliesJson }, false);
    }
}

public sealed class PlayerPerformanceService(PlayerMembershipService memberships, IPlayerPerformanceSource source)
{
    public async Task<IReadOnlyList<PlayerGroupPerformance>> GetAsync(string uid, CancellationToken ct)
    {
        var results = new List<PlayerGroupPerformance>();
        foreach (var group in (await memberships.ListAsync(uid, ct)).Where(group => group.PlayerKeys.Count > 0))
        {
            var keys = group.PlayerKeys.ToHashSet(StringComparer.Ordinal);
            var days = 0;
            var goals = 0;
            var assists = 0;
            var ranks = new List<int>();
            var wins = 0;
            long votes = 0;
            var incomplete = false;
            await foreach (var day in source.ReadAsync(group.OwnerId, group.GroupId, ct))
            {
                if (day.Matches.Count == 0) continue;
                IReadOnlyList<PollCandidate> participants;
                try { participants = PollParticipants.Extract(day.Matches, day.Swaps); }
                catch (PollException ex) when (ex.ErrorCode == "participant_identity_unavailable")
                {
                    incomplete = true;
                    continue;
                }
                if (!participants.Any(player => keys.Contains(player.Key))) continue;
                days++;
                foreach (var match in day.Matches)
                {
                    var json = JObject.Parse(match.SerializedMatch);
                    foreach (var side in new[] { "teamA", "teamB" })
                    {
                        var team = (JObject)json.GetValue(side, StringComparison.OrdinalIgnoreCase)!;
                        var score = CountGoals(team, keys);
                        goals += score.Goals;
                        assists += score.Assists;
                        incomplete |= score.Incomplete;
                    }
                }
                var poll = day.Poll;
                if (poll == null || poll.Status != "closed" || poll.BallotCount is null or 0) continue;
                var own = poll.Results?.Where(result => keys.Contains(result.Key)).ToArray();
                if (poll.MatchdayKey != day.Key || own?.Length != 1)
                {
                    incomplete = true;
                    continue;
                }
                ranks.Add(own[0].Rank);
                votes += own[0].FirstChoiceVotes;
                if (own[0].Rank == 1 && own[0].Points > 0) wins++;
            }
            results.Add(new(InvitationKeys.Hash(JsonConvert.SerializeObject(new[] { group.OwnerId, group.GroupId })),
                group.GroupName, days, goals, assists, ranks.Count, ranks.Count == 0 ? null : ranks.Average(),
                wins, votes, incomplete));
        }
        return results.OrderBy(group => group.GroupName, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static (int Goals, int Assists, bool Incomplete) CountGoals(JObject team, HashSet<string> keys)
    {
        var modern = team.GetValue("goals", StringComparison.OrdinalIgnoreCase);
        var legacy = modern == null || modern.Type == JTokenType.Null;
        var entries = (legacy ? team.GetValue("scorers", StringComparison.OrdinalIgnoreCase) : modern) as JArray;
        if (entries == null) return (0, 0, true);
        var goals = 0;
        var assists = 0;
        var incomplete = legacy && entries.Count > 0;
        var score = team.GetValue("score", StringComparison.OrdinalIgnoreCase);
        if (score?.Type == JTokenType.Integer && score.Value<int>() != entries.Count) incomplete = true;
        foreach (var entry in entries)
        {
            if (entry is not JObject item) { incomplete = true; continue; }
            var scorer = item.GetValue(legacy ? "player" : "scorer", StringComparison.OrdinalIgnoreCase) as JObject;
            if (IsTrue(item, "isOwnGoal") || (scorer != null && IsTrue(scorer, "ownGoal"))) continue;
            var scorerKey = PlayerKey(scorer);
            if (scorerKey == null) incomplete = true;
            else if (keys.Contains(scorerKey)) goals++;
            if (legacy) continue;
            var assist = item.GetValue("assist", StringComparison.OrdinalIgnoreCase);
            if (assist == null) { incomplete = true; continue; }
            if (assist.Type == JTokenType.Null) continue;
            var assistKey = PlayerKey(assist as JObject);
            if (assistKey == null) incomplete = true;
            else if (keys.Contains(assistKey)) assists++;
        }
        return (goals, assists, incomplete);
    }

    private static string? PlayerKey(JObject? player)
    {
        var key = player?.GetValue("key", StringComparison.OrdinalIgnoreCase)
            ?? player?.GetValue("id", StringComparison.OrdinalIgnoreCase);
        return key?.Type == JTokenType.String && !string.IsNullOrWhiteSpace(key.Value<string>()) ? key.Value<string>() : null;
    }

    private static bool IsTrue(JObject value, string property) =>
        value.GetValue(property, StringComparison.OrdinalIgnoreCase) is { Type: JTokenType.Boolean } token && token.Value<bool>();
}
