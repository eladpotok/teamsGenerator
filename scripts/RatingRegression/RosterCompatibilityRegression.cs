using System.Net;
using System.Security.Claims;
using System.Text;
using Azure.Core.Pipeline;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TeamsGenerator.API;
using TeamsGenerator.Orchestration;
using TeamsGeneratorWebAPI.Collaboration;
using TeamsGeneratorWebAPI.Controllers;
using TeamsGeneratorWebAPI.PlayersBlob;
using TeamsGeneratorWebAPI.Premium;
using TeamsGeneratorWebAPI.Telemetry;

internal static class RosterCompatibilityRegression
{
    internal static async Task Run(Action<bool, string> check)
    {
        WebAppAPI.Init();
        foreach (var algorithm in Enum.GetValues<AlgoType>())
        {
            using var fixture = new Fixture((int)algorithm);
            var legacy = new JArray(Player("old"), Player("remove"));
            fixture.Blobs.Replace(legacy);
            var edited = new JArray(Player("old"), Player("added"));
            edited[0]["isArrived"] = true;
            check(await fixture.Save(edited) is OkObjectResult { Value: SavePlayersResponse { Success: true } }
                && fixture.Blobs.Document is JArray
                && JToken.DeepEquals(fixture.Blobs.Document, JToken.Parse(edited.ToString(Formatting.None))),
                $"{algorithm}: production-shaped unversioned array preserves legacy edit/add/delete/attendance behavior");

            var preApprovalRevision = fixture.Blobs.ETag;
            var enrolled = RosterDocument.WithReceipt(fixture.Blobs.Document,
                new JArray(edited.Select(player => player.DeepClone()).Append(Player("join_request"))), "request");
            enrolled["serverMetadata"] = new JObject { ["keep"] = true };
            fixture.Blobs.Replace(enrolled);
            var read = (OkObjectResult)await fixture.Controller.Get("published-client", "owner", (int)algorithm);
            var json = JObject.FromObject(read.Value!);
            check(json["success"]!.Value<bool>() && json["players"] is JArray { Count: 3 }
                && json["revision"]!.Value<string>() == fixture.Blobs.ETag
                && json["groupJoinReceipts"] == null && json["serverMetadata"] == null,
                $"{algorithm}: old GET response remains a player array, never the storage envelope");
            var current = (JArray)json["players"]!.DeepClone();
            var member = current.OfType<JObject>().Single(player => player["key"]!.Value<string>() == "join_request");
            member["name"] = "Edited member";
            member["isArrived"] = true;
            member["isLocked"] = true;
            var priorRevision = fixture.Blobs.ETag;
            check(await fixture.Save(current) is OkObjectResult { Value: SavePlayersResponse { Success: true, Revision: not null } }
                && fixture.Blobs.ETag != priorRevision
                && RosterDocument.HasReceipt(fixture.Blobs.Document, "request")
                && fixture.Blobs.Document!["serverMetadata"]!["keep"]!.Value<bool>()
                && RosterDocument.Players(fixture.Blobs.Document).Single(player => Key(player) == "join_request")["isArrived"]!.Value<bool>(),
                $"{algorithm}: legacy edits/attendance after approval keep receipts and server metadata");

            await Conflict(edited, null, "legacy pre-approval offline snapshot cannot erase the new member");
            await Conflict(edited, preApprovalRevision, "explicit pre-approval revision cannot erase the new member");
            await Conflict(current, priorRevision, "versioned client cannot overwrite a later successful legacy edit");
            await Conflict(new JArray(current.Where(player => Key(player) != "join_request")), null,
                "old intentional member deletion is explicit failure, not a false successful merge");
            await Conflict(new JArray(current.Select(player => player.DeepClone()).Append(member.DeepClone())), null,
                "legacy duplicate membership keys cannot invalidate the account binding");

            var ordinaryChange = new JArray(current.Where(player => Key(player) != "old").Select(player => player.DeepClone()));
            ordinaryChange.Add(Player("another"));
            check(await fixture.Save(ordinaryChange) is OkObjectResult { Value: SavePlayersResponse { Success: true } }
                && RosterDocument.Players(fixture.Blobs.Document).All(player => Key(player) != "old"),
                $"{algorithm}: legacy ordinary-player deletion and addition still work on enrolled rosters");
            check(await fixture.Save(new JArray(), fixture.Blobs.ETag) is OkObjectResult { Value: SavePlayersResponse { Success: true } }
                && RosterDocument.Players(fixture.Blobs.Document).Count == 0
                && RosterDocument.HasReceipt(fixture.Blobs.Document, "request"),
                $"{algorithm}: fresh versioned deletion succeeds without erasing the receipt");
            await Conflict(current, null, "legacy offline snapshot cannot resurrect a deliberately removed membership");
            check(await fixture.Save(new JArray(Player("ordinary"))) is OkObjectResult { Value: SavePlayersResponse { Success: true } }
                && RosterDocument.HasReceipt(fixture.Blobs.Document, "request"),
                $"{algorithm}: retained receipts do not permanently lock out old clients after deletion");

            async Task Conflict(JArray players, string? revision, string reason)
            {
                var before = fixture.Blobs.Document!.ToString(Formatting.None);
                var writes = fixture.Blobs.Writes;
                var assessments = fixture.AssessmentWrites;
                var result = await fixture.Save(players, revision);
                check(result is ConflictObjectResult { Value: SavePlayersResponse { Success: false, Conflict: true, Error: not null } }
                    && fixture.Blobs.Document.ToString(Formatting.None) == before
                    && fixture.Blobs.Writes == writes && fixture.AssessmentWrites == assessments,
                    $"{algorithm}: {reason}; no roster or private-assessment write");
            }
        }

        using (var fixture = new Fixture((int)AlgoType.SkillWise))
        {
            var missing = (OkObjectResult)await fixture.Controller.Get("legacy", "owner", (int)AlgoType.SkillWise);
            check(missing.Value is GetPlayersResponse { Success: false, Revision: RosterDocument.MissingRevision },
                "missing-roster legacy response and new-client missing revision remain compatible");
            check(await fixture.Save(new JArray(Player("first")), RosterDocument.MissingRevision)
                    is OkObjectResult { Value: SavePlayersResponse { Success: true } }
                && fixture.Blobs.LastCondition == "*",
                "new-client first save uses If-None-Match, not an unconditional overwrite");
            check(await fixture.Save(new JArray(), RosterDocument.MissingRevision) is ConflictObjectResult,
                "missing revision cannot overwrite a newly created roster");
            fixture.Blobs.Replace(null);
            check(await fixture.Save(new JArray(Player("legacy-first"))) is OkObjectResult
                && fixture.Blobs.LastCondition == "*",
                "legacy first save still creates a plain array conditionally");

            var stale = new JArray(Player("legacy-first"));
            fixture.Blobs.BeforeUpload = () => fixture.Blobs.Replace(
                RosterDocument.WithReceipt(fixture.Blobs.Document,
                    new JArray(Player("legacy-first"), Player("join_race")), "race"));
            var assessments = fixture.AssessmentWrites;
            check(await fixture.Save(stale) is ConflictObjectResult
                && RosterDocument.HasReceipt(fixture.Blobs.Document, "race")
                && RosterDocument.Players(fixture.Blobs.Document).Count == 2
                && fixture.AssessmentWrites == assessments,
                "approval between legacy read and upload wins storage CAS without losing the member");
            check(fixture.Blobs.LastCondition != null && fixture.Blobs.LastCondition != "*",
                "legacy replacement uses If-Match even though the caller sends no revision");
        }

        using (var fixture = new Fixture((int)AlgoType.SkillWise, shared: true))
        {
            var ownerPlayer = Player("join_shared");
            ownerPlayer["attack"] = 8;
            fixture.Blobs.Replace(RosterDocument.WithReceipt(null, new JArray(ownerPlayer), "shared"));
            var shared = new JArray(new JObject
            {
                ["key"] = "join_shared", ["name"] = "Shared member", ["isArrived"] = true, ["attack"] = 1
            });
            check(await fixture.Save(shared) is OkObjectResult { Value: SavePlayersResponse { Success: true } }
                && RosterDocument.Players(fixture.Blobs.Document)[0].Value<double>("Attack") == 8
                && RosterDocument.HasReceipt(fixture.Blobs.Document, "shared"),
                "old co-organizer can update attendance without overwriting owner ratings or receipts");
            var writes = fixture.Blobs.Writes;
            var assessments = fixture.AssessmentWrites;
            check(await fixture.Save(new JArray()) is ConflictObjectResult
                && fixture.Blobs.Writes == writes && fixture.AssessmentWrites == assessments,
                "server-side shared merge must not promote an unversioned request past the membership guard");
            fixture.Blobs.AfterDownload = () =>
            {
                var updated = fixture.Blobs.Document!.DeepClone();
                RosterDocument.Players(updated)[0]["Attack"] = 9;
                fixture.Blobs.Replace(updated);
            };
            check(await fixture.Save(shared) is ConflictObjectResult
                && RosterDocument.Players(fixture.Blobs.Document)[0].Value<double>("Attack") == 9
                && fixture.Blobs.Writes == writes && fixture.AssessmentWrites == assessments,
                "owner edit during legacy shared merge cannot be overwritten by an earlier private-field snapshot");
        }
    }

    private static string? Key(JToken player) =>
        (player as JObject)?.GetValue("key", StringComparison.OrdinalIgnoreCase)?.Value<string>();

    private static JObject Player(string key) => new()
    {
        ["key"] = key, ["id"] = key, ["name"] = key, ["rank"] = 6, ["attack"] = 6,
        ["defence"] = 6, ["passing"] = 6, ["leadership"] = 6, ["stamina"] = 6,
        ["positions"] = new JArray(0), ["isGoalKeeper"] = false, ["isArrived"] = false,
        ["isLocked"] = false, ["waitingListOrder"] = null, ["modifyTime"] = "2026-10-17T00:00:00Z"
    };

    private sealed class Fixture : IDisposable
    {
        internal BlobHandler Blobs { get; } = new();
        internal UserPlayersController Controller { get; }
        internal int AssessmentWrites { get; private set; }
        private readonly HttpClient client;
        private readonly int algorithm;
        private readonly string caller;
        private readonly string group;

        internal Fixture(int algorithm, bool shared = false)
        {
            this.algorithm = algorithm;
            caller = shared ? "collaborator" : "owner";
            group = shared ? "shared.owner.default" : "default";
            client = new HttpClient(Blobs);
            var options = new BlobClientOptions { Transport = new HttpClientTransport(client) };
            options.Retry.MaxRetries = 0;
            var container = new BlobContainerClient(new Uri("https://roster.invalid/players"), options);
            var assessments = CallbackProxy.Create<IPlayerAssessmentService>((method, args) =>
            {
                if (method.Name == "ApplyAsync") return Task.FromResult((JArray)args[3]!);
                if (method.Name != "SaveAsync") throw new InvalidOperationException(method.Name);
                AssessmentWrites++;
                return Task.CompletedTask;
            });
            var storage = new PlayersStorageBlobConnector(container, NullLogger<PlayersStorageBlobConnector>.Instance, assessments);
            var collaboration = CallbackProxy.Create<IGroupCollaborationService>((method, _) =>
                method.Name == "ResolveAccessAsync"
                    ? Task.FromResult<GroupAccess?>(new GroupAccess { OwnerId = "owner", GroupId = "default", IsOwner = !shared })
                    : throw new InvalidOperationException(method.Name));
            var entitlements = CallbackProxy.Create<IAccountEntitlementService>((_, _) =>
                Task.FromResult(new AccountEntitlements { IsPremium = true }));
            Controller = new UserPlayersController(NullLogger<UserPlayersController>.Instance, storage,
                CallbackProxy.Create<IUsageTelemetry>((_, _) => null), entitlements, collaboration, assessments, null!)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("user_id", caller) }, "test"))
                    }
                }
            };
        }

        internal Task<IActionResult> Save(JArray players, string? revision = null)
        {
            Controller.Request.Headers.Remove("X-Roster-Revision");
            if (revision != null) Controller.Request.Headers["X-Roster-Revision"] = revision;
            return Controller.Post("published-client", players, caller, algorithm, group);
        }

        public void Dispose() => client.Dispose();
    }

    private sealed class BlobHandler : HttpMessageHandler
    {
        internal JToken? Document { get; private set; }
        internal string ETag => $"\"roster-{revision}\"";
        internal int Writes { get; private set; }
        internal string? LastCondition { get; private set; }
        internal Action? BeforeUpload { get; set; }
        internal Action? AfterDownload { get; set; }
        private int revision;

        internal void Replace(JToken? document)
        {
            Document = document?.DeepClone();
            revision++;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (request.RequestUri!.Host != "roster.invalid" || !request.RequestUri.AbsolutePath.StartsWith("/players/"))
                throw new InvalidOperationException("Unexpected storage scope.");
            if (request.Method == HttpMethod.Get || request.Method == HttpMethod.Head)
            {
                if (Document == null) return Error(HttpStatusCode.NotFound, "BlobNotFound");
                var response = Response(HttpStatusCode.OK, Document.ToString(Formatting.None));
                if (request.Method == HttpMethod.Get)
                {
                    var callback = AfterDownload;
                    AfterDownload = null;
                    callback?.Invoke();
                }
                return response;
            }
            if (request.Method != HttpMethod.Put)
                throw new InvalidOperationException("Unexpected storage operation: " + request.Method);
            var before = BeforeUpload;
            BeforeUpload = null;
            before?.Invoke();
            var match = request.Headers.IfMatch.SingleOrDefault()?.ToString();
            var absent = request.Headers.IfNoneMatch.SingleOrDefault()?.ToString();
            LastCondition = match ?? absent;
            if (match == null && absent != "*") throw new InvalidOperationException("Unconditional roster write.");
            if (match != null ? Document == null || match != ETag : Document != null)
                return Error(HttpStatusCode.PreconditionFailed, "ConditionNotMet");
            Replace(JToken.Parse(await request.Content!.ReadAsStringAsync(ct)));
            Writes++;
            return Response(HttpStatusCode.Created, "");
        }

        private HttpResponseMessage Response(HttpStatusCode status, string body)
        {
            var response = new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue(ETag);
            response.Content.Headers.LastModified = DateTimeOffset.UnixEpoch;
            response.Headers.TryAddWithoutValidation("x-ms-blob-type", "BlockBlob");
            return response;
        }

        private static HttpResponseMessage Error(HttpStatusCode status, string code)
        {
            var response = new HttpResponseMessage(status)
            {
                Content = new StringContent($"<Error><Code>{code}</Code><Message>Test storage conflict</Message></Error>",
                    Encoding.UTF8, "application/xml")
            };
            response.Headers.TryAddWithoutValidation("x-ms-error-code", code);
            return response;
        }
    }
}
