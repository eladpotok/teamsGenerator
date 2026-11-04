using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TeamsGeneratorWebAPI.Clients;
using TeamsGeneratorWebAPI.Controllers;
using TeamsGeneratorWebAPI.Invitations;
using TeamsGeneratorWebAPI.PlayerProfiles;
using TeamsGeneratorWebAPI.Polls;

internal static class PlayerPerformanceRegression
{
    public static async Task Run(Action<bool, string> check)
    {
        await InvitationRegression.DocumentPagination(check);
        var ct = CancellationToken.None;
        var store = new InvitationRegression.MemoryStore();
        var source = new Source();
        var service = new PlayerPerformanceService(new(store), source);
        async Task<string> Link(string uid, string owner, string group, params string[] keys)
        {
            var path = $"player-memberships/v1/profiles/{InvitationKeys.Hash("player-profile:" + uid)}/"
                + InvitationKeys.Hash(JsonConvert.SerializeObject(new[] { owner, group }));
            await store.WriteAsync(path, new PlayerMembership(owner, group, "Same display name", keys.ToList()), null, ct);
            return path;
        }
        var path = await Link("me", "owner-a", "group", "mine", "old-mine");
        await Link("me", "owner-b", "group", "mine-b");
        await Link("other-account", "owner-other", "secret", "mine");
        var before = await store.ReadAsync<PlayerMembership>(path, ct);
        source.Days[("owner-a", "group")] = new()
        {
            Day("one", "mine", new JArray(Goal("mine"), Goal("other", "mine"),
                Goal("mine", "mine", own: true)), Poll("one", ("mine", 2, 0), ("other", 0, 2))),
            Day("two", "old-mine", new JArray(Goal("old-mine")),
                Poll("two", ("old-mine", 1, 0), ("other", 2, 0))),
            Day("not-mine", "unlinked-same-name", new JArray(Goal("unlinked-same-name")),
                Poll("not-mine", ("mine", 99, 0), ("other", 0, 99)))
        };
        source.Days[("owner-b", "group")] = new() { Day("no-poll", "mine-b", new JArray()) };
        var results = await service.GetAsync("me", ct);
        var a = results.Single(item => item.CompletedMatchdays == 2);
        check(a.Goals == 2 && a.Assists == 1, "modern goals do not double-count legacy scorers or own goals, and match explicit historical keys");
        check(a.AveragePollPosition == 1.5 && a.RankedMatchdays == 2 && a.MvpWins == 1 && a.FirstChoiceVotes == 3,
            "average poll position, wins and individual first-choice votes remain distinct");
        check(!a.HistoryIncomplete, "complete recorded history stays complete");
        var b = results.Single(item => item.CompletedMatchdays == 1);
        check(b.Goals == 0 && b.Assists == 0 && b.AveragePollPosition == null && b.RankedMatchdays == 0,
            "verified zero scoring differs from unavailable poll metrics");
        check(results.Select(item => item.GroupKey).Distinct().Count() == 2
            && source.Requests.SequenceEqual(new[] { ("owner-a", "group"), ("owner-b", "group") }),
            "same group IDs and names across owners remain isolated using server membership scope");
        check((await service.GetAsync("unlinked", ct)).Count == 0 && source.Requests.Count == 2,
            "unlinked accounts cannot enumerate group history");
        check(before!.ETag == (await store.ReadAsync<PlayerMembership>(path, ct))!.ETag,
            "group result GET never rewrites membership");

        source.Days[("owner-a", "group")] = new()
        {
            Day("tie", "mine", new JArray(), Poll("tie", ("mine", 1, 1), ("other", 1, 1))),
            Day("zero", "mine", new JArray(), Poll("zero", ("mine", 0, 0), ("other", 0, 0))),
            Day("open", "mine", new JArray(), Poll("open", ("mine", 10, 0), ("other", 0, 10)) with { Status = "open" })
        };
        a = (await service.GetAsync("me", ct)).Single(item => item.CompletedMatchdays == 3);
        check(a.RankedMatchdays == 1 && a.MvpWins == 1 && a.FirstChoiceVotes == 1 && a.AveragePollPosition == 1,
            "ties award a shared MVP; zero-ballot and still-open polls never contribute");

        var legacy = Day("legacy", "mine", new JArray());
        var json = JObject.Parse(legacy.Matches[0].SerializedMatch);
        json["teamA"]!["goals"]!.Parent!.Remove();
        json["teamA"]!["score"] = 2;
        json["teamA"]!["scorers"] = new JArray(new JObject { ["player"] = Player("mine") },
            new JObject { ["player"] = new JObject { ["name"] = "Same name" } });
        legacy.Matches[0].SerializedMatch = json.ToString();
        source.Days[("owner-a", "group")] = new() { legacy };
        a = (await service.GetAsync("me", ct)).Single(item => item.Goals == 1);
        check(a.HistoryIncomplete && a.Assists == 0, "legacy keyed goals count but missing keys/assists explicitly mark partial history");
        source.Days[("owner-a", "group")] = new()
        {
            new("broken", new[] { new MatchEntity { RowKey = "bad", SerializedMatch = "{}" } }, Array.Empty<PlayerSwapEntity>(), null)
        };
        a = (await service.GetAsync("me", ct)).Single(item => item.HistoryIncomplete);
        check(a.CompletedMatchdays == 0 && a.AveragePollPosition == null, "invalid legacy lineups are not guessed from names or current roster");
        source.Days[("owner-a", "group")] = new();
        a = (await service.GetAsync("me", ct)).Single(item => item.CompletedMatchdays == 0);
        check(!a.HistoryIncomplete && a.RankedMatchdays == 0, "a linked group with no history has an explicit empty result");

        var tables = new PollRegression.MemoryTables();
        var table = tables.Table("Matches");
        var now = DateTimeOffset.UtcNow;
        var poll = new PollEntity { PartitionKey = PollKeys.Partition("day0"), MatchdayKey = "day0",
            Closed = false, ClosesAt = now.AddMinutes(5),
            TalliesJson = JsonConvert.SerializeObject(new[] { new PollTally { Key = "mine", Name = "Renamed", FirstChoiceVotes = 1 } }),
            BallotCount = 1 };
        await tables.Table("MatchdayPolls").AddEntityAsync(poll);
        for (var i = 0; i < 103; i++)
        {
            var owner = i == 101 ? "different-owner" : "owner-a";
            var group = i == 102 ? "different-group" : "group";
            await table.AddEntityAsync(new MatchdayMetadataEntity { PartitionKey = "day" + i,
                RowKey = AzureTableStorageService.RowKeyForStartStatus, OwnerId = owner, GroupId = group });
            await table.AddEntityAsync(new MatchdayMetadataEntity { PartitionKey = "day" + i,
                RowKey = AzureTableStorageService.RowKeyForCloseStatus, IsClosed = true });
        }
        await table.AddEntityAsync(new MatchdayMetadataEntity { PartitionKey = "still-playing",
            RowKey = AzureTableStorageService.RowKeyForStartStatus, OwnerId = "owner-a", GroupId = "group" });
        var eventDay = Day("day0", "mine", new JArray(Goal("mine")));
        eventDay.Matches[0].PartitionKey = "day0";
        await table.AddEntityAsync(eventDay.Matches[0]);
        await table.AddEntityAsync(new PlayerSwapEntity { PartitionKey = "day0",
            RowKey = AzureTableStorageService.PlayerSwapRowKeyPrefix + "swap", SerializedSwap = "{}" });
        var realSource = new PlayerPerformanceSource(tables, TimeProvider.System);
        check(table.CreateCalls == 0 && tables.Table("MatchdayPolls").CreateCalls == 0,
            "profile source construction never provisions match or voting tables");
        table.ReadDelay = TimeSpan.FromMilliseconds(10);
        tables.Table("MatchdayPolls").ReadDelay = TimeSpan.FromMilliseconds(10);
        var projected = new List<PlayerPerformanceMatchday>();
        await foreach (var day in realSource.ReadAsync("owner-a", "group", ct)) projected.Add(day);
        check(table.PeakReads is > 1 and <= 8 && tables.Table("MatchdayPolls").PeakReads is > 1 and <= 8,
            "group history overlaps storage latency with at most eight matchdays in flight");
        table.ReadDelay = TimeSpan.Zero;
        tables.Table("MatchdayPolls").ReadDelay = TimeSpan.Zero;
        check(projected.Count == 101 && projected.All(day => day.Key != "still-playing" && day.Key is not ("day101" or "day102")),
            "source paginates all scoped matchdays while excluding other owners/groups and unfinished matchdays");
        check(table.QueryReads == 102 && table.QuerySelections.First().SequenceEqual(new[] { "PartitionKey" })
            && table.QuerySelections.Skip(1).All(selection => !selection.Contains("SerializedTeams")),
            "history uses one metadata projection and one event query per closed day, without retransmitting saved lineups");
        check(projected.Single(day => day.Key == "day0").Matches.Count == 1
            && projected.Single(day => day.Key == "day0").Swaps.Count == 1,
            "one event query retains both goals and player swaps without treating status rows as matches");
        check(projected.Single(day => day.Key == "day0").Poll == null, "open polls disclose no results in profile projections");
        poll.ClosesAt = now;
        var persisted = (await tables.Table("MatchdayPolls").GetEntityIfExistsAsync<PollEntity>(poll.PartitionKey, "poll")).Value;
        await tables.Table("MatchdayPolls").UpdateEntityAsync(poll, persisted.ETag);
        projected.Clear();
        await foreach (var day in realSource.ReadAsync("owner-a", "group", ct)) projected.Add(day);
        check(projected.Single(day => day.Key == "day0").Poll!.Results!.Single().Rank == 1
            && !(await tables.Table("MatchdayPolls").GetEntityIfExistsAsync<PollEntity>(poll.PartitionKey, "poll")).Value.Closed,
            "expired poll results are computed without mutating poll state");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        try
        {
            await foreach (var day in realSource.ReadAsync("owner-a", "group", cancelled.Token)) { }
            check(false, "cancelled profile reads must stop");
        }
        catch (OperationCanceledException) { check(true, "batched history reads preserve request cancellation"); }
        var method = typeof(PlayerProfileController).GetMethod("Groups")!;
        check(typeof(PlayerProfileController).IsDefined(typeof(AuthorizeAttribute), true)
            && !method.IsDefined(typeof(AllowAnonymousAttribute), true)
            && method.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { typeof(CancellationToken) })
            && method.GetCustomAttributes(typeof(HttpGetAttribute), true).Length == 1,
            "group results require registered principal identity, not caller-supplied account/group identifiers");
        source.Failure = true;
        try { await service.GetAsync("me", ct); check(false, "storage failure must propagate"); }
        catch (IOException) { check(true, "storage errors do not become a successful empty history"); }
    }

    private static JObject Player(string key) => new() { ["key"] = key, ["name"] = "Same name" };
    private static JObject Goal(string scorer, string? assist = null, bool own = false) => new()
        { ["scorer"] = Player(scorer), ["assist"] = assist == null ? null : Player(assist), ["isOwnGoal"] = own };
    private static PlayerPerformanceMatchday Day(string key, string player, JArray goals, PollSnapshot? poll = null)
    {
        var match = new JObject
        {
            ["teamA"] = new JObject { ["teamId"] = 1, ["players"] = new JArray(Player(player)), ["score"] = goals.Count,
                ["goals"] = goals, ["scorers"] = new JArray(new JObject { ["player"] = Player(player) }) },
            ["teamB"] = new JObject { ["teamId"] = 2, ["players"] = new JArray(Player("other")), ["score"] = 0, ["goals"] = new JArray() }
        };
        return new(key, new[] { new MatchEntity { RowKey = "match", SerializedMatch = match.ToString() } },
            Array.Empty<PlayerSwapEntity>(), poll);
    }
    private static PollSnapshot Poll(string day, params (string Key, long First, long Second)[] votes) =>
        TablePollStore.Snapshot(new PollEntity { Closed = true, MatchdayKey = day, BallotCount = votes.Sum(v => v.First),
            TalliesJson = JsonConvert.SerializeObject(votes.Select(v => new PollTally
                { Key = v.Key, Name = "Same name", FirstChoiceVotes = v.First, SecondChoiceVotes = v.Second })) }, false);

    private sealed class Source : IPlayerPerformanceSource
    {
        public Dictionary<(string Owner, string Group), List<PlayerPerformanceMatchday>> Days { get; } = new();
        public List<(string Owner, string Group)> Requests { get; } = new();
        public bool Failure { get; set; }
        public async IAsyncEnumerable<PlayerPerformanceMatchday> ReadAsync(string ownerId, string groupId,
            [EnumeratorCancellation] CancellationToken ct)
        {
            await Task.Yield();
            ct.ThrowIfCancellationRequested();
            if (Failure) throw new IOException("Synthetic storage failure");
            Requests.Add((ownerId, groupId));
            foreach (var day in Days[(ownerId, groupId)]) yield return day;
        }
    }
}
