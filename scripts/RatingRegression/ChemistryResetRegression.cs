using System.Linq.Expressions;
using System.Reflection;
using System.Security.Claims;
using Azure;
using Azure.Core;
using Azure.Data.Tables;
using Azure.Data.Tables.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using TeamsGenerator.Orchestration;
using TeamsGeneratorWebAPI.Clients;
using TeamsGeneratorWebAPI.Controllers;

internal static class ChemistryResetRegression
{
    private const BindingFlags InstanceMethods = BindingFlags.Instance | BindingFlags.NonPublic;

    internal static async Task Run(Action<bool, string> check)
    {
        var cutoff = DateTimeOffset.UtcNow.AddHours(-2);
        var table = new MemoryTableClient { Now = cutoff };
        var service = new AzureTableStorageService(new MemoryTableServiceClient(table));
        var partition = Partition("account-a");
        ChemistryMatchdayEntity Evidence(string owner, string id, DateTimeOffset completedAt) => new()
        {
            PartitionKey = Partition(owner),
            RowKey = id,
            CompletedAt = completedAt,
            PairEvidenceJson = JsonConvert.SerializeObject(new[]
            {
                new { PairKey = ChemistryPairKey.Create("one", "two"), Evidence = 1, Samples = 2 }
            })
        };
        table.Seed(Evidence("account-a", "old", cutoff.AddDays(-1)));
        table.Seed(Evidence("account-a", "boundary", cutoff));
        table.Seed(Evidence("account-b", "other", cutoff.AddDays(-1)));
        var match = new MatchEntity
        {
            PartitionKey = "old",
            RowKey = "match-one",
            SerializedMatch = """{"teamA":{"score":2,"players":[{"key":"one","rank":5},{"key":"two","rank":5}]},"teamB":{"score":0,"players":[{"key":"three","rank":5},{"key":"four","rank":5}]}}"""
        };
        table.Seed(match);
        table.Seed(new MatchdayEntity { RowKey = "old", OwnerId = "account-a", Status = "closed" });
        var originalRows = table.Rows.ToDictionary(item => item.Key, item => JsonConvert.SerializeObject(item.Value));
        var original = await History(service, "account-a");
        check(original.Count == 2 && original.Scores.Count == 1, "chemistry without a reset retains legacy history");

        var controller = new TeamsController(
            NullLogger<TeamsController>.Instance, null!, null!, service, null!, null!, null!)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        check(await controller.ResetChemistry() is UnauthorizedObjectResult, "anonymous chemistry reset is rejected");
        controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(Array.Empty<Claim>(), "test"));
        check(await controller.ResetChemistry() is UnauthorizedObjectResult, "reset requires an authenticated account claim");
        check(table.Writes == 0, "unauthorized resets never touch storage");

        controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim("user_id", "account-a") }, "test"));
        controller.HttpContext.Request.QueryString = new QueryString("?ownerId=account-b&uid=account-b");
        check(await controller.ResetChemistry() is OkObjectResult, "authenticated organizer can reset chemistry");
        check(table.Writes == 1, "reset persists a single marker");
        foreach (var row in originalRows)
            check(JsonConvert.SerializeObject(table.Rows[row.Key]) == row.Value, "reset preserves history row " + row.Key);
        check(table.Rows.Count == originalRows.Count + 1, "reset neither removes nor recreates match history");
        check(!table.Rows.ContainsKey((Partition("account-b") + "_Settings", "Reset")),
            "supplied owner parameters cannot reset another account");

        var after = await History(service, "account-a");
        check(after.Count == 0 && after.Scores.Count == 0, "reset excludes earlier and exact-cutoff matchdays from scores and count");
        var other = await History(service, "account-b");
        check(other.Count == 1 && other.Scores.Count == 1, "other account chemistry is unchanged");
        var reloaded = new AzureTableStorageService(new MemoryTableServiceClient(table));
        check((await History(reloaded, "account-a")).Count == 0, "reset survives service restart");

        await Invoke(service, "StoreChemistryMatchday", "account-a", "old",
            new[] { match }, cutoff.AddMinutes(30), true);
        check((await History(service, "account-a")).Count == 0, "retrying old completion does not resurrect reset chemistry");
        check(((ChemistryMatchdayEntity)table.Rows[(partition, "old")]).CompletedAt == cutoff.AddDays(-1),
            "retried completion preserves original completion time");

        table.Seed(Evidence("account-a", "new", cutoff.AddMinutes(10)));
        var learned = await History(service, "account-a");
        check(learned.Count == 1 && learned.Scores.Count == 1 && learned.Scores.Values.Single() > 0,
            "future completed matchdays rebuild chemistry");
        table.FailWrites = true;
        try
        {
            await controller.ResetChemistry();
            check(false, "failed storage must not report a successful reset");
        }
        catch (RequestFailedException)
        {
            check((await History(service, "account-a")).Count == 1, "failed reset retains existing chemistry");
        }
        table.FailWrites = false;
        table.Now = cutoff.AddMinutes(20);
        await controller.ResetChemistry();
        check((await History(service, "account-a")).Count == 0, "a repeated reset advances the cutoff");
        check(table.Rows.Values.OfType<ChemistryMatchdayEntity>().Count() == 4, "repeated resets retain all evidence rows");

        var marker = (TableEntity)table.Rows[(partition + "_Settings", "Reset")];
        marker.Timestamp = null;
        try
        {
            await History(service, "account-a");
            check(false, "invalid reset marker must not silently restore old history");
        }
        catch (InvalidOperationException)
        {
            check(true, "invalid reset marker surfaces an explicit failure");
        }
    }

    private static string Partition(string owner) =>
        (string)typeof(AzureTableStorageService).Assembly.GetType("TeamsGeneratorWebAPI.Clients.ChemistryHistory")!
            .GetMethod("GetPartitionKey", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, new object[] { owner })!;

    private static Task Invoke(AzureTableStorageService service, string method, params object[] args) =>
        (Task)typeof(AzureTableStorageService).GetMethod(method, InstanceMethods)!.Invoke(service, args)!;

    private static async Task<(int Count, IReadOnlyDictionary<string, double> Scores)> History(
        AzureTableStorageService service, string owner)
    {
        var task = Invoke(service, "GetChemistryHistory", owner);
        await task;
        var result = task.GetType().GetProperty("Result")!.GetValue(task)!;
        return (
            (int)result.GetType().GetProperty("MatchdayCount", InstanceMethods)!.GetValue(result)!,
            (IReadOnlyDictionary<string, double>)result.GetType().GetProperty("Scores", InstanceMethods)!.GetValue(result)!);
    }

    private sealed class MemoryTableServiceClient : TableServiceClient
    {
        private readonly TableClient _table;
        internal MemoryTableServiceClient(TableClient table) => _table = table;
        public override TableClient GetTableClient(string tableName) => _table;
    }

    private sealed class MemoryTableClient : TableClient
    {
        internal Dictionary<(string, string), ITableEntity> Rows { get; } = new();
        internal DateTimeOffset Now { get; set; }
        internal bool FailWrites { get; set; }
        internal int Writes { get; private set; }
        internal void Seed(ITableEntity entity) => Rows[(entity.PartitionKey, entity.RowKey)] = entity;

        public override Response<TableItem> CreateIfNotExists(CancellationToken cancellationToken = default) =>
            Response.FromValue<TableItem>(null!, new MemoryResponse());

        public override Task<NullableResponse<T>> GetEntityIfExistsAsync<T>(
            string partitionKey, string rowKey, IEnumerable<string>? select = null,
            CancellationToken cancellationToken = default)
        {
            Rows.TryGetValue((partitionKey, rowKey), out var entity);
            return Task.FromResult<NullableResponse<T>>(new OptionalResponse<T>(entity as T));
        }

        public override AsyncPageable<T> QueryAsync<T>(
            Expression<Func<T, bool>> filter, int? maxPerPage = null, IEnumerable<string>? select = null,
            CancellationToken cancellationToken = default) =>
            AsyncPageable<T>.FromPages(new[]
            {
                Page<T>.FromValues(Rows.Values.OfType<T>().Where(filter.Compile()).ToArray(), null, new MemoryResponse())
            });

        public override Task<Response> UpsertEntityAsync<T>(
            T entity, TableUpdateMode mode = TableUpdateMode.Merge, CancellationToken cancellationToken = default)
        {
            if (FailWrites) throw new RequestFailedException(503, "Storage unavailable");
            Writes++;
            entity.Timestamp = Now;
            entity.ETag = new ETag(Writes.ToString());
            Seed(entity);
            return Task.FromResult<Response>(new MemoryResponse());
        }

        public override Task<Response> UpdateEntityAsync<T>(
            T entity, ETag ifMatch, TableUpdateMode mode = TableUpdateMode.Merge,
            CancellationToken cancellationToken = default)
        {
            if (!Rows.TryGetValue((entity.PartitionKey, entity.RowKey), out var previous))
                throw new RequestFailedException(404, "Not found");
            if (previous.ETag != ifMatch) throw new RequestFailedException(412, "Changed");
            return UpsertEntityAsync(entity, mode, cancellationToken);
        }
    }

    private sealed class OptionalResponse<T> : NullableResponse<T> where T : class
    {
        private readonly T? _value;
        internal OptionalResponse(T? value) => _value = value;
        public override bool HasValue => _value != null;
        public override T Value => _value ?? throw new InvalidOperationException("No value");
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
        protected override bool TryGetHeaderValues(string name, out IEnumerable<string> values)
        {
            values = Array.Empty<string>();
            return false;
        }
    }
}
