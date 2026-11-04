using System.Linq.Expressions;
using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using Azure;
using Azure.Core;
using Azure.Data.Tables;
using Azure.Data.Tables.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TeamsGeneratorWebAPI.Clients;
using TeamsGeneratorWebAPI.Collaboration;
using TeamsGeneratorWebAPI.Controllers;
using TeamsGeneratorWebAPI.PlayersBlob;
using TeamsGeneratorWebAPI.Telemetry;

internal static class LiveMatchConcurrencyRegression
{
    internal static async Task Run(Action<bool, string> check)
    {
        var table = new MemoryTable();
        var storage = new AzureTableStorageService(new MemoryTables(table));
        var telemetry = CallbackProxy.Create<IUsageTelemetry>((_, _) => null);
        var collaboration = CallbackProxy.Create<IGroupCollaborationService>((_, values) =>
            Task.FromResult<GroupAccess?>(new GroupAccess { OwnerId = "owner",
                GroupId = values?[1] as string ?? "default", IsOwner = true }));
        TeamsController Controller(ITeamsStorageBlobConnector? blobs = null) => new(
            NullLogger<TeamsController>.Instance, telemetry, blobs!, storage, null!, null!, collaboration)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        var a = Controller();
        var b = Controller();
        static MatchEntity Match(string key, params string[] goals) => new()
        {
            PartitionKey = "day",
            RowKey = key,
            SerializedMatch = new JObject
            {
                ["teamA"] = new JObject { ["score"] = goals.Length, ["goals"] = new JArray(goals) },
                ["teamB"] = new JObject { ["score"] = 0 }
            }.ToString()
        };
        static MatchWriteRequest Write(MatchEntity match, string? revision, int? version = 2) => new()
        {
            PartitionKey = match.PartitionKey, RowKey = match.RowKey, SerializedMatch = match.SerializedMatch,
            WriteContractVersion = version, ExpectedRevision = revision
        };
        static bool IsStatus(IActionResult result, int status) =>
            result is ObjectResult response && response.StatusCode == status;
        static string[] Goals(MatchEntity match) =>
            JObject.Parse(match.SerializedMatch)["teamA"]!["goals"]!.Values<string>().Select(x => x!).ToArray();
        async Task<MatchEntity> Read(string key = "game") =>
            (await storage.GetAllMatchesAsync("day")).Single(x => x.RowKey == key);

        check(await a.StartScoreboard("20.3", "day") is OkResult, "start remains legacy compatible without version fields");
        var startWrites = table.Writes;
        check(await b.StartScoreboard("20.3", "day") is OkResult && table.Writes == startWrites,
            "replayed auto-start acknowledges an already open matchday without writes or a Bad Request popup");
        table.BeforeTransaction = async () =>
        {
            table.BeforeTransaction = null;
            check(await b.StartScoreboard("20.3", "retry-day") is OkResult, "second start commits while first is in flight");
        };
        check(await a.StartScoreboard("20.3", "retry-day") is OkResult,
            "first start safely acknowledges the identical concurrent winner after conditional-write conflict");
        check(await a.AddMatch("20.3", Match("game", "one", "two")) is OkObjectResult, "legacy Add remains create-only");
        var legacyDay = Match("legacy-game", "one");
        legacyDay.PartitionKey = "legacy-unstarted-day";
        check(await a.AddMatch("20.3", legacyDay) is OkObjectResult,
            "legacy Add retains existing unstarted-day behavior; wrong-day routing remains a documented client risk");
        check(await a.AddPlayerSwap("20.3", new PlayerSwapEntity
        {
            PartitionKey = legacyDay.PartitionKey, RowKey = "swap", SerializedSwap = "{}"
        }) is OkObjectResult, "legacy swap remains compatible without version or started-day policy");
        check(await a.DoneAndReadMatches("20.3", legacyDay.PartitionKey) is OkObjectResult,
            "legacy Done remains compatible without version or started-day policy");
        var original = await Read();
        check(!string.IsNullOrEmpty(original.Revision)
            && JArray.FromObject(((OkObjectResult)await a.ReadMatches("day")).Value!)[0]!["revision"]!.Value<string>() == original.ETag.ToString(),
            "match JSON exposes the exact opaque storage revision");
        var legacyWrites = table.Writes;
        check(IsStatus(await b.EditMatch("20.3", Write(Match("game", "one"), null, null)), 428),
            "legacy revisionless edit returns actionable upgrade response");
        check(IsStatus(await b.DeleteMatch("20.3", Write(original, null, null)), 428) && table.Writes == legacyWrites,
            "legacy delete is rejected without touching storage");
        check(IsStatus(await b.EditMatch("20.11", Write(original, "*")), 428),
            "wildcard revision cannot disable concurrency protection");
        check(IsStatus(await b.EditMatch("20.11", Write(original, original.Revision, 99)), 428),
            "unsupported write contract cannot bypass protection");
        check(await a.EditMatch("safe", Write(Match("game", "two"), original.Revision)) is OkObjectResult,
            "A removes first goal with its observed revision");
        var afterA = await Read();
        check(afterA.Revision != original.Revision && afterA.CreatedAt == original.CreatedAt,
            "edit advances revision and preserves original creation order");
        var stale = await b.EditMatch("safe", Write(Match("game", "one"), original.Revision));
        check(IsStatus(stale, 409) && Goals(await Read()).SequenceEqual(new[] { "two" }),
            "B's stale edit cannot resurrect A's deleted goal");
        var conflict = JObject.FromObject(((ConflictObjectResult)stale).Value!);
        check(conflict["IsConflict"]!.Value<bool>() && conflict["Matches"]![0]!["revision"]!.Value<string>() == afterA.Revision,
            "conflict includes authoritative rows with revisions");
        check(IsStatus(await b.DeleteMatch("safe", Write(original, original.Revision)), 409),
            "stale delete cannot remove a revised match");
        check(IsStatus(await b.AddMatch("20.3", Match("game", "one", "two")), 409)
            && Goals(await Read()).SequenceEqual(new[] { "two" }),
            "duplicate Add never merges goals or sums scores");

        var firstRecording = Match("recording-a", "same-goal");
        firstRecording.PartitionKey = "append-order";
        var secondRecording = Match("recording-b", "same-goal");
        secondRecording.PartitionKey = "append-order";
        table.BeforeTransaction = async () =>
        {
            table.BeforeTransaction = null;
            check(await b.AddMatch("safe", secondRecording) is OkObjectResult,
                "another organizer saves an independent recording while the first is still in flight");
        };
        check(await a.AddMatch("safe", firstRecording) is OkObjectResult,
            "delayed new recording appends successfully after status contention");
        var orderedRecordings = await storage.GetAllMatchesAsync("append-order");
        check(orderedRecordings.Select(match => match.RowKey)
                .SequenceEqual(new[] { "recording-b", "recording-a" })
            && orderedRecordings[1].CreatedAt > orderedRecordings[0].CreatedAt,
            "numbered history follows successful save order, not when recording or the request started");
        check(orderedRecordings.All(match => Goals(match).SequenceEqual(new[] { "same-goal" })),
            "separate organizer recordings with identical goals are retained rather than deduplicated");

        var orderStatus = (await table.GetEntityAsync<MatchdayMetadataEntity>(
            "append-order", AzureTableStorageService.RowKeyForCloseStatus)).Value;
        var clockAhead = DateTime.UtcNow.AddMinutes(1);
        orderStatus.LastMatchCreatedAt = clockAhead;
        await table.UpdateEntityAsync(orderStatus, orderStatus.ETag);
        check(await a.EditMatch("safe", Write(orderedRecordings[0], orderedRecordings[0].Revision)) is OkObjectResult,
            "an existing recording can still be edited without changing its history position");
        check(await a.AddPlayerSwap("safe", new PlayerSwapEntity
        {
            PartitionKey = "append-order", RowKey = "swap", SerializedSwap = "{}"
        }) is OkObjectResult, "player swaps preserve the append-order marker");
        var thirdRecording = Match("recording-c", "next-goal");
        thirdRecording.PartitionKey = "append-order";
        check(await a.AddMatch("safe", thirdRecording) is OkObjectResult,
            "another recording appends after unrelated matchday mutations");
        var afterClockChange = (await storage.GetAllMatchesAsync("append-order")).Last();
        check(afterClockChange.RowKey == "recording-c"
            && afterClockChange.CreatedAt >= clockAhead.AddMilliseconds(1),
            "append order survives clock rollback and has at least millisecond separation");
        await a.DoneAndReadMatches("safe", "append-order");
        check((await table.GetEntityAsync<MatchdayMetadataEntity>(
                "append-order", AzureTableStorageService.RowKeyForCloseStatus)).Value.LastMatchCreatedAt
            == afterClockChange.CreatedAt, "closing a matchday preserves its last append position");

        var legacyRecording = Match("old-recording", "legacy-goal");
        legacyRecording.PartitionKey = "legacy-order";
        legacyRecording.CreatedAt = DateTime.UtcNow.AddMinutes(2);
        await table.AddEntityAsync(legacyRecording);
        var newRecording = Match("new-recording", "new-goal");
        newRecording.PartitionKey = "legacy-order";
        check(await a.AddMatch("safe", newRecording) is OkObjectResult,
            "existing history without an append marker needs no migration writes to match records");
        var upgradedHistory = await storage.GetAllMatchesAsync("legacy-order");
        check(upgradedHistory.Select(match => match.RowKey)
                .SequenceEqual(new[] { "old-recording", "new-recording" })
            && upgradedHistory[0].CreatedAt == legacyRecording.CreatedAt
            && upgradedHistory[1].CreatedAt >= legacyRecording.CreatedAt.AddMilliseconds(1),
            "the first new append starts after existing history without renumbering or changing old results");

        var suspended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        table.BeforeTransaction = async () =>
        {
            table.BeforeTransaction = null;
            suspended.SetResult();
            await release.Task;
        };
        var inFlight = a.EditMatch("safe", Write(Match("game", "one", "two"), afterA.Revision));
        await suspended.Task;
        check(await b.EditMatch("safe", Write(Match("game", "three"), afterA.Revision)) is OkObjectResult,
            "B commits while A is suspended before its transaction");
        release.SetResult();
        check(IsStatus(await inFlight, 409) && Goals(await Read()).SequenceEqual(new[] { "three" }),
            "atomic ETag guard rejects a race after the server read");

        var beforeUnrelated = await Read();
        table.BeforeTransaction = async () =>
        {
            table.BeforeTransaction = null;
            await b.AddMatch("20.3", Match("other", "one"));
        };
        check(await a.EditMatch("safe", Write(Match("game", "one", "two", "three", "four"), beforeUnrelated.Revision)) is OkObjectResult,
            "status-only contention retries safely while match revision is unchanged; no global score cap");
        var four = await Read();
        check(Goals(four).Length == 4, "formats allowing four goals remain supported");
        var deleteRequest = Write(await Read("other"), (await Read("other")).Revision);
        deleteRequest.SerializedMatch = null;
        check(await b.DeleteMatch("safe", deleteRequest) is OkObjectResult,
            "delete succeeds with observed revision and no unnecessary serialized snapshot");

        table.BeforeTransaction = async () =>
        {
            table.BeforeTransaction = null;
            await b.DoneAndReadMatches("20.3", "day");
        };
        var racingClose = await a.EditMatch("safe", Write(Match("game", "changed"), four.Revision));
        check(racingClose is OkObjectResult closed && JObject.FromObject(closed.Value!)["IsClosed"]!.Value<bool>(),
            "matchday close wins atomically over in-flight edit");
        check(Goals(await Read()).Length == 4, "closed matchday history remains unchanged");
        var closedWrites = table.Writes;
        var closedAdd = await a.AddMatch("20.3", Match("late", "one"));
        check(closedAdd is OkObjectResult closedResponse && JObject.FromObject(closedResponse.Value!)["IsClosed"]!.Value<bool>()
            && table.Writes == closedWrites, "legacy Add still cannot mutate a closed day");
        var closedStart = await a.StartScoreboard("20.3", "day");
        check(closedStart is ConflictObjectResult && JObject.FromObject(((ConflictObjectResult)closedStart).Value!)["isClosed"]!.Value<bool>()
            && table.Writes == closedWrites, "replayed start cannot reopen a closed day and returns an actionable conflict");
        await CheckBlobs(check, Controller);
    }

    private static async Task CheckBlobs(Action<bool, string> check, Func<ITeamsStorageBlobConnector?, TeamsController> controller)
    {
        await using var server = new LocalBlobs();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["BlobConnectionString"] = $"DefaultEndpointsProtocol=http;AccountName=synthetic;AccountKey={Convert.ToBase64String(new byte[32])};BlobEndpoint={server.Endpoint}synthetic;",
            ["TeamsBlobContainerName"] = "teams"
        }).Build();
        var blobs = new TeamsStorageBlobConnector(config, NullLogger<TeamsStorageBlobConnector>.Instance);
        var endpoint = controller(blobs);
        var group = new TeamsBlobConfig { UId = "owner", GroupId = "default" };
        static JObject Snapshot(string value) => new() { ["teams"] = new JArray(new JObject { ["name"] = value }) };
        static JObject Request(string value, string revision) => new()
        {
            ["teams"] = Snapshot(value)["teams"]!.DeepClone(), ["writeContractVersion"] = 2, ["expectedRevision"] = revision
        };
        var absent = (GetTeamsFromStorageResponse)await blobs.ListAsync(group);
        check(!absent.Exists && absent.Revision == "absent", "missing snapshot exposes explicit creation precondition");
        var before = server.RequestCount;
        check(await endpoint.SaveToStorage("20.3", Snapshot("legacy"), "owner") is ObjectResult legacy
            && legacy.StatusCode == 428 && server.RequestCount == before, "legacy teams overwrite denied before storage");
        check((await blobs.UploadAsync(Snapshot("legacy"), group)) is TeamsStorageWriteResponse denied && denied.RequiresUpgrade,
            "legacy connector entrypoint cannot bypass conditional writes");
        var created = await blobs.UploadVersionedAsync(Snapshot("old"), group, "absent");
        check(created.Success && !string.IsNullOrEmpty(created.Revision), "initial teams save uses If-None-Match");
        check((await blobs.UploadVersionedAsync(Snapshot("duplicate"), group, "absent")).IsConflict,
            "two initial writers cannot overwrite each other");
        var old = (GetTeamsFromStorageResponse)await blobs.ListAsync(group);
        check(old.Revision == created.Revision && old.Teams["teams"]![0]!["name"]!.Value<string>() == "old",
            "existing raw snapshot is read with storage ETag without migration writes");
        var saved = await endpoint.SaveToStorage("safe", Request("new", old.Revision), "owner");
        check(saved is OkObjectResult, "versioned controller save succeeds");
        check(await endpoint.SaveToStorage("safe", Request("old", old.Revision), "owner") is ConflictObjectResult,
            "stale old teams cannot roll newer teams backward");
        var latest = (GetTeamsFromStorageResponse)await blobs.ListAsync(group);
        check(latest.Teams["teams"]![0]!["name"]!.Value<string>() == "new", "latest snapshot retained after stale upload");
        check(server.LastJson != null && JObject.Parse(server.LastJson).Properties().Select(p => p.Name).SequenceEqual(new[] { "teams" }),
            "write metadata never contaminates persisted legacy snapshot shape");
        server.HoldNextPut();
        var delayed = blobs.UploadVersionedAsync(Snapshot("delayed"), group, latest.Revision);
        await server.PutHeld;
        var winner = await blobs.UploadVersionedAsync(Snapshot("winner"), group, latest.Revision);
        server.ReleasePut();
        check(winner.Success && (await delayed).IsConflict, "delayed conditional PUT cannot overwrite a newer completion");
        check(((GetTeamsFromStorageResponse)await blobs.ListAsync(group)).Teams["teams"]![0]!["name"]!.Value<string>() == "winner",
            "delayed old response preserves winning teams");
        var other = new TeamsBlobConfig { UId = "owner", GroupId = "other" };
        var otherTeams = Snapshot("other");
        otherTeams["teams"]![0]!["players"] = JArray.Parse(
            """[{"key":"public-player","name":"Player","goalkeeperSkill":7,"isRecoveringFromInjury":true,"effectiveRank":9}]""");
        check((await blobs.UploadVersionedAsync(otherTeams, other, "absent")).Success
            && ((GetTeamsFromStorageResponse)await blobs.ListAsync(group)).Revision == winner.Revision,
            "group snapshots and revisions stay isolated");
        var otherSnapshot = (GetTeamsFromStorageResponse)await blobs.ListAsync(other);
        check(await endpoint.StartScoreboard("safe", "scoped-day", "other", otherSnapshot.Revision) is UnauthorizedResult,
            "scoped matchday creation requires organizer identity");
        endpoint.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, "owner") }, "synthetic"));
        check(await endpoint.StartScoreboard("safe", "scoped-day", "other", "outdated") is ConflictObjectResult,
            "matchday start rejects a stale lineup revision rather than sharing different teams");
        check(await endpoint.StartScoreboard("safe", "scoped-day", "other", otherSnapshot.Revision) is OkResult,
            "non-default matchday persists the observed group's lineup");
        check(await endpoint.StartScoreboard("safe", "scoped-day", "other", otherSnapshot.Revision) is OkResult,
            "scoped auto-start retry acknowledges the same canonical owner, group, revision and lineup");
        var opened = JObject.FromObject(((OkObjectResult)await endpoint.GetMatchday("safe", "scoped-day")).Value!);
        check(opened["ownerId"]!.Value<string>() == "owner" && opened["groupId"]!.Value<string>() == "other"
            && opened["teams"]![0]!["name"]!.Value<string>() == "other",
            "public matchday metadata returns its durable owner, group, and teams snapshot");
        var publicPlayer = opened["teams"]![0]!["players"]![0]!;
        check(publicPlayer["key"]!.Value<string>() == "public-player"
            && publicPlayer["goalkeeperSkill"] == null && publicPlayer["isRecoveringFromInjury"] == null
            && publicPlayer["effectiveRank"] == null && opened["teamsRevision"]!.Value<string>() == otherSnapshot.Revision,
            "public matchday snapshot preserves its revision and removes private player assessments");
        await blobs.UploadVersionedAsync(Snapshot("later-lineup"), other, otherSnapshot.Revision);
        var reopened = JObject.FromObject(((OkObjectResult)await endpoint.GetMatchday("safe", "scoped-day")).Value!);
        check(JToken.DeepEquals(opened, reopened), "later group lineup changes cannot rewrite an already shared matchday");
        check(await endpoint.StartScoreboard("20.3", "scoped-day") is ConflictObjectResult,
            "legacy starts cannot replace a scoped matchday identity");
        endpoint.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, "co-organizer") }, "synthetic"));
        var laterSnapshot = (GetTeamsFromStorageResponse)await blobs.ListAsync(other);
        check(await endpoint.StartScoreboard("safe", "scoped-day", "other", laterSnapshot.Revision) is ConflictObjectResult,
            "an existing matchday cannot be restarted with a later lineup");
        check(await endpoint.StartScoreboard("safe", "co-day", "other", laterSnapshot.Revision) is OkResult
            && JObject.FromObject(((OkObjectResult)await endpoint.GetMatchday("safe", "co-day")).Value!)["ownerId"]!.Value<string>() == "owner",
            "matchday snapshot uses collaboration's canonical owner rather than the co-organizer account");
        var historical = new MatchEntity
        {
            PartitionKey = "older-still-open-day", RowKey = "historical-game",
            SerializedMatch = """{"teamA":{"score":2},"teamB":{"score":0}}"""
        };
        var historyResult = (OkObjectResult)await endpoint.AddMatch("20.3", historical);
        var historyRevision = ((MatchReadResponse[])historyResult.Value!).Single().Revision;
        check(await endpoint.EditMatch("safe", new MatchWriteRequest
        {
            PartitionKey = historical.PartitionKey, RowKey = historical.RowKey,
            SerializedMatch = """{"teamA":{"score":1},"teamB":{"score":0}}""",
            WriteContractVersion = 2, ExpectedRevision = historyRevision
        }) is OkObjectResult
            && ((GetTeamsFromStorageResponse)await blobs.ListAsync(group)).Revision == winner.Revision,
            "valid old-day game edits preserve current generated teams without moving a day pointer");
        var sharedAccess = CallbackProxy.Create<IGroupCollaborationService>((_, _) =>
            Task.FromResult<GroupAccess?>(new GroupAccess { OwnerId = "owner", GroupId = "default", IsOwner = false }));
        var sharedEndpoint = new TeamsController(
            NullLogger<TeamsController>.Instance, CallbackProxy.Create<IUsageTelemetry>((_, _) => null),
            blobs, null!, null!, null!, sharedAccess)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("user_id", "co-organizer") }, "test"))
                }
            }
        };
        var shared = (OkObjectResult)await sharedEndpoint.GetTeamsFromStorage("safe", "co-organizer", "shared.group");
        check(JObject.FromObject(shared.Value!)["revision"]!.Value<string>() == winner.Revision,
            "co-organizer response retains snapshot revision through redaction");
        server.FailReads = true;
        try
        {
            await blobs.ListAsync(group);
            check(false, "storage read failures must not appear as missing snapshots");
        }
        catch (RequestFailedException exception) when (exception.Status == 403)
        {
            check(true, "storage failures surface rather than yielding success-shaped cached fallbacks");
        }
    }

    private sealed class MemoryTables : TableServiceClient
    {
        private readonly TableClient table;
        internal MemoryTables(TableClient table) => this.table = table;
        public override TableClient GetTableClient(string tableName) => table;
    }

    private sealed class MemoryTable : TableClient
    {
        private readonly Dictionary<(string, string), ITableEntity> rows = new();
        internal int Writes { get; private set; }
        internal Func<Task>? BeforeTransaction { get; set; }
        private static T Copy<T>(T entity) where T : class
        {
            var copy = JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(entity))!;
            if (copy is ITableEntity target && entity is ITableEntity source) target.ETag = source.ETag;
            return copy;
        }
        private void Store(ITableEntity entity)
        {
            var copy = (ITableEntity)JsonConvert.DeserializeObject(JsonConvert.SerializeObject(entity), entity.GetType())!;
            copy.ETag = new ETag($"\"{++Writes}\"");
            copy.Timestamp = DateTimeOffset.UtcNow;
            rows[(copy.PartitionKey, copy.RowKey)] = copy;
        }
        public override Response<TableItem> CreateIfNotExists(CancellationToken cancellationToken = default) =>
            Response.FromValue<TableItem>(null!, new MemoryResponse());
        public override Task<NullableResponse<T>> GetEntityIfExistsAsync<T>(string partitionKey, string rowKey,
            IEnumerable<string>? select = null, CancellationToken cancellationToken = default)
        {
            rows.TryGetValue((partitionKey, rowKey), out var row);
            return Task.FromResult<NullableResponse<T>>(new OptionalResponse<T>(row is T typed ? Copy(typed) : null));
        }
        public override Task<Response<T>> GetEntityAsync<T>(string partitionKey, string rowKey,
            IEnumerable<string>? select = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(Response.FromValue(Copy((T)rows[(partitionKey, rowKey)]), new MemoryResponse()));
        public override AsyncPageable<T> QueryAsync<T>(Expression<Func<T, bool>> filter, int? maxPerPage = null,
            IEnumerable<string>? select = null, CancellationToken cancellationToken = default) =>
            AsyncPageable<T>.FromPages(new[] { Page<T>.FromValues(rows.Values.OfType<T>().Select(Copy)
                .Where(filter.Compile()).ToArray(), null, new MemoryResponse()) });
        public override Task<Response> AddEntityAsync<T>(T entity, CancellationToken cancellationToken = default)
        {
            if (rows.ContainsKey((entity.PartitionKey, entity.RowKey))) throw new RequestFailedException(409, "Exists");
            Store(entity);
            return Task.FromResult<Response>(new MemoryResponse());
        }
        private void RequireCurrent(ITableEntity entity, ETag tag)
        {
            if (!rows.TryGetValue((entity.PartitionKey, entity.RowKey), out var old)) throw new RequestFailedException(404, "Missing");
            if (old.ETag != tag) throw new RequestFailedException(412, "Changed");
        }
        public override Task<Response> UpdateEntityAsync<T>(T entity, ETag ifMatch,
            TableUpdateMode mode = TableUpdateMode.Merge, CancellationToken cancellationToken = default)
        {
            RequireCurrent(entity, ifMatch);
            Store(entity);
            return Task.FromResult<Response>(new MemoryResponse());
        }
        public override async Task<Response<IReadOnlyList<Response>>> SubmitTransactionAsync(
            IEnumerable<TableTransactionAction> transactionActions, CancellationToken cancellationToken = default)
        {
            var actions = transactionActions.ToArray();
            if (BeforeTransaction is { } hook) await hook();
            foreach (var action in actions)
            {
                if (action.ActionType == TableTransactionActionType.Add)
                {
                    if (rows.ContainsKey((action.Entity.PartitionKey, action.Entity.RowKey))) throw new RequestFailedException(409, "Exists");
                }
                else RequireCurrent(action.Entity, action.ETag);
            }
            foreach (var action in actions)
            {
                if (action.ActionType == TableTransactionActionType.Delete) rows.Remove((action.Entity.PartitionKey, action.Entity.RowKey));
                else Store(action.Entity);
            }
            return Response.FromValue<IReadOnlyList<Response>>(actions.Select(_ => (Response)new MemoryResponse()).ToArray(),
                new MemoryResponse());
        }
    }

    private sealed class OptionalResponse<T> : NullableResponse<T> where T : class
    {
        private readonly T? value;
        internal OptionalResponse(T? value) => this.value = value;
        public override bool HasValue => value != null;
        public override T Value => value ?? throw new InvalidOperationException("No value");
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

    private sealed class LocalBlobs : IAsyncDisposable
    {
        private readonly HttpListener listener = new();
        private readonly Task loop;
        private readonly List<Task> handlers = new();
        private readonly Dictionary<string, (byte[] Data, string ETag)> data = new();
        private readonly object gate = new();
        private volatile bool stopping;
        private bool holdNext;
        private int revision;
        private readonly TaskCompletionSource held = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal string Endpoint { get; }
        internal int RequestCount { get; private set; }
        internal string? LastJson { get; private set; }
        internal bool FailReads { get; set; }
        internal Task PutHeld => held.Task;
        internal void HoldNextPut() => holdNext = true;
        internal void ReleasePut() => release.SetResult();
        internal LocalBlobs()
        {
            var portReservation = new TcpListener(IPAddress.Loopback, 0);
            portReservation.Start();
            var port = ((IPEndPoint)portReservation.LocalEndpoint).Port;
            portReservation.Stop();
            Endpoint = $"http://127.0.0.1:{port}/";
            listener.Prefixes.Add(Endpoint);
            listener.Start();
            loop = Accept();
        }
        private async Task Accept()
        {
            while (!stopping)
            {
                HttpListenerContext context;
                try { context = await listener.GetContextAsync(); }
                catch (HttpListenerException) when (stopping) { break; }
                catch (ObjectDisposedException) when (stopping) { break; }
                handlers.Add(Respond(context));
            }
        }
        private async Task Respond(HttpListenerContext context)
        {
            RequestCount++;
            var request = context.Request;
            var response = context.Response;
            var key = request.Url!.AbsolutePath;
            response.Headers["x-ms-request-id"] = "synthetic";
            response.Headers["x-ms-version"] = "2021-12-02";
            response.Headers["Last-Modified"] = "Sun, 27 Sep 2026 20:00:00 GMT";
            response.Headers["x-ms-blob-type"] = "BlockBlob";
            if (request.HttpMethod == "PUT")
            {
                using var body = new MemoryStream();
                await request.InputStream.CopyToAsync(body);
                if (holdNext) { holdNext = false; held.SetResult(); await release.Task; }
                lock (gate)
                {
                    var exists = data.TryGetValue(key, out var existing);
                    if ((request.Headers["If-None-Match"] == "*" && exists)
                        || (request.Headers["If-Match"] is { } tag && (!exists || tag != existing.ETag)))
                    {
                        response.StatusCode = 412;
                        response.Headers["x-ms-error-code"] = "ConditionNotMet";
                    }
                    else
                    {
                        var nextTag = $"\"{++revision}\"";
                        data[key] = (body.ToArray(), nextTag);
                        LastJson = System.Text.Encoding.UTF8.GetString(body.ToArray());
                        response.Headers["ETag"] = nextTag;
                        response.StatusCode = 201;
                    }
                }
                response.ContentLength64 = 0;
            }
            else
            {
                byte[]? content = null;
                lock (gate)
                {
                    if (FailReads)
                    {
                        response.StatusCode = 403;
                        response.Headers["x-ms-error-code"] = "AuthorizationFailure";
                    }
                    else if (data.TryGetValue(key, out var item))
                    {
                        content = item.Data;
                        response.Headers["ETag"] = item.ETag;
                        response.StatusCode = 200;
                    }
                    else
                    {
                        response.StatusCode = 404;
                        response.Headers["x-ms-error-code"] = "BlobNotFound";
                    }
                }
                response.ContentLength64 = content?.Length ?? 0;
                response.ContentType = "application/octet-stream";
                if (content != null && request.HttpMethod == "GET") await response.OutputStream.WriteAsync(content);
            }
            if (response.StatusCode >= 400)
            {
                var errorBody = System.Text.Encoding.UTF8.GetBytes(
                    $"<?xml version=\"1.0\" encoding=\"utf-8\"?><Error><Code>{response.Headers["x-ms-error-code"]}</Code><Message>Synthetic storage response.</Message></Error>");
                response.ContentType = "application/xml";
                response.ContentLength64 = errorBody.Length;
                await response.OutputStream.WriteAsync(errorBody);
            }
            response.Close();
        }
        public async ValueTask DisposeAsync()
        {
            stopping = true;
            listener.Close();
            await loop;
            await Task.WhenAll(handlers);
        }
    }
}
