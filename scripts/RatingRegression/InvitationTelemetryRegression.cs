using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using TeamsGeneratorWebAPI.Controllers;
using TeamsGeneratorWebAPI.Invitations;
using TeamsGeneratorWebAPI.Telemetry;

internal static class InvitationTelemetryRegression
{
    public static async Task Run(Action<bool, string> check)
    {
        var ct = CancellationToken.None;
        var store = new Store();
        var telemetry = new Telemetry(store);
        var clock = new Clock();
        var groups = new Groups();
        var service = new InvitationService(store, groups, clock, telemetry);
        var request = new CreateInvitationRequest("PRIVATE-group", clock.UtcNow.AddDays(7).ToString("O"), "UTC");
        var invitation = await service.CreateAsync("PRIVATE-requester", request, ct);
        check(telemetry.Events.Single().Name == "InvitationLinkCreated"
            && telemetry.Events.Single().Commits == 1, "invitation creation is recorded after the state commit");
        await service.CreateAsync("PRIVATE-collaborator", request, ct);
        check(telemetry.Events.Last().Name == "InvitationLinkReused"
            && telemetry.Events.Last().UserId == "PRIVATE-collaborator",
            "existing links are reused and attributed to the requester, not canonical owner");
        store.LoseNextCommit = true;
        await service.CreateAsync("PRIVATE-requester",
            request with { ScheduledAt = clock.UtcNow.AddDays(8).ToString("O") }, ct);
        check(telemetry.Events.Count(e => e.Name == "InvitationLinkCreated") == 1
            && telemetry.Events.Last().Name == "InvitationLinkReused",
            "concurrent create loser reports reuse rather than another creation");
        CheckPrivacy(telemetry.Events, invitation, check);
        telemetry.Events.Clear();

        await service.RespondAsync(invitation.Token, new("PRIVATE-p1", "out"), ct);
        check(telemetry.Events.Count == 0, "initial out produces no mutation metric");
        store.FailNextWrites = 2;
        var commits = store.StateCommits;
        await service.RespondAsync(invitation.Token, new("PRIVATE-p1", "in"), ct);
        var joined = telemetry.Events.Single();
        check(joined.Name == "InvitationResponseChanged" && joined.Commits == commits + 1
            && joined.Properties["previous_status"] == "unanswered" && joined.Properties["new_status"] == "in",
            "response conflicts emit once and only after commit with actual transition");
        await service.RespondAsync(invitation.Token, new("PRIVATE-p1", "in"), ct);
        check(telemetry.Events.Count == 1, "duplicate response is not feature activity");
        await service.RespondAsync(invitation.Token, new("PRIVATE-p2", "in"), ct);
        check(telemetry.Events.Last().Properties["new_status"] == "waiting"
            && telemetry.Events.Last().Properties["response_status"] == "in"
            && telemetry.Events.Last().Measurements["waiting_count"] == 1,
            "persisted in response distinguishes actual waiting-list placement");
        await service.RespondAsync(invitation.Token, new("PRIVATE-p1", "out"), ct);
        check(telemetry.Events.Last().Properties["previous_status"] == "in"
            && telemetry.Events.Last().Properties["new_status"] == "out"
            && telemetry.Events.Last().Measurements["promoted_count"] == 1
            && telemetry.Events.Last().Measurements["in_count"] == 1
            && telemetry.Events.Last().Measurements["waiting_count"] == 0,
            "withdrawal counts promotions from the same committed roster snapshot");
        await service.RespondAsync(invitation.Token, new("PRIVATE-p1", "in"), ct);
        await service.RespondAsync(invitation.Token, new("PRIVATE-p1", "out"), ct);
        check(telemetry.Events.Last().Properties["previous_status"] == "waiting"
            && telemetry.Events.Last().Measurements["promoted_count"] == 0,
            "waiting-list withdrawal is not a promotion");
        var count = telemetry.Events.Count;
        store.LoseNextCommit = true;
        await service.RespondAsync(invitation.Token, new("PRIVATE-p3", "in"), ct);
        check(telemetry.Events.Count == count, "retry observing a competing identical response emits no change");
        store.FailNextWrites = 12;
        await Error(() => service.RespondAsync(invitation.Token, new("PRIVATE-p3", "out"), ct), 409, check);
        await Error(() => service.RespondAsync(invitation.Token, new("PRIVATE-missing", "in"), ct), 400, check);
        await Error(() => service.RespondAsync(invitation.Token, new("PRIVATE-p3", "PRIVATE-invalid"), ct), 400, check);
        check(telemetry.Events.Count == count, "exhausted conflicts and rejected responses emit no persisted-change metrics");
        check(telemetry.Events.All(e => e.UserId == null && e.Properties["actor_type"] == "participant"),
            "public responses never carry organizer identity");
        CheckPrivacy(telemetry.Events, invitation, check);
        telemetry.Events.Clear();

        await service.PublicAsync(invitation.Token, ct);
        check(telemetry.Events.Count == 0, "service projections are not counted as HTTP reads");
        using var limiter = new InvitationPublicRateLimiter();
        var controller = new InvitationsController(service, limiter, telemetry)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        new[] { new Claim("user_id", "PRIVATE-authenticated-organizer") }, "test"))
                }
            }
        };
        await controller.Public(invitation.Token, ct);
        await controller.Public(invitation.Token, ct);
        await Error(() => controller.Public("PRIVATE-invalid-token", ct), 404, check);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        try
        {
            await controller.Public(invitation.Token, canceled.Token);
            check(false, "canceled read propagates cancellation");
        }
        catch (OperationCanceledException)
        {
            check(telemetry.Events.Last().Properties["outcome"] == "canceled", "canceled read recorded without exception content");
        }
        for (var i = 0; i < 600; i++) limiter.Check("PRIVATE-throttled-token", null, null);
        await Error(() => controller.Public("PRIVATE-throttled-token", ct), 429, check);
        check(telemetry.Events.Count == 5
            && telemetry.Events.All(e => e.Name == "InvitationPublicReadRequested")
            && telemetry.Events.Count(e => e.Properties["outcome"] == "succeeded") == 2
            && telemetry.Events.Count(e => e.Properties["outcome"] == "failed") == 2,
            "HTTP reads include polling, failures and throttling rather than unique visitors");
        check(telemetry.Events.All(e => e.UserId == null && e.Properties["actor_type"] == "participant"),
            "public HTTP reads remain participant activity even with an authenticated organizer principal");
        CheckPrivacy(telemetry.Events, invitation, check);
        telemetry.Events.Clear();

        store.FailNextWrites = 12;
        await Error(() => service.RevokeAsync("PRIVATE-requester", request.GroupId, request.ScheduledAt, ct), 409, check);
        check(telemetry.Events.Count == 0, "uncommitted revocation emits no success");
        store.FailNextWrites = 2;
        commits = store.StateCommits;
        await service.RevokeAsync("PRIVATE-requester", request.GroupId, request.ScheduledAt, ct);
        await service.RevokeAsync("PRIVATE-requester", request.GroupId, request.ScheduledAt, ct);
        await service.RevokeAsync("PRIVATE-requester", request.GroupId, clock.UtcNow.AddDays(9).ToString("O"), ct);
        check(telemetry.Events.Count == 1 && telemetry.Events.Single().Name == "InvitationRevoked"
            && telemetry.Events.Single().Commits == commits + 1
            && telemetry.Events.Single().UserId == "PRIVATE-requester"
            && telemetry.Events.Single().Properties["outcome"] == "succeeded",
            "revocation only counts a winning commit, not retries, repeat deletes or absent invitations");
        CheckPrivacy(telemetry.Events, invitation, check);
    }

    private static void CheckPrivacy(IEnumerable<RecordedEvent> events, OrganizerInvitation invitation,
        Action<bool, string> check)
    {
        var payload = JsonConvert.SerializeObject(events.Select(e => new { e.Name, e.Properties, e.Measurements }));
        check(!payload.Contains("PRIVATE", StringComparison.Ordinal)
            && !payload.Contains(invitation.Token, StringComparison.Ordinal)
            && !payload.Contains(invitation.Id, StringComparison.Ordinal),
            "invitation telemetry dimensions exclude tokens, identifiers, player keys, names and freeform text");
    }

    private static async Task Error(Func<Task> action, int status, Action<bool, string> check)
    {
        try { await action(); check(false, "expected invitation rejection " + status); }
        catch (InvitationException error) { check(error.StatusCode == status, "invitation rejection " + status); }
    }

    private sealed class Clock : IInvitationClock
    {
        public DateTimeOffset UtcNow => new(2026, 9, 22, 0, 0, 0, TimeSpan.Zero);
    }

    private sealed class Groups : IInvitationGroupSource
    {
        public Task<InvitationGroup> ResolveAsync(string requesterId, string groupReference, CancellationToken cancellationToken)
            => LoadAsync("PRIVATE-owner", groupReference, cancellationToken);
        public Task<InvitationGroup> LoadAsync(string ownerId, string groupId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new InvitationGroup(ownerId, groupId, "PRIVATE-name", "PRIVATE-match",
                "PRIVATE-location", "PRIVATE-logo", 1,
                Enumerable.Range(1, 3).Select(i => new RosterPlayer("PRIVATE-p" + i, "PRIVATE-name")).ToArray(),
                true, "en"));
        }
    }

    private sealed class Store : IInvitationDocumentStore
    {
        private readonly Dictionary<string, (string Json, string ETag)> _documents = new();
        private int _version;
        public int FailNextWrites { get; set; }
        public bool LoseNextCommit { get; set; }
        public int StateCommits { get; private set; }
        public Task<StoredDocument<T>?> ReadAsync<T>(string key, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_documents.TryGetValue(key, out var value)
                ? new StoredDocument<T>(JsonConvert.DeserializeObject<T>(value.Json)!, value.ETag) : null);
        }
        public Task<bool> WriteAsync<T>(string key, T value, string? etag, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (FailNextWrites > 0) { FailNextWrites--; return Task.FromResult(false); }
            var exists = _documents.TryGetValue(key, out var previous);
            if (etag == null ? exists : !exists || previous.ETag != etag) return Task.FromResult(false);
            _documents[key] = (JsonConvert.SerializeObject(value), (++_version).ToString());
            if (value is InvitationState)
            {
                StateCommits++;
                // Simulate another request committing this same candidate before our conditional write.
                if (LoseNextCommit) { LoseNextCommit = false; return Task.FromResult(false); }
            }
            return Task.FromResult(true);
        }
        public Task<DocumentPage> ListAsync(string prefix, string? continuation, int pageSize, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed record RecordedEvent(string Name, string? UserId,
        IReadOnlyDictionary<string, string?> Properties, IReadOnlyDictionary<string, double> Measurements, int Commits);

    private sealed class Telemetry : IUsageTelemetry
    {
        private readonly Store _store;
        public Telemetry(Store store) => _store = store;
        public List<RecordedEvent> Events { get; } = new();
        public void Track(string eventName, string? clientVersion = null, string? userId = null,
            IReadOnlyDictionary<string, string?>? properties = null, IReadOnlyDictionary<string, double>? measurements = null)
            => Events.Add(new(eventName, userId, properties ?? new Dictionary<string, string?>(),
                measurements ?? new Dictionary<string, double>(), _store.StateCommits));
    }
}
