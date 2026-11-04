using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Data.Tables;
using Azure.Data.Tables.Models;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TeamsGenerator.Orchestration;
using TeamsGeneratorWebAPI.Capacity;
using TeamsGeneratorWebAPI.Premium;
using TeamsGeneratorWebAPI.Telemetry;

internal static class RuntimeCapacityRegression
{
    internal static async Task Run(Action<bool, string> check)
    {
        var options = new CapacityOptions();
        void Invalid(string json, int limit, string code)
        {
            using var document = JsonDocument.Parse(json);
            try
            {
                RuntimePayloadGuard.ValidateGeneration(document.RootElement, options, limit);
                check(false, "runtime rejects " + code);
            }
            catch (CapacityException exception) { check(exception.Code == code, "runtime rejects " + code); }
        }
        string Players(int count) => string.Join(",", Enumerable.Range(0, count)
            .Select(index => "{\"key\":\"p" + index + "\",\"name\":\"Player\"}"));
        Invalid("{\"config\":{},\"players\":[" + Players(26) + "]}", 25, "generation_players_limit");
        Invalid("{\"config\":{},\"players\":[" + Players(51) + "]}", 50, "generation_players_limit");
        Invalid("{\"config\":{\"numberOfTeams\":11},\"players\":[]}", 25, "generation_teams_limit");
        Invalid("{\"config\":{\"NumberOfTeams\":100000},\"players\":[]}", 25, "generation_teams_limit");
        Invalid("{\"config\":{\"numberOfTeams\":\"big\"},\"players\":[]}", 25, "invalid_payload");
        Invalid("{\"config\":{},\"config\":{},\"players\":[]}", 25, "invalid_payload");
        Invalid("{\"config\":{},\"players\":[],\"Players\":[]}", 25, "invalid_payload");
        Invalid("{\"config\":{},\"players\":[{\"name\":\"" + new string('x', 101) + "\"}]}",
            25, "field_size_limit");
        Invalid("{\"config\":{},\"players\":[" + Players(25) + "],\"teams\":[{\"players\":[{\"key\":\"extra\"}]}]}",
            25, "generation_players_limit");
        using (var allowed = JsonDocument.Parse("{\"config\":{},\"players\":[" + Players(25)
            + "],\"teams\":[{\"players\":[{\"key\":\"p0\"}]}]}"))
            RuntimePayloadGuard.ValidateGeneration(allowed.RootElement, options, 25);
        check(true, "locked roster keys do not count twice");

        var firstCaller = Context("Teams", "Post", "{}");
        firstCaller.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.1");
        var key = RuntimeCapacityMiddleware.CallerKey(firstCaller);
        firstCaller.Request.QueryString = new QueryString("?ownerId=spoof&uid=other");
        firstCaller.Request.Headers["X-Forwarded-For"] = "198.51.100.1";
        check(RuntimeCapacityMiddleware.CallerKey(firstCaller) == key, "UID and forwarded headers cannot rotate rate keys");
        firstCaller.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("user_id", "verified") }, "test"));
        check(RuntimeCapacityMiddleware.CallerKey(firstCaller) == "user:verified", "verified subject is the caller key");

        var clock = new Clock();
        var table = new MemoryTable();
        var transport = new LeaseTransport();
        var blobOptions = new BlobClientOptions { Transport = transport };
        blobOptions.Retry.MaxRetries = 0;
        var container = new BlobContainerClient(new Uri("https://capacity.invalid/runtime"), blobOptions);
        RuntimeCapacityState State() => new(table, container, clock, NullLogger<RuntimeCapacityState>.Instance);
        var stateA = State();
        var stateB = State();
        var accepted = 0;
        await Task.WhenAll(Enumerable.Range(0, 24).Select(async index =>
        {
            try
            {
                await (index % 2 == 0 ? stateA : stateB).ConsumeAsync("generation:caller", 20, "rate", default);
                Interlocked.Increment(ref accepted);
            }
            catch (CapacityException exception) { check(exception.Code == "rate", "rolling rate rejection code"); }
        }));
        check(accepted == 20, "two workers share a conditional twenty-request rolling rate counter");
        clock.Now = clock.Now.AddSeconds(59);
        try
        {
            await stateB.ConsumeAsync("generation:caller", 20, "rate", default);
            check(false, "rolling rate remains full");
        }
        catch (CapacityException exception) { check(exception.RetryAfterSeconds == 1, "rolling rate retry time"); }
        clock.Now = clock.Now.AddSeconds(1);
        await stateB.ConsumeAsync("generation:caller", 20, "rate", default);
        check(true, "rolling rate recovers after sixty seconds without a process restart");
        var leaseA = await stateA.AcquireAsync("global", 2, "concurrent", default);
        var leaseB = await stateB.AcquireAsync("global", 2, "concurrent", default);
        try
        {
            await stateB.AcquireAsync("global", 2, "concurrent", default);
            check(false, "global concurrency rejects a third operation");
        }
        catch (CapacityException exception) { check(exception.Code == "concurrent", "global lease shared across workers"); }
        await leaseA.DisposeAsync();
        await using (await stateB.AcquireAsync("global", 2, "concurrent", default)) { }
        await leaseB.DisposeAsync();
        check(transport.ActiveLeases == 0, "distributed slots are reusable and released");
        await using (await stateA.AcquireAsync("caller", 1, "caller_concurrency", default))
        {
            try
            {
                await stateB.AcquireAsync("caller", 1, "caller_concurrency", default);
                check(false, "one concurrent generation per caller");
            }
            catch (CapacityException exception)
            {
                check(exception.Code == "caller_concurrency", "caller lease spans application instances");
            }
        }

        var telemetry = new Telemetry();
        var fakeState = new StateStub();
        var entitlement = new Entitlements();
        var visitCalled = false;
        var visit = Context("WebsiteVisits", "Post", "{}");
        await new RuntimeCapacityMiddleware(_ => { visitCalled = true; return Task.CompletedTask; }, Options.Create(options))
            .InvokeAsync(visit, fakeState, entitlement, telemetry);
        check(visitCalled && fakeState.Rates.Count == 0 && fakeState.Acquisitions.Count == 0,
            "anonymous website visits cannot consume shared application write or generation capacity");
        foreach (var chunkedVisit in new[] { false, true })
        {
            var largeVisit = Context("WebsiteVisits", "Post", "{\"source\":\"" + new string('x', 2100) + "\"}");
            if (chunkedVisit)
            {
                largeVisit.Request.ContentLength = null;
                largeVisit.Request.Headers["Transfer-Encoding"] = "chunked";
            }
            visitCalled = false;
            await new RuntimeCapacityMiddleware(_ => { visitCalled = true; return Task.CompletedTask; }, Options.Create(options))
                .InvokeAsync(largeVisit, fakeState, entitlement, telemetry);
            check(!visitCalled && largeVisit.Response.StatusCode == 413 && fakeState.Rates.Count == 0,
                "website body size is bounded before parsing, including chunked requests");
        }
        var called = false;
        var small = new CapacityOptions { MaximumRequestBytes = 128 };
        var chunked = Context("Config", "Upload", "{\"logo\":\"" + new string('x', 200) + "\"}");
        chunked.Request.ContentLength = null;
        chunked.Request.Headers["Transfer-Encoding"] = "chunked";
        await new RuntimeCapacityMiddleware(_ => { called = true; return Task.CompletedTask; }, Options.Create(small))
            .InvokeAsync(chunked, fakeState, entitlement, telemetry);
        check(chunked.Response.StatusCode == 413 && !called && fakeState.Rates.Count == 0,
            "chunked oversize payload rejected before model binding and state work");
        var deep = Context("Config", "Upload", new string('[', 33) + "0" + new string(']', 33));
        await new RuntimeCapacityMiddleware(_ => { called = true; return Task.CompletedTask; }, Options.Create(options))
            .InvokeAsync(deep, fakeState, entitlement, telemetry);
        check(deep.Response.StatusCode == 400 && !called, "JSON depth rejected before model binding");
        var free = Context("Teams", "Post", "{\"config\":{},\"players\":[" + Players(26) + "]}");
        await new RuntimeCapacityMiddleware(_ => { called = true; return Task.CompletedTask; }, Options.Create(options))
            .InvokeAsync(free, fakeState, entitlement, telemetry);
        check(free.Response.StatusCode == 413 && !called, "free player cap applies before MVC and core generation");
        var logo = Context("Config", "Upload", "{\"groupLogo\":\"" + Convert.ToBase64String(new byte[512 * 1024]) + "\"}");
        var logoAccepted = false;
        await new RuntimeCapacityMiddleware(_ => { logoAccepted = true; return Task.CompletedTask; }, Options.Create(options))
            .InvokeAsync(logo, fakeState, entitlement, telemetry);
        check(logoAccepted, "two MiB request limit allows the existing 512 KiB base64 logo");
        foreach (var operation in new[] { ("UserPlayers", "PostSharePlayers"), ("Teams", "GetAllTeamsDesign") })
        {
            var render = Context(operation.Item1, operation.Item2,
                "{\"players\":[" + Players(100) + "],\"teamInfo\":{\"teamName\":\"Weekly match\"}}");
            var renderCalled = false;
            var before = fakeState.Acquisitions.Count;
            await new RuntimeCapacityMiddleware(_ => { renderCalled = true; return Task.CompletedTask; }, Options.Create(options))
                .InvokeAsync(render, fakeState, entitlement, telemetry);
            check(renderCalled && fakeState.Acquisitions.Count == before + 1
                && fakeState.Acquisitions.Last() == "expensive:global",
                "full-roster rendering uses distributed expensive admission: " + operation);
        }
        foreach (var field in new[] { "teamName", "matchName", "location", "venue", "eventDate" })
        {
            var render = Context("UserPlayers", "PostSharePlayers",
                "{\"players\":[],\"teamInfo\":{\"" + field + "\":\"" + new string('x', 100000) + "\"}}");
            var renderCalled = false;
            var before = fakeState.Acquisitions.Count;
            await new RuntimeCapacityMiddleware(_ => { renderCalled = true; return Task.CompletedTask; }, Options.Create(options))
                .InvokeAsync(render, fakeState, entitlement, telemetry);
            check(!renderCalled && render.Response.StatusCode == 413
                && fakeState.Acquisitions.Count == before,
                "long renderer text rejected before admission: " + field);
        }
        var largeRender = Context("UserPlayers", "PostSharePlayers", "{\"players\":[" + Players(101) + "]}");
        await new RuntimeCapacityMiddleware(_ => throw new InvalidOperationException("oversized render admitted"),
            Options.Create(options)).InvokeAsync(largeRender, fakeState, entitlement, telemetry);
        check(largeRender.Response.StatusCode == 413, "player image roster limit is 100, not generation's 50");
        var history = Context("Teams", "GetAiSummary",
            "{\"matches\":[" + string.Join(",", Enumerable.Repeat("{}", 80)) + "]}");
        var historyCalled = false;
        await new RuntimeCapacityMiddleware(_ => { historyCalled = true; return Task.CompletedTask; }, Options.Create(options))
            .InvokeAsync(history, fakeState, entitlement, telemetry);
        check(historyCalled, "AI summary history is not capped by selected-player generation limits");
        var text = Context("Teams", "PostResultString", "{\"teams\":[{\"players\":[" + Players(60) + "]}]}");
        var textCalled = false;
        var beforeText = fakeState.Rates.Count + fakeState.Acquisitions.Count;
        await new RuntimeCapacityMiddleware(_ => { textCalled = true; return Task.CompletedTask; }, Options.Create(options))
            .InvokeAsync(text, fakeState, entitlement, telemetry);
        check(textCalled && beforeText == fakeState.Rates.Count + fakeState.Acquisitions.Count,
            "bounded text formatting does not consume generation or rendering quota");
        using (var renderLogo = JsonDocument.Parse("{\"teamInfo\":{\"groupLogo\":\""
            + Convert.ToBase64String(new byte[512 * 1024]) + "\"},\"teams\":[]}"))
            RuntimePayloadGuard.Validate(renderLogo.RootElement, options, true, "Teams.GetAllTeamsDesign");
        check(true, "non-text group logo retains its existing payload allowance");
        foreach (var operation in new[] { ("UserPlayers", "Upload"), ("UserPlayers", "Move"), ("UserPlayers", "Copy"),
                     ("UserPlayers", "CreateGroup"), ("Config", "Upload"), ("Teams", "SaveToStorage") })
        {
            var context = Context(operation.Item1, operation.Item2, "{}");
            var before = fakeState.Rates.Count;
            await new RuntimeCapacityMiddleware(_ => Task.CompletedTask, Options.Create(options))
                .InvokeAsync(context, fakeState, entitlement, telemetry);
            check(fakeState.Rates.Count == before + 2, operation + " is protected by caller and global write rates");
        }
        var summary = Context("Teams", "GetAiSummary", "{}");
        var beforeAi = fakeState.Rates.Count;
        await new RuntimeCapacityMiddleware(_ => Task.CompletedTask, Options.Create(options))
            .InvokeAsync(summary, fakeState, entitlement, telemetry);
        check(fakeState.Rates.Count == beforeAi && fakeState.Acquisitions.Last() == "expensive:global",
            "AI summary has shared expensive concurrency but no daily/minute AI quota");
        try
        {
            await new RuntimeCapacityMiddleware(_ => throw new InvalidOperationException("action failure"), Options.Create(options))
                .InvokeAsync(Context("Teams", "GetAiSummary", "{}"), fakeState, entitlement, telemetry);
            check(false, "action failure propagates");
        }
        catch (InvalidOperationException)
        {
            check(fakeState.Active == 0, "action exceptions do not leak distributed permits");
        }
        var health = new DefaultHttpContext();
        health.Request.Method = "GET";
        var healthCalled = false;
        var beforeHealth = fakeState.Rates.Count + fakeState.Acquisitions.Count;
        await new RuntimeCapacityMiddleware(_ => { healthCalled = true; return Task.CompletedTask; }, Options.Create(options))
            .InvokeAsync(health, fakeState, entitlement, telemetry);
        check(healthCalled && beforeHealth == fakeState.Rates.Count + fakeState.Acquisitions.Count,
            "public health is cheap and never consumes a rate or lease");
        var webhook = Context("Billing", "Webhook", "{\"event\":{\"type\":\"RENEWAL\"}}");
        var webhookBody = webhook.Request.Body;
        var webhookCalled = false;
        var beforeWebhook = fakeState.Rates.Count + fakeState.Acquisitions.Count;
        await new RuntimeCapacityMiddleware(context =>
        {
            webhookCalled = ReferenceEquals(context.Request.Body, webhookBody);
            return Task.CompletedTask;
        }, Options.Create(options)).InvokeAsync(webhook, fakeState, entitlement, telemetry);
        check(webhookCalled && beforeWebhook == fakeState.Rates.Count + fakeState.Acquisitions.Count,
            "provider webhook bypasses user capacity admission and preserves raw body for its own authentication/size guard");
        var playPush = Context("PlayBilling", "Notifications", "{\"message\":{\"data\":\"encoded\"}}");
        var playPushCalled = false;
        var beforePlayPush = fakeState.Rates.Count + fakeState.Acquisitions.Count;
        await new RuntimeCapacityMiddleware(_ => { playPushCalled = true; return Task.CompletedTask; }, Options.Create(options))
            .InvokeAsync(playPush, fakeState, entitlement, telemetry);
        check(playPushCalled && beforePlayPush == fakeState.Rates.Count + fakeState.Acquisitions.Count,
            "authenticated Google push has independent admission, not the user's write bucket");
        var canceled = Context("Teams", "GetAiSummary", "{}");
        await new RuntimeCapacityMiddleware(async context =>
            await Task.Delay(Timeout.Infinite, context.RequestAborted),
            Options.Create(new CapacityOptions { OperationTimeoutSeconds = 1 }))
            .InvokeAsync(canceled, fakeState, entitlement, telemetry);
        check(canceled.Response.StatusCode == 504 && fakeState.Active == 0, "timeout cancels real work and releases its lease");
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ignored = Context("Teams", "GetAiSummary", "{}");
        var running = new RuntimeCapacityMiddleware(async _ => { entered.SetResult(); await release.Task; },
            Options.Create(new CapacityOptions { OperationTimeoutSeconds = 1 }))
            .InvokeAsync(ignored, fakeState, entitlement, telemetry);
        await entered.Task;
        await Task.Delay(1100);
        check(!running.IsCompleted && fakeState.Active == 1, "timeout never abandons uncancelable work or frees its slot early");
        release.SetResult();
        await running;
        check(fakeState.Active == 0, "uncancelable work releases its slot only on completion");
        var storageFailure = Context("Teams", "SaveToStorage", "{}");
        await new RuntimeCapacityMiddleware(_ => throw new CapacityException(
            "storage_limit", "Storage limit reached.", 429, 25, 26, 7), Options.Create(options))
            .InvokeAsync(storageFailure, fakeState, entitlement, telemetry);
        check(storageFailure.Response.StatusCode == 429 && storageFailure.Response.Headers["Retry-After"] == "7"
            && ReadBody(storageFailure).Contains("\"success\":false")
            && ReadBody(storageFailure).Contains("\"code\":\"storage_limit\""),
            "storage capacity failures preserve useful JSON and actual HTTP status/Retry-After");
        check(telemetry.Events.All(item => item.Name == "CapacityRejected"
            && item.Properties.Keys.OrderBy(key => key).SequenceEqual(new[] { "code", "operation" })),
            "capacity rejection telemetry carries only code and bounded operation");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using (GenerationCancellation.Enter(cancellation.Token))
        {
            try { GenerationCancellation.ThrowIfCancellationRequested(); check(false, "algorithm cancellation"); }
            catch (OperationCanceledException) { check(true, "synchronous generation observes cooperative cancellation"); }
            var fit = typeof(TeamsDesignCreator.SkiaImageCreator).GetMethod("DrawFittedRosterText",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
            try
            {
                fit.Invoke(null, new object?[] { null, "Canceled rendering", default(SkiaSharp.SKRect),
                    0f, null, SkiaSharp.SKTextAlign.Left, 12f });
                check(false, "text renderer must observe cancellation before shaping or drawing");
            }
            catch (System.Reflection.TargetInvocationException exception)
                when (exception.InnerException is OperationCanceledException)
            {
                check(true, "text renderer observes cancellation before native rendering");
            }
        }
        check(!GenerationCancellation.Token.IsCancellationRequested, "generation cancellation restores prior scope");
    }

    private static DefaultHttpContext Context(string controller, string action, string body)
    {
        var context = new DefaultHttpContext();
        context.RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider();
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask,
            new EndpointMetadataCollection(new ControllerActionDescriptor { ControllerName = controller, ActionName = action }),
            controller + "." + action));
        context.Request.Method = "POST";
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        context.Request.ContentLength = context.Request.Body.Length;
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static string ReadBody(HttpContext context) =>
        Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray());

    private sealed class Clock : IRuntimeClock
    {
        internal DateTimeOffset Now = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class Entitlements : IAccountEntitlementService
    {
        public Task<AccountEntitlements> GetAsync(string userId, string? verifiedEmail = null,
            CancellationToken cancellationToken = default, bool? paidActive = null) =>
            Task.FromResult(new AccountEntitlements { IsPremium = paidActive == true });
    }

    private sealed class Telemetry : IUsageTelemetry
    {
        internal List<(string Name, IReadOnlyDictionary<string, string?> Properties)> Events = new();
        public void Track(string eventName, string? clientVersion = null, string? userId = null,
            IReadOnlyDictionary<string, string?>? properties = null, IReadOnlyDictionary<string, double>? measurements = null) =>
            Events.Add((eventName, properties!));
    }

    private sealed class StateStub : IRuntimeCapacityState
    {
        internal List<string> Rates = new();
        internal List<string> Acquisitions = new();
        internal int Active;
        public Task ConsumeAsync(string key, int limit, string code, CancellationToken cancellationToken)
        {
            Rates.Add(key);
            return Task.CompletedTask;
        }
        public Task<IRuntimeLease> AcquireAsync(string key, int slots, string code, CancellationToken cancellationToken)
        {
            Acquisitions.Add(key);
            Active++;
            return Task.FromResult<IRuntimeLease>(new Lease(this));
        }
        private sealed class Lease : IRuntimeLease
        {
            private readonly StateStub _owner;
            internal Lease(StateStub owner) => _owner = owner;
            public CancellationToken LeaseLost => default;
            public ValueTask DisposeAsync() { _owner.Active--; return ValueTask.CompletedTask; }
        }
    }

    private sealed class MemoryTable : TableClient
    {
        private readonly Dictionary<string, RuntimeRateEntity> _rows = new();
        private int _revision;
        public override Task<Response<TableItem>> CreateIfNotExistsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Response.FromValue<TableItem>(null!, new FakeResponse(201)));
        public override async Task<NullableResponse<T>> GetEntityIfExistsAsync<T>(string partitionKey, string rowKey,
            IEnumerable<string>? select = null, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            lock (_rows)
                return new OptionalResponse<T>(_rows.TryGetValue(rowKey, out var value) ? Clone(value) as T : null);
        }
        public override Task<Response> AddEntityAsync<T>(T entity, CancellationToken cancellationToken = default)
        {
            lock (_rows)
            {
                if (_rows.ContainsKey(entity.RowKey)) throw new RequestFailedException(409, "exists");
                return Store(entity);
            }
        }
        public override Task<Response> UpdateEntityAsync<T>(T entity, ETag ifMatch,
            TableUpdateMode mode = TableUpdateMode.Merge, CancellationToken cancellationToken = default)
        {
            lock (_rows)
            {
                if (!_rows.TryGetValue(entity.RowKey, out var current) || current.ETag != ifMatch)
                    throw new RequestFailedException(412, "revision");
                return Store(entity);
            }
        }
        private Task<Response> Store<T>(T entity) where T : ITableEntity
        {
            var copy = Clone((RuntimeRateEntity)(object)entity);
            copy.ETag = new ETag((++_revision).ToString());
            _rows[entity.RowKey] = copy;
            return Task.FromResult<Response>(new FakeResponse(204));
        }
        private static RuntimeRateEntity Clone(RuntimeRateEntity entity) => new()
        {
            PartitionKey = entity.PartitionKey, RowKey = entity.RowKey, Requests = entity.Requests, ETag = entity.ETag
        };
    }

    private sealed class OptionalResponse<T> : NullableResponse<T> where T : class
    {
        private readonly T? _value;
        internal OptionalResponse(T? value) => _value = value;
        public override bool HasValue => _value != null;
        public override T Value => _value!;
        public override Response GetRawResponse() => new FakeResponse(200);
    }

    private sealed class LeaseTransport : HttpPipelineTransport
    {
        private readonly HashSet<string> _blobs = new();
        private readonly Dictionary<string, string> _leases = new();
        internal int ActiveLeases => _leases.Count;
        public override Request CreateRequest() => HttpClientTransport.Shared.CreateRequest();
        public override void Process(HttpMessage message)
        {
            lock (_blobs)
            {
                var uri = message.Request.Uri.ToUri();
                var key = uri.AbsolutePath;
                if (uri.Query.Contains("restype=container"))
                    message.Response = new FakeResponse(201);
                else if (uri.Query.Contains("comp=lease"))
                {
                    message.Request.Headers.TryGetValue("x-ms-lease-action", out var action);
                    if (action == "acquire")
                    {
                        if (_leases.ContainsKey(key))
                            message.Response = new FakeResponse(409, "LeaseAlreadyPresent");
                        else
                        {
                            var id = Guid.NewGuid().ToString();
                            _leases[key] = id;
                            message.Response = new FakeResponse(201, leaseId: id);
                        }
                    }
                    else if (action == "release")
                    {
                        _leases.Remove(key);
                        message.Response = new FakeResponse(200);
                    }
                    else message.Response = new FakeResponse(200, leaseId: _leases[key]);
                }
                else message.Response = _blobs.Add(key)
                    ? new FakeResponse(201) : new FakeResponse(412, "ConditionNotMet");
            }
        }
        public override ValueTask ProcessAsync(HttpMessage message) { Process(message); return ValueTask.CompletedTask; }
    }

    private sealed class FakeResponse : Response
    {
        private readonly Dictionary<string, string> _headers = new(StringComparer.OrdinalIgnoreCase);
        internal FakeResponse(int status, string? error = null, string? leaseId = null)
        {
            Status = status;
            _headers["Date"] = DateTimeOffset.UtcNow.ToString("R");
            _headers["ETag"] = "\"revision\"";
            _headers["Last-Modified"] = DateTimeOffset.UtcNow.ToString("R");
            if (error != null) _headers["x-ms-error-code"] = error;
            if (leaseId != null) _headers["x-ms-lease-id"] = leaseId;
        }
        public override int Status { get; }
        public override string ReasonPhrase => "Fake";
        public override Stream? ContentStream { get; set; }
        public override string ClientRequestId { get; set; } = "";
        public override void Dispose() { }
        protected override bool ContainsHeader(string name) => _headers.ContainsKey(name);
        protected override IEnumerable<HttpHeader> EnumerateHeaders() =>
            _headers.Select(item => new HttpHeader(item.Key, item.Value));
        protected override bool TryGetHeader(string name, out string value) => _headers.TryGetValue(name, out value!);
        protected override bool TryGetHeaderValues(string name, out IEnumerable<string> values)
        {
            values = _headers.TryGetValue(name, out var value) ? new[] { value } : Array.Empty<string>();
            return _headers.ContainsKey(name);
        }
    }
}
