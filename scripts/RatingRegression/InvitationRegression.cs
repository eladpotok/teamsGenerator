using System.Security.Claims;
using System.Net;
using System.Text;
using Azure.Core.Pipeline;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TeamsGeneratorWebAPI.Controllers;
using TeamsGeneratorWebAPI.Invitations;
using TeamsGeneratorWebAPI.Collaboration;
using TeamsGeneratorWebAPI.ConfigBlob;
using TeamsGeneratorWebAPI.PlayersBlob;
using TeamsGenerator.Algos.SkillWiseAlgo;
using TeamsGenerator.API;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using TeamsGeneratorWebAPI.Premium;
using TeamsGeneratorWebAPI.Telemetry;
using TeamsGeneratorWebAPI.PlayerProfiles;
using TeamsGeneratorWebAPI.GroupJoins;

internal static class InvitationRegression
{
    public static async Task Run(Action<bool, string> check)
    {
        await DocumentPagination(check);
        await EventDuration(check);
        var ct = CancellationToken.None;
        var clock = new Clock();
        var store = new MemoryStore();
        var groups = new Groups();
        var service = new InvitationService(store, groups, clock);
        var accounts = new NotificationAccountService(store, clock);
        var sender = new Sender();
        var processor = new InvitationDigestProcessor(store, groups, accounts, sender, clock);
        var at = clock.UtcNow.AddDays(7).ToString("O");
        var request = new CreateInvitationRequest("group", at, "Asia/Jerusalem");
        var invite = await service.CreateAsync("owner", request, ct);
        var key = InvitationKeys.State(invite.Id);
        check(invite.Revision == 0 && invite.Capacity == 2 && invite.Token.Length == 43, "invitation starts empty with strong capability");
        check((await service.CreateAsync("owner", request, ct)).Token == invite.Token, "resharing keeps token");
        check((await service.CreateAsync("collaborator", request with { GroupId = "shared" }, ct)).Id == invite.Id,
            "canonical owner/group occurrence deduplicates collaborator share");
        check((await service.CurrentAsync("owner", "group", at, ct))?.Id == invite.Id, "organizer current finds occurrence");
        check(await service.CurrentAsync("other", "group", at, ct) == null, "current never guesses another owner's occurrence");
        check((await service.GetForRecipientAsync("owner", invite.Id, ct)).Token == invite.Token,
            "notification recipient loads exact organizer invitation by ID");
        await Error(() => service.GetForRecipientAsync("other", invite.Id, ct), 404, check);
        await Error(() => service.GetForRecipientAsync("collaborator", invite.Id, ct), 404, check);
        await Error(() => service.GetForRecipientAsync("owner", "../group", ct), 404, check);
        await Error(() => service.GetForRecipientAsync("owner", new string('a', 64), ct), 404, check);
        groups.DenyAccess = true;
        await Error(() => service.GetForRecipientAsync("owner", invite.Id, ct), 403, check);
        groups.DenyAccess = false;
        var next = await service.CreateAsync("owner", request with { ScheduledAt = clock.UtcNow.AddDays(14).ToString("O") }, ct);
        check(next.Id != invite.Id && next.Token != invite.Token, "next week has a different occurrence and capability");
        await Error(() => service.CreateAsync("owner", request with { ScheduledAt = clock.UtcNow.ToString("O") }, ct), 400, check);
        await Error(() => service.CreateAsync("owner", request with { TimeZone = "Pacific Standard Time" }, ct), 400, check);
        await Error(() => service.CreateAsync("owner", request with { ScheduledAt = "2026-10-01T10:00:00+03:00" }, ct), 400, check);
        await Error(() => service.CreateAsync("stranger", request with { GroupId = "shared" }, ct), 403, check);
        await Error(() => service.CreateAsync("owner", request with { GroupId = "missing" }, ct), 404, check);
        await Error(() => service.PublicAsync(new string('x', 43), ct), 404, check);

        var initialOut = await service.RespondAsync(invite.Token, new("p1", "out"), ct);
        var state = await State();
        check(initialOut.Revision == 0 && initialOut.Players[0].Status == "unanswered"
            && state.Events.Count == 0 && state.PendingSince == null, "initial Out has no mutation, event, or notification");
        await Error(() => service.RespondAsync(invite.Token, new("unknown", "in"), ct), 400, check);
        await Error(() => service.RespondAsync(invite.Token, new("p1", "yes"), ct), 400, check);
        await service.RespondAsync(invite.Token, new("p1", "in"), ct);
        var firstPending = (await State()).PendingSince;
        clock.UtcNow = clock.UtcNow.AddMinutes(5);
        var duplicate = await service.RespondAsync(invite.Token, new("p1", "in"), ct);
        check(duplicate.Revision == 1 && (await State()).Events.Count == 1, "duplicate In is idempotent");
        await service.RespondAsync(invite.Token, new("p2", "in"), ct);
        var full = await service.RespondAsync(invite.Token, new("p3", "in"), ct);
        check(full.Counts == new InvitationCounts(2, 1, 0) && full.Players.Single(p => p.Key == "p3").WaitingListPosition == 1,
            "capacity selects first respondents and creates waiting list");
        check(full.Players.Take(2).Select(p => p.Name).Distinct().Count() == 1,
            "duplicate display names remain independent real player keys");
        check((await State()).PendingSince == firstPending, "later response does not slide deadline");
        var withdrawn = await service.RespondAsync(invite.Token, new("p1", "out"), ct);
        check(withdrawn.Counts == new InvitationCounts(2, 0, 1) && withdrawn.Players.Single(p => p.Key == "p3").WaitingListPosition == null,
            "withdrawal immediately promotes first waiting player");
        var rejoined = await service.RespondAsync(invite.Token, new("p1", "in"), ct);
        check(rejoined.Players.Single(p => p.Key == "p1").WaitingListPosition == 1, "rejoin goes to the back of response order");
        var publicJson = JObject.FromObject(await service.PublicAsync(invite.Token, ct));
        check(!publicJson.ContainsKey("Token") && !publicJson.ContainsKey("OwnerId") && !publicJson.ContainsKey("GroupId")
            && publicJson["Players"]![0]!.Children<JProperty>().Select(p => p.Name).OrderBy(x => x)
                .SequenceEqual(new[] { "Key", "Name", "Status", "WaitingListPosition" }),
            "public projection is an explicit private-field-free whitelist");
        groups.Players.RemoveAll(p => p.Key == "p2");
        var filtered = await service.PublicAsync(invite.Token, ct);
        check(filtered.Players.All(p => p.Key != "p2") && filtered.Counts.In == 2 && filtered.Counts.Waiting == 0,
            "deleted roster player disappears and frees capacity");
        await Error(() => service.RespondAsync(invite.Token, new("p2", "in"), ct), 400, check);
        groups.Players.Insert(1, new("p2", "Same name"));

        check((await accounts.GetAsync("owner", ct)).Preferences == new NotificationPreferences(true, 10, 360),
            "notifications default to 10 distinct players or 6 hours");
        var notifications = new NotificationsController(accounts, sender)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext
                { User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("user_id", "owner") }, "test")) } }
        };
        sender.Configured = false;
        check((await notifications.Preferences(ct)).Result is OkObjectResult
            { Value: NotificationPreferencesResponse { Enabled: true, PlayerThreshold: 10, IntervalMinutes: 360, DeliveryConfigured: false } },
            "GET preferences explicitly distinguishes unavailable server delivery from enabled preferences");
        sender.Configured = true;
        check((await notifications.Preferences(ct)).Result is OkObjectResult
            { Value: NotificationPreferencesResponse { DeliveryConfigured: true } },
            "GET preferences reports independently configured server delivery");
        check((await notifications.SetPreferences(new(true, 10, 360), ct)).Result is OkObjectResult
            { Value: NotificationPreferences }, "PUT remains the three-field writable preferences contract");
        await Error(() => accounts.SetPreferencesAsync("owner", new(true, 0, 360), ct), 400, check);
        await Error(() => accounts.SetPreferencesAsync("owner", new(true, 10, 59), ct), 400, check);
        await accounts.RegisterAsync("owner", new("test-device", "android"), ct);
        sender.Configured = false;
        check((await notifications.Preferences(ct)).Result is OkObjectResult
            { Value: NotificationPreferencesResponse { DeliveryConfigured: false } },
            "device registration cannot make unprovisioned server delivery appear configured");
        sender.Configured = true;
        await accounts.RemoveAsync("other", "test-device", ct);
        check((await accounts.GetAsync("owner", ct)).Devices.Count == 1, "device removal is private to JWT user");
        await Error(() => accounts.RegisterAsync("owner", new("test-device", "ios"), ct), 400, check);
        await processor.ProcessAsync(key, ct);
        check(sender.Deliveries.Count == 0, "responses do not trigger an instant alert below threshold");
        clock.UtcNow = firstPending!.Value.AddHours(6).AddSeconds(-1);
        await processor.ProcessAsync(key, ct);
        check(sender.Deliveries.Count == 0, "deadline not early");
        clock.UtcNow = clock.UtcNow.AddSeconds(1);
        await processor.ProcessAsync(key, ct);
        check(sender.Deliveries.Count == 1 && sender.Deliveries[0].Summary == "3 joined, 0 withdrew; 2/2 places filled",
            "6-hour deadline produces one net-status digest");
        state = await State();
        check(state.BaselineRevision == state.Revision && state.PendingSince == null, "successful digest advances baseline");
        await service.RespondAsync(invite.Token, new("p1", "out"), ct);
        await service.RespondAsync(invite.Token, new("p1", "in"), ct);
        check((await State()).PendingSince == null, "withdraw and rejoin collapses against delivered baseline");
        await service.RespondAsync(invite.Token, new("p1", "out"), ct);
        await accounts.SetPreferencesAsync("owner", new(true, 1, 60), ct);
        await processor.ProcessAsync(key, ct);
        check(sender.Deliveries.Count == 1, "10-minute burst cooldown applies even at threshold");
        clock.UtcNow = clock.UtcNow.AddMinutes(10);
        await processor.ProcessAsync(key, ct);
        check(sender.Deliveries.Count == 2 && sender.Deliveries.Last().Summary.StartsWith("0 joined, 1 withdrew;"),
            "post-delivery In-to-Out queues withdrawal");

        // Failed sends survive restart, retain baseline, and recompute unsent net status.
        clock.UtcNow = clock.UtcNow.AddMinutes(10);
        await service.RespondAsync(invite.Token, new("p4", "in"), ct);
        sender.Result = PushResult.Retry;
        var beforeFailure = (await State()).BaselineRevision;
        await processor.ProcessAsync(key, ct);
        state = await State();
        check(state.Batch != null && state.RetryAt > clock.UtcNow && state.BaselineRevision == beforeFailure,
            "failed delivery persists outbox and retry without marking delivered");
        await service.RespondAsync(invite.Token, new("p4", "out"), ct);
        clock.UtcNow = state.RetryAt!.Value;
        sender.Result = PushResult.Delivered;
        processor = new(store, groups, accounts, sender, clock);
        await processor.ProcessAsync(key, ct);
        check(sender.Deliveries.Count == 3 && sender.Deliveries.Last().Summary.StartsWith("0 joined, 1 withdrew;"),
            "failed unsent join followed by Out sends the latest withdrawal rather than a stale join after restart");
        var afterWithdrawal = (await State()).BaselineRevision;
        await service.RespondAsync(invite.Token, new("p4", "in"), ct);
        sender.Enabled = false;
        await processor.ProcessAsync(key, ct);
        check((await State()).BaselineRevision == afterWithdrawal, "unconfigured sender never marks delivered");
        sender.Enabled = true;
        await accounts.SetPreferencesAsync("owner", new(false, 1, 60), ct);
        await processor.ProcessAsync(key, ct);
        state = await State();
        check(state.PendingSince == null && state.BaselineRevision == state.Revision && sender.Deliveries.Count == 3,
            "disabled preferences intentionally drain without sending or disabling RSVP");
        await accounts.SetPreferencesAsync("owner", new(true, 1, 60), ct);
        await processor.ProcessAsync(key, ct);
        check(sender.Deliveries.Count == 3, "reenabling does not replay disabled notifications");

        // Atomic ETag retries preserve independent concurrent answers.
        var concurrentAt = clock.UtcNow.AddDays(9).ToString("O");
        var concurrent = await service.CreateAsync("owner", request with { ScheduledAt = concurrentAt }, ct);
        store.YieldReads = true;
        await Task.WhenAll(Enumerable.Range(1, 8).Select(i =>
            service.RespondAsync(concurrent.Token, new("p" + i, "in"), ct)));
        store.YieldReads = false;
        var concurrentView = await service.PublicAsync(concurrent.Token, ct);
        check(concurrentView.Revision == 8 && concurrentView.Counts.In == 2 && concurrentView.Counts.Waiting == 6,
            "simultaneous distinct responses have no lost updates");
        store.FailNextWrites = 2;
        await service.RespondAsync(concurrent.Token, new("p9", "in"), ct);
        check((await service.PublicAsync(concurrent.Token, ct)).Revision == 9, "optimistic conflicts retry without duplicate revisions");
        var concurrentKey = InvitationKeys.State(concurrent.Id);
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        sender.BeforeSend = async () => { entered.TrySetResult(true); await release.Task; };
        var firstWorker = processor.ProcessAsync(concurrentKey, ct);
        await entered.Task;
        await new InvitationDigestProcessor(store, groups, accounts, sender, clock).ProcessAsync(concurrentKey, ct);
        release.TrySetResult(true);
        await firstWorker;
        sender.BeforeSend = null;
        check(sender.Deliveries.Count == 4, "multiple worker instances share a persisted exclusive digest lease");
        var leaseState = (await store.ReadAsync<InvitationState>(concurrentKey, ct))!.Value;
        check(leaseState.LeaseOwner == null && leaseState.Batch == null, "completed batch releases its lease");

        await service.RespondAsync(concurrent.Token, new("p9", "out"), ct);
        clock.UtcNow = clock.UtcNow.AddMinutes(10);
        sender.Result = PushResult.InvalidDevice;
        await processor.ProcessAsync(concurrentKey, ct);
        check((await accounts.GetAsync("owner", ct)).Devices.Count == 0, "unregistered FCM token is deleted privately");
        check((await store.ReadAsync<InvitationState>(concurrentKey, ct))!.Value.BaselineRevision == leaseState.BaselineRevision,
            "invalid device alone does not advance delivery baseline");

        using var limiter = new InvitationPublicRateLimiter();
        for (var i = 0; i < 40; i++) limiter.Check(invite.Token, "shared-household", null);
        for (var i = 0; i < 30; i++) limiter.Check(invite.Token, null, "p1");
        await Error(() => { limiter.Check(invite.Token, null, "p1"); return Task.CompletedTask; }, 429, check);
        limiter.Check(invite.Token, null, "p2");
        check(true, "public write throttling distinguishes players behind the same NAT");
        check(typeof(InvitationsController).IsDefined(typeof(AuthorizeAttribute), true)
            && typeof(NotificationsController).IsDefined(typeof(AuthorizeAttribute), true)
            && typeof(InvitationsController).GetMethod("Respond")!.IsDefined(typeof(AllowAnonymousAttribute), true),
            "organizer routes require JWT and public responses intentionally do not");
        var controller = new InvitationsController(service, limiter)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        await Error(() => controller.Create(request, ct), 401, check);
        await Error(() => controller.GetById(invite.Id, ct), 401, check);
        controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("user_id", "owner") }, "test"));
        check((await controller.Current("group", at, ct)) is OkObjectResult, "authenticated current controller uses verified identity");
        check((await controller.GetById(invite.Id, ct)).Result is OkObjectResult
            { Value: OrganizerInvitation detail } && detail.Id == invite.Id,
            "authenticated notification endpoint returns OrganizerInvitation directly");
        await service.RevokeAsync("owner", "group", concurrentAt, ct);
        await Error(() => service.PublicAsync(concurrent.Token, ct), 404, check);
        check(await service.CurrentAsync("owner", "group", concurrentAt, ct) == null, "revoked capability excluded from current");
        await Error(() => service.GetForRecipientAsync("owner", concurrent.Id, ct), 410, check);
        clock.UtcNow = invite.ScheduledAt;
        var closed = await service.PublicAsync(invite.Token, ct);
        check(closed.Status == "closed" && closed.ScheduledAt == invite.ScheduledAt && closed.Id != next.Id,
            "old share link stays on frozen closed occurrence");
        await Error(() => service.RespondAsync(invite.Token, new("p1", "in"), ct), 409, check);
        clock.UtcNow = invite.ScheduledAt.AddDays(31);
        await Error(() => service.PublicAsync(invite.Token, ct), 410, check);
        var historical = await service.GetForRecipientAsync("owner", invite.Id, ct);
        check(historical.Status == "closed" && historical.ScheduledAt == invite.ScheduledAt && historical.Id != next.Id
            && historical.Capacity == invite.Capacity,
            "old notification opens frozen original occurrence even after public capability expiry and later weeks");
        await DeliveryEdges(check);
        await UnsentWithdrawal(check);
        await DeviceOwnershipEdges(check);
        await ExistingStorageAdapter(check);
        await InvitationPolicy(check);

        async Task<InvitationState> State() => (await store.ReadAsync<InvitationState>(key, ct))!.Value;
    }

    internal static async Task DocumentPagination(Action<bool, string> check)
    {
        static string Page(string marker = "", params string[] keys) =>
            "<EnumerationResults><Blobs>" + string.Concat(keys.Select(key => $"<Blob><Name>{key}</Name></Blob>"))
            + $"</Blobs><NextMarker>{marker}</NextMarker></EnumerationResults>";
        using var handler = new BlobListHandler(Page(), Page(), Page("", "scope/a", "scope/b"),
            Page("next-a"), Page("next-b", "scope/a"), Page("", "scope/b"), Page(), Page());
        using var client = new HttpClient(handler);
        var options = new BlobClientOptions { Transport = new HttpClientTransport(client) };
        options.Retry.MaxRetries = 0;
        var container = new BlobContainerClient(new Uri("https://storage.invalid/players"), options);
        await foreach (var raw in container.GetBlobsAsync(prefix: "scope/").AsPages())
        {
            check(raw.Values.Count == 0 && raw.ContinuationToken == "",
                "real Blob SDK projects the final empty XML NextMarker as an empty string, not null");
            break;
        }
        var store = new BlobInvitationDocumentStore(container);
        var empty = await store.ListAsync("scope/", null, 100, CancellationToken.None);
        check(empty.Keys.Count == 0 && empty.Continuation == null,
            "document storage normalizes the empty Blob continuation so an empty account terminates");
        var final = await store.ListAsync("scope/", null, 100, CancellationToken.None);
        check(final.Keys.SequenceEqual(new[] { "scope/a", "scope/b" }) && final.Continuation == null,
            "a populated final Blob page retains its keys and terminates");
        var first = await store.ListAsync("scope/", null, 100, CancellationToken.None);
        var second = await store.ListAsync("scope/", first.Continuation, 100, CancellationToken.None);
        var third = await store.ListAsync("scope/", second.Continuation, 100, CancellationToken.None);
        check(first.Keys.Count == 0 && first.Continuation == "next-a"
            && second.Keys.SequenceEqual(new[] { "scope/a" }) && second.Continuation == "next-b"
            && third.Keys.SequenceEqual(new[] { "scope/b" }) && third.Continuation == null,
            "an empty intermediate page keeps its opaque marker and all subsequent pages remain discoverable");
        check(handler.Requests[4].Query.Contains("marker=next-a")
            && handler.Requests[5].Query.Contains("marker=next-b")
            && handler.Requests.Skip(1).Take(5).All(uri => uri.Query.Contains("maxresults=100")
                && uri.Query.Contains("prefix=scope%2F")),
            "continuations, account prefixes and page sizes reach the Blob SDK unchanged");
        var memberships = new PlayerMembershipService(store);
        check((await memberships.SummariesAsync("unlinked", new Groups(), CancellationToken.None)).Count == 0,
            "membership summaries terminate after one real SDK empty page");
        var performance = new PlayerPerformanceService(memberships,
            new PlayerPerformanceSource(new PollRegression.MemoryTables(), TimeProvider.System));
        check((await performance.GetAsync("unlinked", CancellationToken.None)).Count == 0
            && handler.Requests.Count == 8,
            "unlinked performance terminates after one real SDK empty page without restarting the listing");
    }

    private sealed class BlobListHandler : HttpMessageHandler
    {
        private readonly string[] pages;
        internal BlobListHandler(params string[] pages) => this.pages = pages;
        internal List<Uri> Requests { get; } = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (request.Method != HttpMethod.Get || !request.RequestUri!.Query.Contains("comp=list")
                || Requests.Count >= pages.Length)
                throw new InvalidOperationException("Unexpected or repeated Blob listing request.");
            Requests.Add(request.RequestUri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(pages[Requests.Count - 1], Encoding.UTF8, "application/xml")
            });
        }
    }

    private static async Task EventDuration(Action<bool, string> check)
    {
        foreach (var minutes in new int?[] { null, 1, 150, 1440 })
        {
            var rules = new MatchdayRules { EventDurationMinutes = minutes };
            check(rules.TryNormalize(out _), $"optional event duration {minutes} is valid");
            check(!rules.TimeLimitEnabled && rules.Minutes == 8, "event duration does not change per-game timing");
            var roundTrip = JsonConvert.DeserializeObject<MatchdayRules>(JsonConvert.SerializeObject(rules.Clone()))!;
            check(roundTrip.EventDurationMinutes == minutes, "event duration survives cloning and serialization");
        }
        foreach (var minutes in new[] { -1, 0, 1441 })
            check(!new MatchdayRules { EventDurationMinutes = minutes }.TryNormalize(out var error)
                && error.Contains("duration"), "invalid event duration is rejected");
        check(JsonConvert.DeserializeObject<MatchdayRules>("{}")!.EventDurationMinutes == null,
            "legacy rules leave optional duration unset");
        var clock = new Clock();
        var groups = new Groups { EventDurationMinutes = 150 };
        var service = new InvitationService(new MemoryStore(), groups, clock);
        var ct = CancellationToken.None;
        var request = new CreateInvitationRequest("group", clock.UtcNow.AddDays(1).ToString("O"), "Asia/Jerusalem");
        var created = await service.CreateAsync("owner", request, ct);
        check(created.EventDurationMinutes == 150
            && (await service.PublicAsync(created.Token, ct)).EventDurationMinutes == 150,
            "organizer and public invitation carry configured duration");
        groups.EventDurationMinutes = 75;
        check((await service.CreateAsync("owner", request, ct)).EventDurationMinutes == 150
            && (await service.CurrentAsync("owner", "group", request.ScheduledAt, ct))!.EventDurationMinutes == 150
            && (await service.PublicAsync(created.Token, ct)).EventDurationMinutes == 150,
            "resharing and changed group rules preserve saved occurrence duration");
        check((await service.CreateAsync("owner", request with { ScheduledAt = clock.UtcNow.AddDays(2).ToString("O") }, ct))
            .EventDurationMinutes == 75, "next occurrence uses updated rules");
        groups.EventDurationMinutes = 120;
        check((await service.CreateAsync("owner", request with { ScheduledAt = clock.UtcNow.AddDays(3).ToString("O") }, ct))
            .EventDurationMinutes == 120, "unset rules produce a two-hour occurrence");
        var legacy = JsonConvert.DeserializeObject<InvitationState>("{}")!;
        check(InvitationService.Project(legacy, Array.Empty<RosterPlayer>(), clock.UtcNow).EventDurationMinutes == 120,
            "legacy invitation documents project a two-hour duration without storage migration");
    }

    private static async Task ExistingStorageAdapter(Action<bool, string> check)
    {
        var empty = false;
        var inaccessible = false;
        var config = new UserConfigResponse
        {
            TeamName = "Configured match", Location = "Configured pitch", GroupLogoDataUrl = "data:image/png;base64,AA==",
            HasCustomMatchdayPlayerLimit = true, MaxMatchdayPlayers = 23, SelectedAlgoKey = 0,
            MatchdayRules = new MatchdayRules { EventDurationMinutes = 150 }
        };
        var collaboration = CallbackProxy.Create<IGroupCollaborationService>((method, args) =>
            method.Name == nameof(IGroupCollaborationService.ResolveAccessAsync)
                ? Task.FromResult<GroupAccess?>(inaccessible ? null : new GroupAccess
                    { OwnerId = "canonical-owner", GroupId = "group", IsOwner = false })
                : throw new InvalidOperationException("Unexpected collaboration mutation."));
        var players = CallbackProxy.Create<IPlayersStorageBlobConnector>((method, args) =>
        {
            if (method.Name == nameof(IPlayersStorageBlobConnector.ListGroupsAsync))
            {
                check((string)args[0]! == "canonical-owner", "real source checks canonical owner's group existence");
                return Task.FromResult(new PlayerGroupsResponse
                {
                    Success = true, Groups = new[] { new PlayerGroup { Id = "group", Name = "Configured group" } }
                });
            }
            if (method.Name == "ListAsync")
            {
                var scope = (PlayersBlobConfig)args[0]!;
                check(scope.UId == "canonical-owner" && scope.GroupId == "group" && scope.AlgoType == 0,
                    "real source scopes live roster to canonical group and configured algorithm");
                return Task.FromResult<IResponse>(empty ? GetPlayersResponse.Failure("Players were not found")
                    : new GetPlayersResponse(new[] { new SkillWisePlayer
                        { Key = "actual-key", Name = "Roster name", GoalkeeperSkill = 9, IsRecoveringFromInjury = true } }));
            }
            throw new InvalidOperationException("Invitation source must not mutate roster/history.");
        });
        var missingGroupConfig = false;
        var settings = CallbackProxy.Create<IUserConfigAzureStorage>((method, args) =>
            method.Name == "ListAsync" ? Task.FromResult<IResponse>(
                ((UserConfigBlobConfig)args[0]!).GroupId == "default"
                    ? new GetConfigResponse(new UserConfigResponse { MatchdayRules = new MatchdayRules { EventDurationMinutes = 240 } })
                    : missingGroupConfig ? GetConfigResponse.Missing() : new GetConfigResponse(config))
                : throw new InvalidOperationException("Invitation source must not mutate group config."));
        var entitlements = CallbackProxy.Create<IAccountEntitlementService>((method, args) =>
            Task.FromResult(new AccountEntitlements { IsPremium = true }));
        var source = new InvitationGroupSource(collaboration, players, settings, entitlements);
        var group = await source.ResolveAsync("collaborator", "shared.reference", CancellationToken.None);
        check(group.Capacity == 23 && group.Name == "Configured group" && group.MatchName == config.TeamName
            && group.Location == config.Location && group.Logo == config.GroupLogoDataUrl && group.EventDurationMinutes == 150
            && group.Players.Single() == new RosterPlayer("actual-key", "Roster name"),
            "existing storage adapter loads all configured metadata and only player identity");
        config.MatchdayRules.EventDurationMinutes = null;
        check((await source.LoadAsync("canonical-owner", "group", CancellationToken.None)).EventDurationMinutes == 120,
            "cleared group duration uses two hours instead of default group's duration");
        missingGroupConfig = true;
        check((await source.LoadAsync("canonical-owner", "group", CancellationToken.None)).EventDurationMinutes == 120,
            "group without config does not inherit another group's event duration");
        missingGroupConfig = false;
        empty = true;
        check((await source.LoadAsync("canonical-owner", "group", CancellationToken.None)).Players.Count == 0,
            "existing empty group is a legitimate zero-player invitation");
        inaccessible = true;
        await Error(() => source.ResolveAsync("stranger", "shared.reference", CancellationToken.None), 403, check);
    }

    private static async Task InvitationPolicy(Action<bool, string> check)
    {
        var ct = CancellationToken.None;
        var clock = new Clock();
        var store = new MemoryStore();
        var owner = new UserConfigResponse { Language = "he", EnableMatchInvitations = false };
        var member = new UserConfigResponse { Language = "en", EnableMatchInvitations = false };
        var groupConfig = new UserConfigResponse { Language = "en", EnableMatchInvitations = true };
        var ownerPremium = false;
        var memberPremium = false;
        var denyAccess = false;
        var writes = 0;
        var settings = CallbackProxy.Create<IUserConfigAzureStorage>((method, args) =>
        {
            if (method.Name == "ListAsync")
            {
                var config = (UserConfigBlobConfig)args[0]!;
                return Task.FromResult<IResponse>(new GetConfigResponse(config.UId == "member" ? member
                    : config.GroupId == "default" ? owner : groupConfig));
            }
            if (method.Name == "UploadAsync")
            {
                writes++;
                var value = JsonConvert.DeserializeObject<UserConfigResponse>(JsonConvert.SerializeObject(args[0]))!;
                var scope = (UserConfigBlobConfig)args[1]!;
                if (scope.UId == "member") member = value;
                else if (scope.GroupId == "default") owner = value;
                else groupConfig = value;
                return Task.FromResult<IResponse>(new SaveConfigResponse { Success = true });
            }
            throw new InvalidOperationException("Unexpected config operation.");
        });
        var collaboration = CallbackProxy.Create<IGroupCollaborationService>((method, args) =>
            Task.FromResult<GroupAccess?>(denyAccess ? null : new GroupAccess
                { OwnerId = "owner", GroupId = "group", IsOwner = (string)args[0]! == "owner" }));
        var players = CallbackProxy.Create<IPlayersStorageBlobConnector>((method, args) =>
            method.Name == "ListGroupsAsync"
                ? Task.FromResult(new PlayerGroupsResponse { Success = true,
                    Groups = new[] { new PlayerGroup { Id = "group", Name = "Group" } } })
                : method.Name == "ListAsync"
                    ? Task.FromResult<IResponse>(new GetPlayersResponse(new[]
                        { new SkillWisePlayer { Key = "p1", Name = "Player" } }))
                    : throw new InvalidOperationException("No roster writes allowed."));
        var entitlements = CallbackProxy.Create<IAccountEntitlementService>((method, args) =>
            Task.FromResult(new AccountEntitlements { IsPremium = (string)args[0]! == "owner" ? ownerPremium : memberPremium }));
        var telemetry = CallbackProxy.Create<IUsageTelemetry>((method, args) => null);
        var source = new InvitationGroupSource(collaboration, players, settings, entitlements);
        var service = new InvitationService(store, source, clock);
        var request = new CreateInvitationRequest("shared.group", clock.UtcNow.AddDays(7).ToString("O"), "UTC");
        var identity = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("user_id", "member") }, "test"));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        cache.Set("latest-release-version", "test");
        var home = new HomeController(NullLogger<HomeController>.Instance, settings, telemetry, null!,
            entitlements, collaboration, players, cache)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = identity } }
        };
        async Task<GetAppSetupResponse> Setup() =>
            (GetAppSetupResponse)((OkObjectResult)(await home.Get("test", "untrusted", "shared.group")).Result!).Value!;
        check(!new UserConfigResponse().EnableMatchInvitations
            && !JsonConvert.DeserializeObject<UserConfigResponse>("{}")!.EnableMatchInvitations,
            "old/missing account invitation setting defaults off");
        check(!(await Setup()).InvitationAccess.Enabled, "group-local flag cannot enable owner's disabled invitations");
        check((await Setup()).GroupJoinAccess.Enabled,
            "Free owner group joining defaults on without opting into match invitations");
        await Error(() => service.CreateAsync("member", request, ct), 403, check);
        owner.EnableMatchInvitations = true;
        var setup = await Setup();
        check(setup.InvitationAccess.Enabled && !setup.Config.EnableMatchInvitations
            && setup.Entitlements.CanUseMatchInvitations && !setup.Entitlements.IsPremium && setup.Config.Language == "en",
            "shared Home availability uses owner while preserving free requester's own global config and entitlement");
        var invite = await service.CreateAsync("member", request, ct);
        check(invite.Language == "he", "Free co-organizer can share an enabled Free owner's invitation");
        await service.RespondAsync(invite.Token, new("p1", "in"), ct);
        var before = await store.ReadAsync<InvitationState>(InvitationKeys.State(invite.Id), ct);
        owner.EnableMatchInvitations = false;
        var closed = await service.PublicAsync(invite.Token, ct);
        check(closed.Status == "closed" && closed.Id == invite.Id && closed.ScheduledAt == invite.ScheduledAt
            && closed.Players.Single().Status == "in", "disabling closes original public occurrence and preserves response");
        check((await service.CurrentAsync("member", request.GroupId, request.ScheduledAt, ct))!.Status == "closed",
            "organizer current sees same disabled occurrence");
        await Error(() => service.RespondAsync(invite.Token, new("p1", "out"), ct), 409, check);
        await Error(() => service.CreateAsync("member", request, ct), 403, check);
        owner.EnableMatchInvitations = true;
        check((await service.PublicAsync(invite.Token, ct)).Status == "open", "reenabling reopens future original occurrence");
        ownerPremium = true;
        check((await Setup()).InvitationAccess.Enabled, "upgrading preserves enabled invitations");
        ownerPremium = false;
        memberPremium = true;
        member.EnableMatchInvitations = true;
        check((await Setup()).InvitationAccess.Enabled && (await Setup()).GroupJoinAccess.Enabled,
            "Premium co-organizer can use both invitation features with a Free owner");
        check((await service.CreateAsync("member", request, ct)).Id == invite.Id,
            "owner downgrade preserves the original invitation");
        check((await service.PublicAsync(invite.Token, ct)).Status == "open", "owner downgrade keeps existing link open");
        var after = await store.ReadAsync<InvitationState>(InvitationKeys.State(invite.Id), ct);
        check(before!.ETag == after!.ETag && JsonConvert.SerializeObject(before.Value) == JsonConvert.SerializeObject(after.Value),
            "disable/downgrade/read/rejected responses do not alter events, queue, revision or stored response");
        check((await service.RespondAsync(invite.Token, new("p1", "out"), ct)).Players.Single().Status == "out",
            "anonymous RSVP can change after owner downgrades to Free");
        foreach (var language in new[] { "he", "heb", "he-il", "iw", " HE-IL ", "en", "fr", "" })
        {
            owner.Language = language;
            var publicView = await service.PublicAsync(invite.Token, ct);
            check(publicView.Language == (new[] { "en", "fr", "" }.Contains(language) ? "en" : "he"),
                "public language follows live normalized owner preference: " + language);
        }
        var freeOwner = await entitlements.GetAsync("owner");
        check(freeOwner.CanUseMatchInvitations && !freeOwner.CanUseAiSummary && !freeOwner.CanUseAiAlgorithm
            && !freeOwner.CanUseChemistry && !freeOwner.CanUsePremiumShareTemplates && !freeOwner.CanUseCustomGroupLogo
            && freeOwner.MaximumPlayers == 25 && freeOwner.MaximumOwnedGroups == 2,
            "Free invitation access does not unlock paid features or raise existing limits");
        var analyzer = CallbackProxy.Create<IGroupJoinAnalyzer>((method, args) =>
            throw new InvalidOperationException("Free invitation tests must never invoke AI."));
        var joins = new GroupJoinService(store, source, clock, new GroupJoinRoster(store), analyzer, entitlements);
        var link = (await joins.LinkAsync("owner", "group", true, ct)).Token!;
        check((await joins.LinkAsync("member", "shared.group", true, ct)).Token == link,
            "Free owner and Premium co-organizer share one canonical group link");
        var joinView = await joins.PublicAsync(link, ct);
        var ratings = joinView.Skills.ToDictionary(skill => skill.Id, _ => 5.0);
        var submission = new GroupJoinSubmission(Guid.NewGuid().ToString("D"), "New Free player", false,
            "I play weekly.", ratings, joinView.AlgorithmKey);
        var rosterPath = (string)typeof(PlayersStorageBlobConnector).GetMethod("GetPlayersPath",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(null, new object[] { new PlayersBlobConfig
                { UId = "owner", GroupId = "group", AlgoType = joinView.AlgorithmKey } })!;
        var existingPlayers = new JArray(Enumerable.Range(1, 24).Select(index =>
            new JObject { ["key"] = "existing-" + index, ["name"] = "Existing " + index, ["isArrived"] = false }));
        await store.WriteAsync(rosterPath, existingPlayers, null, ct);
        await joins.SubmitAsync(link, submission, ct);
        var pending = (await joins.RequestsAsync("owner", "group", ct)).Single();
        var storedRoster = (await store.ReadAsync<JToken>(rosterPath, ct))!;
        check(pending.Status == "pending" && RosterDocument.Players(storedRoster.Value).Count == 24,
            "a Free group request waits for approval and never adds attendance or roster membership");
        await Error(() => joins.AnalyzeAsync("owner", "group", pending.Id, pending.Revision, ct), 403, check);
        memberPremium = false;
        await Error(() => joins.AnalyzeAsync("member", "shared.group", pending.Id, pending.Revision, ct), 403, check);
        owner.EnableGroupJoinRequests = false;
        check(!(await Setup()).GroupJoinAccess.Enabled && (await Setup()).InvitationAccess.Enabled,
            "Free owner can disable group joining without disabling match RSVP");
        await Error(() => joins.LinkAsync("member", "shared.group", true, ct), 403, check);
        await Error(() => joins.PublicAsync(link, ct), 403, check);
        await Error(() => joins.SubmitAsync(link, submission with { SubmissionId = Guid.NewGuid().ToString("D") }, ct), 403, check);
        await Error(() => joins.ApproveAsync("owner", "group", pending.Id, new(pending.Revision, ratings), ct), 403, check);
        owner.EnableGroupJoinRequests = true;
        denyAccess = true;
        await Error(() => joins.RequestsAsync("member", "shared.group", ct), 403, check);
        await Error(() => joins.ApproveAsync("member", "shared.group", pending.Id, new(pending.Revision, ratings), ct), 403, check);
        await Error(() => service.CreateAsync("member", request, ct), 403, check);
        denyAccess = false;
        check((await joins.RequestsAsync("owner", "group", ct)).Single().Revision == pending.Revision
            && (await store.ReadAsync<JToken>(rosterPath, ct))!.ETag == storedRoster.ETag,
            "disabled settings, AI rejection and revoked access do not change request or roster");
        var approved = await joins.ApproveAsync("owner", "group", pending.Id, new(pending.Revision, ratings), ct);
        storedRoster = (await store.ReadAsync<JToken>(rosterPath, ct))!;
        var approvedPlayer = RosterDocument.Players(storedRoster.Value).Single(player => player["name"]!.Value<string>() == submission.Name);
        check(approved.Status == "approved" && RosterDocument.Players(storedRoster.Value).Count == 25
            && approvedPlayer["isArrived"]!.Value<bool>() == false && approvedPlayer["waitingListOrder"]!.Type == JTokenType.Null,
            "Free owner can approve the 25th player without setting match attendance");
        await joins.SubmitAsync(link, submission with { SubmissionId = Guid.NewGuid().ToString("D"), Name = "Over quota" }, ct);
        var overQuota = (await joins.RequestsAsync("owner", "group", ct)).Single(item => item.Status == "pending");
        memberPremium = true;
        await Error(() => joins.ApproveAsync("member", "shared.group", overQuota.Id, new(overQuota.Revision, ratings), ct), 409, check);
        check((await store.ReadAsync<JToken>(rosterPath, ct))!.ETag == storedRoster.ETag
            && (await joins.RequestsAsync("owner", "group", ct)).Single(item => item.Id == overQuota.Id).Status == "pending",
            "Premium co-organizer cannot exceed the Free owner's 25-player limit");
        memberPremium = false;
        await joins.RejectAsync("member", "shared.group", overQuota.Id, overQuota.Revision, ct);
        check((await joins.RequestsAsync("owner", "group", ct)).Single(item => item.Id == overQuota.Id).Status == "rejected",
            "Free co-organizer can reject a membership request");
        await joins.RevokeAsync("member", "shared.group", ct);
        await Error(() => joins.PublicAsync(link, ct), 404, check);

        var configController = new ConfigController(NullLogger<HomeController>.Instance, settings, telemetry,
            entitlements, collaboration, players)
        {
            ControllerContext = home.ControllerContext
        };
        memberPremium = false;
        owner.EnableMatchInvitations = false;
        check((await configController.Post("test", new UserConfigResponse { EnableMatchInvitations = true },
            "owner", "shared.group", "global")).Success && writes == 1
            && member.EnableMatchInvitations && !owner.EnableMatchInvitations && !(await Setup()).InvitationAccess.Enabled,
            "Free caller can enable their own invitations, but forged UID cannot change shared owner's disabled flag");
        owner.EnableMatchInvitations = true;
        check((await configController.Post("test", new UserConfigResponse { EnableMatchInvitations = false },
            "owner", "shared.group", "global")).Success && !member.EnableMatchInvitations && owner.EnableMatchInvitations,
            "free account can disable without altering shared owner's global flag");
        check((await configController.Post("test", new UserConfigResponse { EnableMatchInvitations = false },
            "member", "shared.group", "group")).Success && owner.EnableMatchInvitations,
            "group-only saves preserve owner global flag");
        memberPremium = true;
        check((await configController.Post("test", new UserConfigResponse { EnableMatchInvitations = true },
            "owner", "shared.group", "global")).Success && member.EnableMatchInvitations,
            "Premium global save persists requester's account-wide flag");
        configController.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity());
        check(!(await configController.Post("test", new UserConfigResponse { EnableMatchInvitations = true },
            "member", "group", "global")).Success, "anonymous UID cannot enable invitations");
        configController.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(Array.Empty<Claim>(), "test"));
        check(!(await configController.Post("test", new UserConfigResponse { EnableMatchInvitations = true },
            "member", "group", "global")).Success, "principal without a verified identity cannot enable using supplied UID");
        clock.UtcNow = invite.ScheduledAt;
        check((await service.PublicAsync(invite.Token, ct)).Status == "closed", "reenabling never opens a past occurrence");
    }

    private static async Task DeliveryEdges(Action<bool, string> check)
    {
        var ct = CancellationToken.None;
        var store = new MemoryStore();
        var clock = new Clock();
        var groups = new Groups();
        var service = new InvitationService(store, groups, clock);
        var accounts = new NotificationAccountService(store, clock);
        var sender = new Sender();
        var processor = new InvitationDigestProcessor(store, groups, accounts, sender, clock);
        var invite = await service.CreateAsync("owner", new("group", clock.UtcNow.AddDays(20).ToString("O"), "Etc/UTC"), ct);
        var key = InvitationKeys.State(invite.Id);
        await accounts.RegisterAsync("owner", new("android-one", "android"), ct);
        for (var i = 0; i < 12; i++)
        {
            await service.RespondAsync(invite.Token, new("p1", "in"), ct);
            await service.RespondAsync(invite.Token, new("p1", "out"), ct);
        }
        await processor.ProcessAsync(key, ct);
        check(sender.Deliveries.Count == 0
            && InvitationService.NetChanges((await store.ReadAsync<InvitationState>(key, ct))!.Value, groups.Players).Count == 1,
            "24 changes by one player coalesce into one pending withdrawal, not 24 distinct changed players");
        for (var i = 1; i <= 9; i++) await service.RespondAsync(invite.Token, new("p" + i, "in"), ct);
        await processor.ProcessAsync(key, ct);
        check(sender.Deliveries.Count == 0, "nine distinct pending players do not trigger default threshold");
        await service.RespondAsync(invite.Token, new("p10", "in"), ct);
        await processor.ProcessAsync(key, ct);
        check(sender.Deliveries.Count == 1 && sender.Deliveries.Single().Summary.StartsWith("10 joined, 0 withdrew;"),
            "tenth distinct actionable player triggers exactly one digest");

        // A failed second device must not resend the successful first device after restart.
        clock.UtcNow = clock.UtcNow.AddMinutes(10);
        await accounts.SetPreferencesAsync("owner", new(true, 1, 60), ct);
        await accounts.RegisterAsync("owner", new("android-two", "android"), ct);
        await service.RespondAsync(invite.Token, new("p1", "out"), ct);
        sender.Results.Enqueue(PushResult.Delivered);
        sender.Results.Enqueue(PushResult.Retry);
        await processor.ProcessAsync(key, ct);
        var partial = (await store.ReadAsync<InvitationState>(key, ct))!;
        check(partial.Value.Batch?.CompletedDevices.Count == 1 && partial.Value.Batch.AnyDelivered,
            "partial delivery persists per-device receipt");
        clock.UtcNow = partial.Value.RetryAt!.Value;
        await new InvitationDigestProcessor(store, groups, accounts, sender, clock).ProcessAsync(key, ct);
        check(sender.Deliveries.Count == 3, "retry sends only previously unsuccessful device");
        check((await store.ReadAsync<InvitationState>(key, ct))!.Value.Batch == null, "partial retry completes persistent outbox");

        // An abandoned lease is exclusive until expiry and then recoverable.
        clock.UtcNow = clock.UtcNow.AddMinutes(10);
        await service.RespondAsync(invite.Token, new("p11", "in"), ct);
        var abandoned = (await store.ReadAsync<InvitationState>(key, ct))!;
        abandoned.Value.LeaseOwner = "crashed-instance";
        abandoned.Value.LeaseUntil = clock.UtcNow.AddMinutes(2);
        await store.WriteAsync(key, abandoned.Value, abandoned.ETag, ct);
        await processor.ProcessAsync(key, ct);
        check(sender.Deliveries.Count == 3, "live persisted lease cannot be stolen");
        clock.UtcNow = clock.UtcNow.AddMinutes(2);
        await processor.ProcessAsync(key, ct);
        check(sender.Deliveries.Count == 5, "expired crashed-worker lease is recovered");

        // Response and event commits remain immediate during a push; post-snapshot changes survive acknowledgement.
        clock.UtcNow = clock.UtcNow.AddMinutes(10);
        await service.RespondAsync(invite.Token, new("p2", "out"), ct);
        var responseDuringSend = false;
        sender.BeforeSend = async () =>
        {
            if (responseDuringSend) return;
            responseDuringSend = true;
            await service.RespondAsync(invite.Token, new("p12", "in"), ct);
        };
        await processor.ProcessAsync(key, ct);
        sender.BeforeSend = null;
        var during = (await store.ReadAsync<InvitationState>(key, ct))!.Value;
        check(during.PendingSince == clock.UtcNow && during.Revision > during.BaselineRevision
            && during.Responses["p12"].Status == "in" && !during.Baseline["p12"],
            "responses during delivery remain visible and pending after snapshot acknowledgement");
        var message = FirebaseDigestSender.BuildMessage("private-registration", during, sender.Deliveries.Last(), clock.UtcNow);
        check(message.Notification != null && message.Android.Priority == FirebaseAdmin.Messaging.Priority.Normal
            && message.Android.Notification.ChannelId == "matchday-rsvp"
            && message.Data["invitationId"] == invite.Id && message.Data["groupId"] == "group"
            && message.Data.ContainsKey("scheduledAt") && message.Data.ContainsKey("digestId")
            && message.Android.TimeToLive <= TimeSpan.FromHours(6),
            "FCM payload displays with app closed and carries exact native deep-link fields");
        var telemetry = new Microsoft.ApplicationInsights.DataContracts.RequestTelemetry
        {
            Url = new Uri("https://localhost/Invitations/Public/" + invite.Token + "/Responses"),
            Name = "POST /Invitations/Public/" + invite.Token + "/Responses"
        };
        new InvitationTelemetryRedactor().Initialize(telemetry);
        check(!telemetry.Url.ToString().Contains(invite.Token) && !telemetry.Name.Contains(invite.Token)
            && !telemetry.Context.Operation.Name.Contains(invite.Token), "capability is redacted from request telemetry");
        var sendsBeforeClose = sender.Deliveries.Count;
        clock.UtcNow = invite.ScheduledAt;
        await processor.ProcessAsync(key, ct);
        check(sender.Deliveries.Count == sendsBeforeClose, "closed occurrences never initiate late digests");
    }

    private static async Task UnsentWithdrawal(Action<bool, string> check)
    {
        var ct = CancellationToken.None;
        var store = new MemoryStore();
        var clock = new Clock();
        var groups = new Groups();
        var service = new InvitationService(store, groups, clock);
        var accounts = new NotificationAccountService(store, clock);
        var sender = new Sender();
        var processor = new InvitationDigestProcessor(store, groups, accounts, sender, clock);
        var invite = await service.CreateAsync("owner", new("group", clock.UtcNow.AddDays(2).ToString("O"), "UTC"), ct);
        var key = InvitationKeys.State(invite.Id);
        await accounts.RegisterAsync("owner", new("android-withdrawal", "android"), ct);
        var firstChange = clock.UtcNow;
        await service.RespondAsync(invite.Token, new("p1", "in"), ct);
        clock.UtcNow = clock.UtcNow.AddMinutes(15);
        var withdrawal = await service.RespondAsync(invite.Token, new("p1", "out"), ct);
        var state = (await store.ReadAsync<InvitationState>(key, ct))!.Value;
        check(withdrawal.Revision == 2 && withdrawal.Counts.Out == 1 && state.Events.Count == 2
            && state.PendingSince == firstChange
            && InvitationService.NetChanges(state, groups.Players).Single() is { Key: "p1", Value: false },
            "In-to-Out before first digest remains one meaningful withdrawal with original anchored deadline");
        await service.RespondAsync(invite.Token, new("p1", "out"), ct);
        await service.RespondAsync(invite.Token, new("p2", "out"), ct);
        check((await store.ReadAsync<InvitationState>(key, ct))!.Value.Revision == 2,
            "duplicate withdrawal and another player's initial Out add no event");
        await processor.ProcessAsync(key, ct);
        check(sender.Deliveries.Count == 0, "unsent withdrawal still respects explicit batching, not instant delivery");
        clock.UtcNow = firstChange.AddHours(6);
        await processor.ProcessAsync(key, ct);
        check(sender.Deliveries.Single().Summary == "0 joined, 1 withdrew; 0/2 places filled",
            "first digest reports changed mind even though initial In was never delivered");
        clock.UtcNow = clock.UtcNow.AddHours(6);
        await processor.ProcessAsync(key, ct);
        check(sender.Deliveries.Count == 1, "acknowledged withdrawal does not replay against initially unanswered baseline");
        await service.RespondAsync(invite.Token, new("p1", "in"), ct);
        await service.RespondAsync(invite.Token, new("p1", "out"), ct);
        check(InvitationService.NetChanges((await store.ReadAsync<InvitationState>(key, ct))!.Value, groups.Players)
            .Single() is { Key: "p1", Value: false }, "a later genuine In-to-Out remains meaningful after an earlier withdrawal digest");
    }

    private static async Task DeviceOwnershipEdges(Action<bool, string> check)
    {
        var ct = CancellationToken.None;
        var store = new MemoryStore();
        var clock = new Clock();
        var accounts = new NotificationAccountService(store, clock);
        var sender = new Sender();
        const string token = "shared-physical-phone";
        var tokenKey = InvitationKeys.Hash(token);
        await accounts.RegisterAsync("owner", new(token, "android"), ct);
        var oldDevice = (await accounts.GetAsync("owner", ct)).Devices[tokenKey];
        await accounts.RegisterAsync("owner", new(token, "android"), ct);
        check((await accounts.GetAsync("owner", ct)).Devices.Count == 1 && await accounts.IsOwnedAsync("owner", tokenKey, ct),
            "repeated registration is idempotent with globally unique current owner");
        await accounts.RemoveByKeyAsync("owner", tokenKey, oldDevice, ct);
        check((await accounts.GetAsync("owner", ct)).Devices.Count == 1 && await accounts.IsOwnedAsync("owner", tokenKey, ct),
            "stale invalid-token cleanup cannot remove a refreshed registration, even at same timestamp");
        await accounts.SetPreferencesAsync("owner", new(true, 1, 60), ct);
        var staleInventory = (await store.ReadAsync<NotificationAccount>(InvitationKeys.Account("owner"), ct))!.Value;
        await accounts.RegisterAsync("new-owner", new(token, "android"), ct);
        check(!await accounts.IsOwnedAsync("owner", tokenKey, ct) && await accounts.IsOwnedAsync("new-owner", tokenKey, ct)
            && (await accounts.GetAsync("owner", ct)).Devices.Count == 0,
            "same physical token transfers to exactly one authenticated owner and removes old inventory");
        await accounts.RemoveAsync("owner", token, ct);
        check(await accounts.IsOwnedAsync("new-owner", tokenKey, ct)
            && (await accounts.GetAsync("new-owner", ct)).Devices.Count == 1,
            "delayed old-account logout cannot unregister the new account's token");

        // A crash or concurrent old registration may leave an inventory; it must never grant send authority.
        var oldAccount = (await store.ReadAsync<NotificationAccount>(InvitationKeys.Account("owner"), ct))!;
        await store.WriteAsync(InvitationKeys.Account("owner"), staleInventory, oldAccount.ETag, ct);
        var groups = new Groups();
        var service = new InvitationService(store, groups, clock);
        var invite = await service.CreateAsync("owner", new("group", clock.UtcNow.AddDays(2).ToString("O"), "UTC"), ct);
        await service.RespondAsync(invite.Token, new("p1", "in"), ct);
        await new InvitationDigestProcessor(store, groups, accounts, sender, clock)
            .ProcessAsync(InvitationKeys.State(invite.Id), ct);
        check(sender.Deliveries.Count == 0 && await accounts.IsOwnedAsync("new-owner", tokenKey, ct),
            "worker rejects stale old-owner inventory without sending or deleting new owner's token");
        await accounts.RemoveAsync("new-owner", token, ct);
        await accounts.RemoveAsync("new-owner", token, ct);
        check(!await accounts.IsOwnedAsync("new-owner", tokenKey, ct)
            && (await accounts.GetAsync("new-owner", ct)).Devices.Count == 0,
            "DELETE succeeds idempotently for an already removed token");

        store.ThrowNextWriteForKey = InvitationKeys.Account("recovery-owner");
        var interrupted = false;
        try { await accounts.RegisterAsync("recovery-owner", new(token, "android"), ct); }
        catch (IOException) { interrupted = true; }
        check(interrupted && await accounts.IsOwnedAsync("recovery-owner", tokenKey, ct),
            "ambiguous registration can retain authoritative ownership without a completed inventory write");
        await accounts.RegisterAsync("recovery-owner", new(token, "android"), ct);
        check((await accounts.GetAsync("recovery-owner", ct)).Devices.Count == 1,
            "retry repairs interrupted registration without duplicate device");
        store.YieldReads = true;
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => accounts.RegisterAsync("recovery-owner", new(token, "android"), ct)));
        store.YieldReads = false;
        check((await accounts.GetAsync("recovery-owner", ct)).Devices.Count == 1
            && await accounts.IsOwnedAsync("recovery-owner", tokenKey, ct),
            "simultaneous same-owner registration retries remain idempotent");
    }

    private static async Task Error(Func<Task> action, int expected, Action<bool, string> check)
    {
        try { await action(); throw new InvalidOperationException("Expected invitation error " + expected); }
        catch (InvitationException ex) { check(ex.StatusCode == expected, $"invitation error status {expected}"); }
    }

    private sealed class Clock : IInvitationClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 9, 20, 0, 0, 0, TimeSpan.Zero);
    }

    private sealed class Groups : IInvitationGroupSource
    {
        public int EventDurationMinutes { get; set; } = 120;
        public bool DenyAccess { get; set; }
        public List<RosterPlayer> Players { get; } = Enumerable.Range(1, 15)
            .Select(i => new RosterPlayer("p" + i, i <= 2 ? "Same name" : "Player " + i)).ToList();
        public Task<InvitationGroup> ResolveAsync(string requesterId, string groupReference, CancellationToken cancellationToken)
        {
            if (DenyAccess) throw new InvitationException(403, "Denied");
            if (groupReference == "shared" && requesterId != "collaborator")
                throw new InvitationException(403, "Denied");
            return LoadAsync(groupReference == "shared" ? "owner" : requesterId,
                groupReference == "shared" ? "group" : groupReference, cancellationToken);
        }
        public Task<InvitationGroup> LoadAsync(string ownerId, string groupId, CancellationToken cancellationToken)
        {
            if (groupId != "group") throw new InvitationException(404, "Missing");
            return Task.FromResult(new InvitationGroup(ownerId, groupId, "Private group", "Weekly football",
                "Pitch", null, 2, Players.ToArray(), true, "en", EventDurationMinutes));
        }
    }

    private sealed class Sender : IInvitationPushSender
    {
        public bool Enabled { get; set; } = true;
        public bool Configured { get; set; } = true;
        public Task<bool> IsDeliveryConfiguredAsync(CancellationToken ct) => Task.FromResult(Enabled && Configured);
        public PushResult Result { get; set; } = PushResult.Delivered;
        public Queue<PushResult> Results { get; } = new();
        public Func<Task>? BeforeSend { get; set; }
        public List<DigestBatch> Deliveries { get; } = new();
        public async Task<PushResult> SendAsync(string deviceToken, InvitationState invitation, DigestBatch batch, CancellationToken ct)
        {
            if (BeforeSend != null) await BeforeSend();
            var result = Results.Count > 0 ? Results.Dequeue() : Result;
            if (result == PushResult.Delivered)
                Deliveries.Add(JsonConvert.DeserializeObject<DigestBatch>(JsonConvert.SerializeObject(batch))!);
            return result;
        }
    }

    internal sealed class MemoryStore : IInvitationDocumentStore
    {
        private readonly Dictionary<string, (string Json, int Version)> _values = new();
        private readonly object _gate = new();
        public bool YieldReads { get; set; }
        public int FailNextWrites { get; set; }
        public string? ThrowNextWriteForKey { get; set; }
        public async Task<StoredDocument<T>?> ReadAsync<T>(string key, CancellationToken cancellationToken)
        {
            if (YieldReads) await Task.Yield();
            lock (_gate)
                return _values.TryGetValue(key, out var value)
                    ? new(JsonConvert.DeserializeObject<T>(value.Json)!, value.Version.ToString()) : null;
        }
        public Task<bool> WriteAsync<T>(string key, T value, string? etag, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                if (ThrowNextWriteForKey == key)
                {
                    ThrowNextWriteForKey = null;
                    throw new IOException("Simulated interrupted document write.");
                }
                if (FailNextWrites > 0) { FailNextWrites--; return Task.FromResult(false); }
                var exists = _values.TryGetValue(key, out var previous);
                if (etag == null ? exists : !exists || previous.Version.ToString() != etag) return Task.FromResult(false);
                _values[key] = (JsonConvert.SerializeObject(value), previous.Version + 1);
                return Task.FromResult(true);
            }
        }
        public Task<DocumentPage> ListAsync(string prefix, string? continuation, int pageSize, CancellationToken cancellationToken)
        {
            lock (_gate)
                return Task.FromResult(new DocumentPage(_values.Keys.Where(k => k.StartsWith(prefix)).Take(pageSize).ToArray(), null));
        }
    }
}
