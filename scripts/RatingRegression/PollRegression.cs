using System.Linq.Expressions;
using System.Security.Claims;
using Azure;
using Azure.Core;
using Azure.Data.Tables;
using Azure.Data.Tables.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TeamsGeneratorWebAPI.Clients;
using TeamsGeneratorWebAPI.Collaboration;
using TeamsGeneratorWebAPI.Controllers;
using TeamsGeneratorWebAPI.Polls;

internal static class PollRegression
{
    internal static async Task Run(Action<bool, string> check)
    {
        var tables = new MemoryTables();
        var clock = new Clock();
        var a = new TablePollStore(tables, clock);
        var b = new TablePollStore(tables, clock);
        var table = tables.Table("MatchdayPolls");
        var candidates = new[] { new PollCandidate("a", "A"), new PollCandidate("b", "B"), new PollCandidate("c", "C") };
        var poll = await a.CreateAsync("day", candidates, default);
        var duplicateCreate = await b.CreateAsync("day", new[] { new PollCandidate("x", "X"), new PollCandidate("y", "Y") }, default);
        check(poll.Token == duplicateCreate.Token && poll.ClosesAt == clock.Now.AddHours(48), "create is idempotent with a 48h expiry");
        check(poll.Token != "day" && poll.Token.Length == 129, "public link includes an unpredictable secret rather than the matchday key");
        var voter = await a.VoterAsync(null, default);
        check(await b.VoterAsync(voter, default) == voter, "durable server-issued voter identity survives service reconstruction");
        await Expect(() => b.VoterAsync(PollKeys.NewSecret(), default), 401, "invalid_voter", check);
        await Expect(() => a.ReadAsync(poll.Token[..65] + PollKeys.NewSecret(), null, false, default), 404, "poll_not_found", check);
        var open = await a.ReadAsync(poll.Token, voter, false, default);
        check(open.Results == null && open.BallotCount == null && !open.HasVoted, "open read hides aggregate counts and results");
        await Expect(() => a.VoteAsync(poll.Token, voter, new("a", "a"), default), 400, "invalid_ballot", check);
        await Expect(() => a.VoteAsync(poll.Token, voter, new("a", "outsider"), default), 400, "invalid_ballot", check);
        await Expect(() => a.VoteAsync(poll.Token, "forged", new("a", "b"), default), 401, "invalid_voter", check);

        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        table.BeforeTransaction = async () => { table.BeforeTransaction = null; reached.SetResult(); await release.Task; };
        var firstVote = a.VoteAsync(poll.Token, voter, new("a", "b"), default);
        await reached.Task;
        var winningVote = await b.VoteAsync(poll.Token, voter, new("b", "c"), default);
        release.SetResult();
        await Expect(() => firstVote, 409, "already_voted", check);
        check(winningVote.HasVoted && winningVote.Results == null, "successful ballot snapshot hides results and acknowledges browser");
        await Expect(() => a.VoteAsync(poll.Token, voter, new("a", "b"), default), 409, "already_voted", check);

        var other = await a.VoterAsync(null, default);
        table.BeforeTransaction = async () =>
        {
            table.BeforeTransaction = null;
            await b.ReadAsync(poll.Token, null, true, default);
        };
        await Expect(() => a.VoteAsync(poll.Token, other, new("a", "b"), default), 409, "poll_closed", check);
        var closed = await b.ReadAsync(poll.Token, voter, false, default);
        check(closed.Status == "closed" && closed.BallotCount == 1
            && closed.Results![0].Key == "b" && closed.Results[0].Points == 5
            && closed.Results[1].Key == "c" && closed.Results[1].Points == 3,
            "close defeats in-flight ballot without partial writes or duplicate increments");
        check((await a.CreateAsync("day", candidates, default)).Closed, "create cannot reset a closed poll");
        check((await b.ReadAsync(poll.Token, null, true, default)).BallotCount == 1, "close is idempotent");

        var independent = await a.CreateAsync("independent-voters", candidates, default);
        var eventDetails = new PollEventDetails("Friday football", "North pitch",
            new DateTimeOffset(2026, 9, 18, 17, 30, 0, TimeSpan.Zero), "Asia/Jerusalem", "en");
        table.BeforeTransaction = async () =>
        {
            table.BeforeTransaction = null;
            await b.SaveEventAsync(independent.Token, eventDetails, default);
            await b.VoteAsync(independent.Token, other, new("c", "b"), default);
        };
        var afterEventVote = await a.VoteAsync(independent.Token, voter, new("a", "b"), default);
        check(afterEventVote.EventDetails == eventDetails, "in-flight ballot retries retain concurrently saved event details");
        var independentResult = await a.ReadAsync(independent.Token, null, true, default);
        await b.SaveEventAsync(independent.Token, eventDetails with { MatchName = "Next week", ScheduledAt = eventDetails.ScheduledAt.AddDays(7) }, default);
        var reshared = await b.ReadAsync(independent.Token, null, false, default);
        check(reshared.EventDetails == eventDetails && reshared.ClosesAt == independent.ClosesAt
            && reshared.Status == "closed" && reshared.BallotCount == 2, "event snapshot is write-once across read, vote, close and re-share");
        check(independentResult.BallotCount == 2 && independentResult.Results!.Single(c => c.Key == "b").Points == 6,
            "distinct concurrent browsers preserve both ballots through aggregate ETag contention");

        var racing = await a.CreateAsync("voting-wins", candidates, default);
        table.BeforeUpdate = async () =>
        {
            table.BeforeUpdate = null;
            await a.VoteAsync(racing.Token, other, new("a", "b"), default);
        };
        check((await b.ReadAsync(racing.Token, null, true, default)).BallotCount == 1,
            "close retries its stale ETag and preserves the ballot that committed first");
        var expiry = await a.CreateAsync("expiry", candidates, default);
        clock.Now = expiry.ClosesAt;
        await Expect(() => a.VoteAsync(expiry.Token, other, new("a", "b"), default), 409, "poll_closed", check);
        check((await b.FindAsync("expiry", default))!.Closed, "automatic expiry persists without a background timer");
        var readExpiry = await a.CreateAsync("read-expiry", candidates, default);
        clock.Now = readExpiry.ClosesAt.AddSeconds(1);
        check((await a.ReadAsync(readExpiry.Token, null, false, default)).Status == "closed"
            && (await b.FindAsync("read-expiry", default))!.Closed, "a read alone materializes automatic closure and reveals final results");
        var tie = TablePollStore.Snapshot(new PollEntity { Closed = true, TalliesJson = JsonConvert.SerializeObject(new[]
        {
            new PollTally { Key = "a", Name = "A", FirstChoiceVotes = 3 },
            new PollTally { Key = "b", Name = "B", SecondChoiceVotes = 5 },
            new PollTally { Key = "c", Name = "C", FirstChoiceVotes = 3 }
        }) }, false);
        check(tie.Results!.Select(c => c.Key).SequenceEqual(new[] { "a", "c", "b" })
            && tie.Results.Select(c => c.Rank).SequenceEqual(new[] { 1, 1, 3 }), "points, first-choice tiebreak and shared competition placing");

        await CheckParticipantsAndAuthorization(tables, a, check);
        await CheckHttp(tables, clock, check);
    }

    private static async Task CheckParticipantsAndAuthorization(MemoryTables tables, TablePollStore store, Action<bool, string> check)
    {
        var match = new MatchEntity { PartitionKey = "auth-day", RowKey = "game", SerializedMatch =
            """{"teamA":{"teamId":1,"players":[{"key":"a","name":"<A>","attack":9}]},"teamB":{"teamId":2,"players":[{"key":"b","name":"B","rank":8}]}}""" };
        var swap = new PlayerSwapEntity { SerializedSwap =
            """{"activeMatchKey":"game","fromTeamId":1,"toTeamId":3,"playerA":{"key":"a","name":"A"},"playerB":{"key":"c","name":"C","attack":1}}""" };
        var extracted = PollParticipants.Extract(new[] { match }, new[] { swap });
        check(extracted.Count == 3 && extracted[0].Name == "A"
            && !JsonConvert.SerializeObject(extracted).Contains("attack"), "snapshot includes substitutes and sanitized key/name only");
        var benchSwap = new PlayerSwapEntity { SerializedSwap =
            """{"activeMatchKey":"game","fromTeamId":3,"toTeamId":4,"playerA":{"key":"d","name":"D"},"playerB":{"key":"e","name":"E"}}""" };
        var draftSwap = new PlayerSwapEntity { SerializedSwap = swap.SerializedSwap.Replace("\"game\"", "\"unsaved-draft\"") };
        check(PollParticipants.Extract(new[] { match }, new[] { benchSwap, draftSwap }).Count == 2,
            "resting-team swaps and unrecorded draft games cannot nominate nonparticipants");
        await Expect(() => Task.FromResult(PollParticipants.Extract(new[] { new MatchEntity { SerializedMatch =
            """{"teamA":{"players":["A"]},"teamB":{"players":["B"]}}""" } }, Array.Empty<PlayerSwapEntity>())),
            400, "participant_identity_unavailable", check);
        await Expect(() => Task.FromResult(PollParticipants.Extract(new[] { match }, new[] { new PlayerSwapEntity { SerializedSwap = "{}" } })),
            400, "participant_identity_unavailable", check);
        var matches = tables.Table("Matches");
        var eventStartedAt = new DateTime(2026, 9, 18, 17, 30, 0, DateTimeKind.Utc);
        await matches.AddEntityAsync(new MatchdayMetadataEntity { PartitionKey = "auth-day",
            RowKey = AzureTableStorageService.RowKeyForStartStatus, OwnerId = "owner", GroupId = "group", CreatedAt = eventStartedAt });
        await matches.AddEntityAsync(new MatchdayMetadataEntity { PartitionKey = "auth-day",
            RowKey = AzureTableStorageService.RowKeyForCloseStatus, IsClosed = false });
        await matches.AddEntityAsync(match);
        var collaboration = CallbackProxy.Create<IGroupCollaborationService>((name, args) =>
        {
            check(name.Name == "ResolveAccessAsync"
                && (string)args![1]! == GroupCollaborationService.CreateSharedGroupId("owner", "group"),
                "co-organizer authorization uses persisted owner/group, never a request-supplied UID");
            return Task.FromResult<GroupAccess?>((string)args![0]! == "co"
                ? new GroupAccess { OwnerId = "owner", GroupId = "group" } : null);
        });
        var service = new PollService(new AzureTableStorageService(tables), collaboration, store);
        static ClaimsPrincipal User(string uid) => new(new ClaimsIdentity(new[] { new Claim("user_id", uid) }, "test"));
        await Expect(() => service.OrganizerAsync("auth-day", new ClaimsPrincipal(), true, false, default), 401, "authentication_required", check);
        await Expect(() => service.OrganizerAsync("auth-day", User("outsider"), true, false, default), 403, "organizer_required", check);
        await Expect(() => service.OrganizerAsync("auth-day", User("owner"), true, false, default), 409, "matchday_not_finished", check);
        var status = (await matches.GetEntityAsync<MatchdayMetadataEntity>("auth-day", AzureTableStorageService.RowKeyForCloseStatus)).Value;
        status.IsClosed = true;
        await matches.UpdateEntityAsync(status, status.ETag);
        await Expect(() => service.OrganizerAsync("auth-day", User("owner"), false, false, default), 404, "poll_not_found", check);
        var created = await service.OrganizerAsync("auth-day", User("co"), true, false, default);
        check(created.Candidates.Count == 2, "only recorded players, not whole saved group, become candidates");
        check(created.EventStartedAt == new DateTimeOffset(eventStartedAt), "organizer poll uses persisted matchday start, not poll creation time");
        var details = new PollEventDetails("Football", "Pitch", new DateTimeOffset(eventStartedAt).AddHours(-1), "UTC", "en");
        await Expect(() => service.SaveEventAsync("auth-day", new ClaimsPrincipal(), details, default), 401, "authentication_required", check);
        await Expect(() => service.SaveEventAsync("auth-day", User("outsider"), details, default), 403, "organizer_required", check);
        await Expect(() => service.SaveEventAsync("auth-day", User("owner"), null, default), 400, "invalid_event_details", check);
        foreach (var invalid in new[] { details with { MatchName = new string('a', 201) }, details with { TimeZone = "not-a-zone" },
            details with { ScheduledAt = default }, details with { Language = "invalid" }, details with { Location = "a\nb" } })
            await Expect(() => service.SaveEventAsync("auth-day", User("owner"), invalid, default), 400, "invalid_event_details", check);
        check((await service.SaveEventAsync("auth-day", User("co"), details, default)).EventDetails == details,
            "co-organizer can enrich an existing poll with allowlisted event metadata");
        check((await store.ReadAsync(created.Token, null, false, default)).EventDetails == details,
            "anonymous public poll reads persisted event details without organizer identity or settings");
        check((await service.OrganizerAsync("auth-day", User("owner"), false, true, default)).Status == "closed", "owner can close co-organizer's poll");
        check((await service.OrganizerAsync("auth-day", User("owner"), true, false, default)).EventStartedAt == created.EventStartedAt,
            "existing closed polls retain the original event time on read and reshare without a poll migration");
        await matches.AddEntityAsync(new MatchdayMetadataEntity { PartitionKey = "legacy", RowKey = AzureTableStorageService.RowKeyForStartStatus });
        await Expect(() => service.OrganizerAsync("legacy", User("owner"), true, false, default), 400, "matchday_identity_unavailable", check);
    }

    private static async Task CheckHttp(MemoryTables tables, Clock clock, Action<bool, string> check)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddControllers().AddApplicationPart(typeof(PollsController).Assembly).AddNewtonsoftJson();
        builder.Services.AddAuthentication("PollTest").AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("PollTest", _ => { });
        builder.Services.AddCors(options => options.AddPolicy("Frontend", policy =>
            policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod().WithExposedHeaders("Retry-After")));
        builder.Services.AddSingleton<TableServiceClient>(tables);
        builder.Services.AddSingleton<TimeProvider>(clock);
        builder.Services.AddSingleton<TablePollStore>();
        builder.Services.AddSingleton<AzureTableStorageService>();
        builder.Services.AddSingleton(CallbackProxy.Create<IGroupCollaborationService>((_, _) => Task.FromResult<GroupAccess?>(null)));
        builder.Services.AddSingleton<PollService>();
        builder.Services.AddSingleton<PollPublicRateLimiter>();
        builder.Services.AddScoped<PollExceptionFilter>();
        await using var app = builder.Build();
        app.UseCors("Frontend");
        app.UseAuthentication();
        app.MapControllers();
        await app.StartAsync();
        using var http = new HttpClient { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
        var store = app.Services.GetRequiredService<TablePollStore>();
        var poll = await store.CreateAsync("http-day", new[] { new PollCandidate("a", "A"), new PollCandidate("b", "B") }, default);
        foreach (var origin in new[] { "https://app.teamify.test", "capacitor://localhost" })
        {
            using var preflight = new HttpRequestMessage(HttpMethod.Options, "/api/polls/" + poll.Token + "/ballots");
            preflight.Headers.Add("Origin", origin);
            preflight.Headers.Add("Access-Control-Request-Method", "POST");
            preflight.Headers.Add("Access-Control-Request-Headers", "content-type,x-teamify-voter,authorization");
            using var response = await http.SendAsync(preflight);
            check(response.IsSuccessStatusCode
                && response.Headers.GetValues("Access-Control-Allow-Headers").Any(h => h.Contains("x-teamify-voter")),
                "cross-origin web/native preflight permits the anonymous voter credential header");
        }
        var issuance = await http.PostAsync("/api/polls/voter", null);
        var voter = JObject.Parse(await issuance.Content.ReadAsStringAsync())["voterToken"]!.Value<string>()!;
        var read = await http.GetAsync("/api/polls/" + poll.Token);
        var json = JObject.Parse(await read.Content.ReadAsStringAsync());
        check(read.IsSuccessStatusCode && json["token"]!.Value<string>() == poll.Token && json["status"]!.Value<string>() == "open"
            && json["results"] == null && json["ballotCount"] == null && json["candidates"]![0]!["key"] != null,
            "real MVC wire contract is camelCase and omits all open results and counts");
        check(read.Headers.CacheControl?.NoStore == true, "poll responses prohibit caching across voters");
        http.DefaultRequestHeaders.Add("X-Teamify-Voter", voter);
        var vote = await http.PostAsync("/api/polls/" + poll.Token + "/ballots",
            new StringContent("""{"firstChoiceKey":"a","secondChoiceKey":"b"}""", System.Text.Encoding.UTF8, "application/json"));
        check(vote.IsSuccessStatusCode && JObject.Parse(await vote.Content.ReadAsStringAsync())["hasVoted"]!.Value<bool>(),
            "public ballot endpoint accepts opaque voter header and returns acknowledged snapshot");
        var twice = await http.PostAsync("/api/polls/" + poll.Token + "/ballots",
            new StringContent("""{"firstChoiceKey":"a","secondChoiceKey":"b"}""", System.Text.Encoding.UTF8, "application/json"));
        check((int)twice.StatusCode == 409 && JObject.Parse(await twice.Content.ReadAsStringAsync())["errorCode"]!.Value<string>() == "already_voted",
            "duplicate HTTP ballot returns structured 409 already_voted");
        var malformed = await http.PostAsync("/api/polls/" + poll.Token + "/ballots",
            new StringContent("{", System.Text.Encoding.UTF8, "application/json"));
        check((int)malformed.StatusCode == 400 && JObject.Parse(await malformed.Content.ReadAsStringAsync())["errorCode"] != null,
            "invalid JSON returns a structured error");
        var auth = await http.PostAsync("/api/matchday-polls/http-day", null);
        check((int)auth.StatusCode == 401 && JObject.Parse(await auth.Content.ReadAsStringAsync())["errorCode"]!.Value<string>() == "authentication_required",
            "organizer endpoint rejects anonymous access with structured 401");
        var matchTable = tables.Table("Matches");
        await matchTable.AddEntityAsync(new MatchdayMetadataEntity { PartitionKey = "http-organizer",
            RowKey = AzureTableStorageService.RowKeyForStartStatus, OwnerId = "owner", GroupId = "group",
            CreatedAt = new DateTime(2026, 9, 18, 17, 30, 0, DateTimeKind.Utc) });
        await matchTable.AddEntityAsync(new MatchdayMetadataEntity { PartitionKey = "http-organizer",
            RowKey = AzureTableStorageService.RowKeyForCloseStatus, IsClosed = true });
        await matchTable.AddEntityAsync(new MatchEntity { PartitionKey = "http-organizer", RowKey = "match",
            SerializedMatch = """{"teamA":{"players":[{"key":"a","name":"A"}]},"teamB":{"players":[{"key":"b","name":"B"}]}}""" });
        http.DefaultRequestHeaders.Authorization = new("Bearer", "test-outsider");
        var outsider = await http.PostAsync("/api/matchday-polls/http-organizer", null);
        check((int)outsider.StatusCode == 403
            && JObject.Parse(await outsider.Content.ReadAsStringAsync())["errorCode"]!.Value<string>() == "organizer_required",
            "authenticated other account cannot create an owner's poll");
        http.DefaultRequestHeaders.Authorization = new("Bearer", "test-owner");
        var ownerCreate = await http.PostAsync("/api/matchday-polls/http-organizer", null);
        var ownerJson = JObject.Parse(await ownerCreate.Content.ReadAsStringAsync());
        check(ownerCreate.IsSuccessStatusCode && ownerJson["status"]!.Value<string>() == "open"
            && ownerJson["results"] == null && ownerJson["ballotCount"] == null,
            "authenticated owner HTTP creation returns the hidden-results contract");
        check(ownerJson["eventStartedAt"]!.ToObject<DateTimeOffset>() == new DateTimeOffset(2026, 9, 18, 17, 30, 0, TimeSpan.Zero),
            "organizer HTTP response includes the event start as an explicit UTC instant");
        var ownerRead = await http.GetAsync("/api/matchday-polls/http-organizer");
        check(ownerRead.IsSuccessStatusCode, "authenticated owner can read their poll");
        var eventBody = """{"matchName":"Friday football","location":"North pitch","scheduledAt":"2026-09-18T16:30:00Z","timeZone":"Asia/Jerusalem","language":"en"}""";
        var savedEvent = await http.PostAsync("/api/matchday-polls/http-organizer/event",
            new StringContent(eventBody, System.Text.Encoding.UTF8, "application/json"));
        check(savedEvent.IsSuccessStatusCode, "organizer can persist event metadata over HTTP");
        var withEvent = JObject.Parse(await http.GetStringAsync("/api/polls/" + ownerJson["token"]!.Value<string>()));
        check(withEvent["eventDetails"]!["matchName"]!.Value<string>() == "Friday football"
            && withEvent["eventDetails"]!["scheduledAt"]!.ToObject<DateTimeOffset>() == new DateTimeOffset(2026, 9, 18, 16, 30, 0, TimeSpan.Zero)
            && withEvent["eventDetails"]!.Children().Count() == 5 && withEvent["ballotCount"] == null,
            "public HTTP contract exposes only saved event name/location/schedule/timezone/language and keeps open aggregates hidden");
        http.DefaultRequestHeaders.Authorization = new("Bearer", "test-outsider");
        var outsiderEvent = await http.PostAsync("/api/matchday-polls/http-organizer/event",
            new StringContent(eventBody, System.Text.Encoding.UTF8, "application/json"));
        check((int)outsiderEvent.StatusCode == 403, "non-organizer cannot enrich even an already populated public poll");
        var outsiderRead = await http.GetAsync("/api/matchday-polls/http-organizer");
        var outsiderClose = await http.PostAsync("/api/matchday-polls/http-organizer/close", null);
        check((int)outsiderRead.StatusCode == 403 && (int)outsiderClose.StatusCode == 403,
            "authenticated other account cannot read organizer data or close the poll");
        http.DefaultRequestHeaders.Authorization = new("Bearer", "test-owner");
        var ownerClose = await http.PostAsync("/api/matchday-polls/http-organizer/close", null);
        var ownerClosed = JObject.Parse(await ownerClose.Content.ReadAsStringAsync());
        check(ownerClose.IsSuccessStatusCode && ownerClosed["status"]!.Value<string>() == "closed"
            && ownerClosed["ballotCount"]!.Value<long>() == 0 && ownerClosed["results"] is JArray,
            "authenticated owner HTTP close returns final results");
        http.DefaultRequestHeaders.Authorization = new("Bearer", "invalid-existing-session");
        check((await http.GetAsync("/api/polls/" + poll.Token)).IsSuccessStatusCode,
            "public poll stays anonymous even if generic fetch attaches an invalid existing bearer");
        http.DefaultRequestHeaders.Authorization = null;
        await store.ReadAsync(poll.Token, null, true, default);
        var closedJson = JObject.Parse(await http.GetStringAsync("/api/polls/" + poll.Token));
        check(closedJson["ballotCount"]!.Value<long>() == 1 && closedJson["results"]![0]!["firstChoiceVotes"]!.Value<long>() == 1
            && closedJson["results"]![0]!["points"]!.Value<long>() == 5, "closed HTTP response exposes final camelCase tally fields");
        using var limiter = new PollPublicRateLimiter();
        for (var i = 0; i < 12; i++) limiter.Check("192.0.2.1", true, true);
        await Expect(() => { limiter.Check("192.0.2.1", true, true); return Task.CompletedTask; }, 429, "rate_limited", check);
        var telemetry = new RequestTelemetry { Url = new Uri("https://example.test/api/polls/" + poll.Token + "?secret=x"), Name = poll.Token };
        telemetry.Context.Location.Ip = "192.0.2.1";
        new PollTelemetryRedactor().Initialize(telemetry);
        check(!telemetry.Url.ToString().Contains(poll.Token) && !telemetry.Name.Contains(poll.Token)
            && telemetry.Url.Query == "" && telemetry.Context.Location.Ip == "0.0.0.0", "telemetry strips capability URLs and raw client IP");
        await app.StopAsync();
    }

    private static async Task Expect(Func<Task> action, int status, string code, Action<bool, string> check)
    {
        try { await action(); check(false, $"Expected {status} {code}"); }
        catch (PollException ex) { check(ex.StatusCode == status && ex.ErrorCode == code, $"Expected {status} {code}, got {ex.StatusCode} {ex.ErrorCode}"); }
    }

    private sealed class TestAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public TestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger, System.Text.Encodings.Web.UrlEncoder encoder) : base(options, logger, encoder) { }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var header = Request.Headers.Authorization.ToString();
            if (header.Length == 0) return Task.FromResult(AuthenticateResult.NoResult());
            var uid = header switch { "Bearer test-owner" => "owner", "Bearer test-outsider" => "outsider", _ => null };
            if (uid == null) return Task.FromResult(AuthenticateResult.Fail("Invalid synthetic credential"));
            var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("user_id", uid) }, Scheme.Name));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }
    }

    internal sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    internal sealed class MemoryTables : TableServiceClient
    {
        private readonly Dictionary<string, MemoryTable> tables = new();
        internal MemoryTable Table(string name)
        {
            if (!tables.TryGetValue(name, out var table)) tables[name] = table = new();
            return table;
        }
        public override TableClient GetTableClient(string tableName) => Table(tableName);
    }

    internal sealed class MemoryTable : TableClient
    {
        private readonly Dictionary<(string, string), ITableEntity> rows = new();
        private readonly object gate = new();
        private int revision;
        internal int CreateCalls;
        internal TimeSpan ReadDelay;
        internal int PeakReads;
        internal int QueryReads;
        internal List<string[]> QuerySelections = new();
        private int activeReads;
        internal Func<Task>? BeforeTransaction;
        internal Func<Task>? BeforeUpdate;
        private static T Copy<T>(T entity) where T : class
        {
            var copy = JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(entity))!;
            if (copy is ITableEntity target && entity is ITableEntity source) target.ETag = source.ETag;
            return copy;
        }
        private void Store(ITableEntity entity)
        {
            var copy = (ITableEntity)JsonConvert.DeserializeObject(JsonConvert.SerializeObject(entity), entity.GetType())!;
            copy.ETag = new ETag($"\"{++revision}\"");
            copy.Timestamp = DateTimeOffset.UtcNow;
            rows[(copy.PartitionKey, copy.RowKey)] = copy;
        }
        public override Response<TableItem> CreateIfNotExists(CancellationToken cancellationToken = default)
        {
            CreateCalls++;
            return Response.FromValue<TableItem>(null!, new MemoryResponse());
        }
        public override async Task<NullableResponse<T>> GetEntityIfExistsAsync<T>(string partitionKey, string rowKey,
            IEnumerable<string>? select = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (gate) { activeReads++; PeakReads = Math.Max(PeakReads, activeReads); }
            try
            {
                if (ReadDelay > TimeSpan.Zero) await Task.Delay(ReadDelay, cancellationToken);
                lock (gate)
                {
                    rows.TryGetValue((partitionKey, rowKey), out var row);
                    return new Optional<T>(row is T typed ? Copy(typed) : null);
                }
            }
            finally { lock (gate) activeReads--; }
        }
        public override Task<Response<T>> GetEntityAsync<T>(string partitionKey, string rowKey,
            IEnumerable<string>? select = null, CancellationToken cancellationToken = default)
        {
            lock (gate) return Task.FromResult(Response.FromValue(Copy((T)rows[(partitionKey, rowKey)]), new MemoryResponse()));
        }
        public override AsyncPageable<T> QueryAsync<T>(Expression<Func<T, bool>> filter, int? maxPerPage = null,
            IEnumerable<string>? select = null, CancellationToken cancellationToken = default)
        {
            lock (gate)
            {
                QueryReads++;
                QuerySelections.Add(select?.ToArray() ?? Array.Empty<string>());
                var chunks = rows.Values.Select(row =>
                    JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(row))!).Where(filter.Compile())
                    .Chunk(maxPerPage ?? 1000).ToArray();
                return AsyncPageable<T>.FromPages(chunks.Select((chunk, index) =>
                    Page<T>.FromValues(chunk, index + 1 < chunks.Length ? (index + 1).ToString() : null, new MemoryResponse())));
            }
        }
        public override Task<Response> AddEntityAsync<T>(T entity, CancellationToken cancellationToken = default)
        {
            lock (gate)
            {
                if (rows.ContainsKey((entity.PartitionKey, entity.RowKey))) throw new RequestFailedException(409, "Exists");
                Store(entity);
                return Task.FromResult<Response>(new MemoryResponse());
            }
        }
        private void RequireCurrent(ITableEntity entity, ETag tag)
        {
            if (!rows.TryGetValue((entity.PartitionKey, entity.RowKey), out var old)) throw new RequestFailedException(404, "Missing");
            if (old.ETag != tag) throw new RequestFailedException(412, "Changed");
        }
        public override async Task<Response> UpdateEntityAsync<T>(T entity, ETag ifMatch,
            TableUpdateMode mode = TableUpdateMode.Merge, CancellationToken cancellationToken = default)
        {
            if (BeforeUpdate is { } hook) await hook();
            lock (gate)
            {
                RequireCurrent(entity, ifMatch);
                Store(entity);
                return new MemoryResponse();
            }
        }
        public override async Task<Response<IReadOnlyList<Response>>> SubmitTransactionAsync(
            IEnumerable<TableTransactionAction> transactionActions, CancellationToken cancellationToken = default)
        {
            var actions = transactionActions.ToArray();
            if (actions.Select(a => a.Entity.PartitionKey).Distinct().Count() != 1) throw new InvalidOperationException("Cross-partition transaction");
            if (BeforeTransaction is { } hook) await hook();
            lock (gate)
            {
                foreach (var action in actions)
                {
                    if (action.ActionType == TableTransactionActionType.Add)
                    {
                        if (rows.ContainsKey((action.Entity.PartitionKey, action.Entity.RowKey))) throw new RequestFailedException(409, "Exists");
                    }
                    else RequireCurrent(action.Entity, action.ETag);
                }
                foreach (var action in actions) Store(action.Entity);
                return Response.FromValue<IReadOnlyList<Response>>(actions.Select(_ => (Response)new MemoryResponse()).ToArray(), new MemoryResponse());
            }
        }
    }
    private sealed class Optional<T> : NullableResponse<T> where T : class
    {
        private readonly T? value;
        internal Optional(T? value) => this.value = value;
        public override bool HasValue => value != null;
        public override T Value => value ?? throw new InvalidOperationException("Missing");
        public override Response GetRawResponse() => new MemoryResponse();
    }
    private sealed class MemoryResponse : Response
    {
        public override int Status => 200;
        public override string ReasonPhrase => "OK";
        public override Stream? ContentStream { get; set; }
        public override string ClientRequestId { get; set; } = "";
        public override void Dispose() { }
        protected override bool ContainsHeader(string name) => false;
        protected override IEnumerable<HttpHeader> EnumerateHeaders() => Array.Empty<HttpHeader>();
        protected override bool TryGetHeader(string name, out string value) { value = ""; return false; }
        protected override bool TryGetHeaderValues(string name, out IEnumerable<string> values) { values = Array.Empty<string>(); return false; }
    }
}
