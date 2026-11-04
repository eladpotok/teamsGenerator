using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TeamsGeneratorWebAPI.Controllers;
using TeamsGeneratorWebAPI.Invitations;
using TeamsGeneratorWebAPI.PlayerProfiles;

internal static class InvitationPromotionRegression
{
    public static async Task Run(Action<bool, string> check)
    {
        await QueueAndDelivery(check);
        await Retries(check);
        await Cancellation(check);
        await DeviceOwnership(check);
        await AuthenticationAndMessage(check);
    }

    private static async Task QueueAndDelivery(Action<bool, string> check)
    {
        var f = await Fixture.Create();
        await f.Fill();
        check((await f.State()).Promotions.Count == 0, "joining directly or waiting does not send a promotion");
        f.Store.FailNextWrites = 1;
        var promoted = await f.Service.RespondAsync(f.Invitation.Token, new("p1", "out"), default);
        var state = await f.State();
        check(promoted.Players.Single(p => p.Key == "p2").WaitingListPosition == null
            && state.Promotions.Count == 1, "promotion and notification commit atomically despite a CAS retry");
        check(state.Promotions[0].ProfileId == PlayerProfileService.Id("player")
            && state.Promotions[0].PlayerKey == "p2", "promotion targets the linked account, not the withdrawing player");
        check(!JObject.FromObject(promoted).ContainsKey("Promotions"), "public attendance never exposes notification state");
        await f.Service.RespondAsync(f.Invitation.Token, new("p1", "out"), default);
        check((await f.State()).Promotions.Count == 1, "repeated Out does not queue a duplicate promotion");
        await f.Accounts.RegisterAsync("owner", new("owner-device", "android"), default);
        await f.Accounts.RegisterAsync("player", new("player-device", "android"), default);
        f.Store.YieldReads = true;
        await Task.WhenAll(f.Processor.ProcessAsync(f.Key, default), f.Processor.ProcessAsync(f.Key, default));
        check(f.Sender.Deliveries.SequenceEqual(new[] { "player-device" }), "concurrent workers send only to the promoted player's Android device");
        check((await f.State()).Promotions.Count == 0, "completed promotion leaves no replayable outbox");
        await f.Processor.ProcessAsync(f.Key, default);
        check(f.Sender.Deliveries.Count == 1, "restart or later polls do not resend a completed notification");
        check((await f.State()).Batch == null && (await f.State()).LastDeliveredAt == null,
            "urgent promotion delivery does not consume organizer digest state or thresholds");

        f = await Fixture.Create();
        await f.Fill();
        f.Store.ThrowNextWriteForKey = f.Key;
        try { await f.Service.RespondAsync(f.Invitation.Token, new("p1", "out"), default); check(false, "write must fail"); }
        catch (IOException) { }
        state = await f.State();
        check(state.Responses["p1"].Status == "in" && state.Promotions.Count == 0,
            "failed commit changes neither attendance nor promotion outbox");
        await f.Service.RespondAsync(f.Invitation.Token, new("p1", "out"), default);
        check((await f.State()).Promotions.Count == 1, "retry after interrupted commit creates exactly one promotion");

        f = await Fixture.Create(linked: false);
        await f.Fill();
        await f.Service.RespondAsync(f.Invitation.Token, new("p1", "out"), default);
        check((await f.State()).Promotions.Count == 0, "guest name selection never invents an account notification recipient");
        f = await Fixture.Create(enabled: false);
        await f.Fill();
        await f.Service.RespondAsync(f.Invitation.Token, new("p1", "out"), default);
        check((await f.State()).Promotions.Count == 0, "disabled promotion feature preserves RSVP-only behavior");
    }

    private static async Task Retries(Action<bool, string> check)
    {
        var f = await Fixture.Promoted();
        await f.Accounts.RegisterAsync("player", new("first-device", "android"), default);
        await f.Accounts.RegisterAsync("player", new("second-device", "android"), default);
        f.Sender.Results.Enqueue(PushResult.Delivered);
        f.Sender.Results.Enqueue(PushResult.Retry);
        await f.Processor.ProcessAsync(f.Key, default);
        var promotion = (await f.State()).Promotions.Single();
        check(promotion.CompletedDevices.Count == 1 && promotion.RetryAt > f.Clock.UtcNow
            && promotion.LeaseOwner == null, "partial delivery persists per-device completion and bounded retry");
        var attempts = f.Sender.Attempts;
        await f.Processor.ProcessAsync(f.Key, default);
        check(f.Sender.Attempts == attempts, "retry backoff prevents immediate repeated sends");
        f.Clock.UtcNow = promotion.RetryAt!.Value;
        await new InvitationPromotionProcessor(f.Store, f.Groups, f.Accounts, f.Sender, f.Clock).ProcessAsync(f.Key, default);
        check(f.Sender.Deliveries.Distinct().Count() == 2 && f.Sender.Deliveries.Count == 2
            && (await f.State()).Promotions.Count == 0, "restart retries only the unfinished device");

        f = await Fixture.Promoted();
        f.Sender.Enabled = false;
        await f.Accounts.RegisterAsync("player", new("phone", "android"), default);
        await f.Processor.ProcessAsync(f.Key, default);
        check(f.Sender.Attempts == 0 && (await f.State()).Promotions.Count == 1,
            "unconfigured delivery retains a pending notification");
        f.Sender.Enabled = true;
        f.Sender.Results.Enqueue(PushResult.Unavailable);
        await f.Processor.ProcessAsync(f.Key, default);
        check((await f.State()).Promotions.Single().RetryAt > f.Clock.UtcNow,
            "provider credential unavailability does not lose the notification");

        f = await Fixture.Promoted();
        await f.Processor.ProcessAsync(f.Key, default);
        check((await f.State()).Promotions.Count == 1 && f.Sender.Attempts == 0,
            "no registered Android device is not falsely marked delivered");
        await f.Accounts.RegisterAsync("player", new("later-device", "android"), default);
        await f.Processor.ProcessAsync(f.Key, default);
        check(f.Sender.Deliveries.SequenceEqual(new[] { "later-device" }), "a pending promotion can reach a newly registered signed-in device");

        f = await Fixture.Promoted();
        await f.Accounts.RegisterAsync("player", new("phone", "android"), default);
        var stored = (await f.Store.ReadAsync<InvitationState>(f.Key, default))!;
        stored.Value.Promotions[0].LeaseOwner = "interrupted-worker";
        stored.Value.Promotions[0].LeaseUntil = f.Clock.UtcNow.AddSeconds(-1);
        await f.Store.WriteAsync(f.Key, stored.Value, stored.ETag, default);
        await f.Processor.ProcessAsync(f.Key, default);
        check(f.Sender.Deliveries.Count == 1 && (await f.State()).Promotions.Count == 0,
            "an expired lease is recoverable after a worker crash");
    }

    private static async Task Cancellation(Action<bool, string> check)
    {
        foreach (var reason in new[] { "withdrawn", "removed", "disabled", "kickoff", "revoked" })
        {
            var f = await Fixture.Promoted();
            await f.Accounts.RegisterAsync("player", new("phone", "android"), default);
            switch (reason)
            {
                case "withdrawn": await f.Service.RespondOwnAsync("player", f.Invitation.Token, new("out"), default); break;
                case "removed": f.Groups.Players.RemoveAll(p => p.Key == "p2"); break;
                case "disabled": f.Groups.Enabled = false; break;
                case "kickoff": f.Clock.UtcNow = f.Invitation.ScheduledAt; break;
                case "revoked": await f.Service.RevokeAsync("owner", "group", f.Invitation.ScheduledAt.ToString("O"), default); break;
            }
            await f.Processor.ProcessAsync(f.Key, default);
            check(f.Sender.Attempts == 0 && (await f.State()).Promotions.Count == 0,
                $"{reason} promotion is cancelled without sending stale confirmation");
        }

        var retry = await Fixture.Promoted();
        await retry.Accounts.RegisterAsync("player", new("phone", "android"), default);
        retry.Sender.Results.Enqueue(PushResult.Retry);
        await retry.Processor.ProcessAsync(retry.Key, default);
        var due = (await retry.State()).Promotions.Single().RetryAt!.Value;
        await retry.Service.RespondOwnAsync("player", retry.Invitation.Token, new("out"), default);
        retry.Clock.UtcNow = due;
        await retry.Processor.ProcessAsync(retry.Key, default);
        check(retry.Sender.Attempts == 1, "withdrawal cancels a failed notification before its retry");

        var f2 = await Fixture.Promoted();
        await f2.Bind("p3", "next-player");
        await f2.Service.RespondOwnAsync("next-player", f2.Invitation.Token, new("in"), default);
        await f2.Service.RespondOwnAsync("player", f2.Invitation.Token, new("out"), default);
        var pending = (await f2.State()).Promotions;
        check(pending.Count == 1 && pending[0].ProfileId == PlayerProfileService.Id("next-player"),
            "successive withdrawals notify the next waiting player and cancel the old confirmation");
    }

    private static async Task DeviceOwnership(Action<bool, string> check)
    {
        var f = await Fixture.Promoted();
        await f.Accounts.RegisterAsync("player", new("phone", "android"), default);
        await f.Accounts.RegisterAsync("different-account", new("phone", "android"), default);
        await f.Processor.ProcessAsync(f.Key, default);
        check(f.Sender.Attempts == 0 && (await f.Accounts.GetAsync("different-account", default)).Devices.Count == 1,
            "switching accounts never delivers the previous player's promotion to the new account");

        f = await Fixture.Promoted();
        await f.Accounts.RegisterAsync("player", new("invalid-phone", "android"), default);
        f.Sender.Results.Enqueue(PushResult.InvalidDevice);
        await f.Processor.ProcessAsync(f.Key, default);
        check((await f.Accounts.GetAsync("player", default)).Devices.Count == 0
            && (await f.State()).Promotions.Count == 0, "invalid FCM device is removed without replay");

        f = await Fixture.Promoted();
        await f.Accounts.RegisterAsync("player", new("phone", "android"), default);
        var now = f.Clock.UtcNow;
        f.Clock.UtcNow = now.AddDays(-91);
        await f.Accounts.RegisterAsync("player", new("phone", "android"), default);
        f.Clock.UtcNow = now;
        await f.Processor.ProcessAsync(f.Key, default);
        check(f.Sender.Attempts == 0 && (await f.State()).Promotions.Count == 1,
            "an expired device is skipped even while its promotion is still eligible");

        f = await Fixture.Promoted();
        await f.Accounts.RegisterAsync("player", new("phone", "android"), default);
        var recipientKey = InvitationKeys.PlayerRecipient(PlayerProfileService.Id("player"));
        var recipient = (await f.Store.ReadAsync<PlayerNotificationRecipient>(recipientKey, default))!;
        await f.Store.WriteAsync(recipientKey, new PlayerNotificationRecipient("different-account"), recipient.ETag, default);
        try { await f.Processor.ProcessAsync(f.Key, default); check(false, "inconsistent recipient must fail"); }
        catch (InvalidDataException) { check(f.Sender.Attempts == 0, "an inconsistent recipient index fails closed without targeting another account"); }
    }

    private static async Task AuthenticationAndMessage(Action<bool, string> check)
    {
        var f = await Fixture.Promoted();
        var state = await f.State();
        var promotion = state.Promotions.Single();
        var message = FirebaseDigestSender.BuildPromotionMessage("phone", "player", state, promotion, "en", f.Clock.UtcNow);
        check(message.Notification.Title == "You're in!" && message.Notification.Body.Contains("waiting list"),
            "promotion notification explicitly says the player is now in");
        check(message.Data["invitationToken"] == f.Invitation.Token && message.Data["recipientUserId"] == "player"
            && message.Data["scheduledAt"] == state.ScheduledAt.UtcDateTime.ToString("O"),
            "notification opens the exact occurrence and identifies its signed-in recipient");
        check(message.Android.Notification.ChannelId == "matchday-promotions"
            && message.Android.Notification.Tag == promotion.Id && message.Android.CollapseKey == promotion.Id
            && message.Android.Priority == FirebaseAdmin.Messaging.Priority.High,
            "Android promotion has an urgent channel and stable deduplication identity");
        check(message.Android.TimeToLive <= TimeSpan.FromHours(6)
            && FirebaseDigestSender.BuildPromotionMessage("phone", "player", state, promotion, "he", f.Clock.UtcNow)
                .Notification.Title != message.Notification.Title, "promotion TTL is bounded and group language is respected");
        var sender = new DigestSender();
        var controller = new NotificationsController(f.Accounts, sender, Fixture.Configuration(true))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] {
                    new Claim("user_id", "guest"), new Claim("firebase", "{\"sign_in_provider\":\"anonymous\"}")
                }, "test"))
            }}
        };
        try { await controller.Register(new("guest-phone", "android"), default); check(false, "anonymous registration must fail"); }
        catch (InvitationException e) { check(e.StatusCode == 401, "anonymous Firebase sessions cannot register player devices"); }
        try { await controller.PromotionStatus(default); check(false, "anonymous status must fail"); }
        catch (InvitationException e) { check(e.StatusCode == 401, "promotion setup requires a registered account"); }
        controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("user_id", "player") }, "test"));
        var status = (OkObjectResult)await controller.PromotionStatus(default);
        check(JObject.FromObject(status.Value!)["deliveryConfigured"]!.Value<bool>(),
            "registered accounts can check configured promotion delivery");
        var disabled = new NotificationsController(f.Accounts, sender, Fixture.Configuration(false))
        {
            ControllerContext = controller.ControllerContext
        };
        status = (OkObjectResult)await disabled.PromotionStatus(default);
        check(!JObject.FromObject(status.Value!)["deliveryConfigured"]!.Value<bool>(),
            "disabled promotion feature cannot advertise delivery as configured");
    }

    private sealed class Fixture
    {
        public InvitationRegression.MemoryStore Store { get; } = new();
        public TestClock Clock { get; } = new();
        public TestGroups Groups { get; } = new();
        public TestSender Sender { get; } = new();
        public InvitationService Service { get; private set; } = null!;
        public NotificationAccountService Accounts { get; private set; } = null!;
        public InvitationPromotionProcessor Processor { get; private set; } = null!;
        public OrganizerInvitation Invitation { get; private set; } = null!;
        public string Key => InvitationKeys.State(Invitation.Id);
        private bool _linked;
        public static IConfiguration Configuration(bool enabled) => new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Notifications:Promotions:Enabled"] = enabled.ToString() }).Build();
        public static async Task<Fixture> Create(bool linked = true, bool enabled = true)
        {
            var f = new Fixture { _linked = linked };
            f.Service = new(f.Store, f.Groups, f.Clock, configuration: Configuration(enabled));
            f.Accounts = new(f.Store, f.Clock);
            f.Processor = new(f.Store, f.Groups, f.Accounts, f.Sender, f.Clock);
            f.Invitation = await f.Service.CreateAsync("owner",
                new("group", f.Clock.UtcNow.AddDays(2).ToString("O"), "UTC"), default);
            if (linked) await f.Bind("p2", "player");
            return f;
        }
        public static async Task<Fixture> Promoted()
        {
            var f = await Create();
            await f.Fill();
            await f.Service.RespondAsync(f.Invitation.Token, new("p1", "out"), default);
            return f;
        }
        public async Task Fill()
        {
            await Service.RespondAsync(Invitation.Token, new("p1", "in"), default);
            if (_linked) await Service.RespondOwnAsync("player", Invitation.Token, new("in"), default);
            else await Service.RespondAsync(Invitation.Token, new("p2", "in"), default);
        }
        public async Task Bind(string player, string uid)
        {
            var group = await Groups.LoadAsync("owner", "group", default);
            var profileId = PlayerProfileService.Id(uid);
            var groupKey = InvitationKeys.Hash(JsonConvert.SerializeObject(new[] { group.OwnerId, group.GroupId }));
            await Store.WriteAsync("player-memberships/v1/profiles/" + profileId + "/" + groupKey,
                new PlayerMembership("owner", "group", "Group", new() { player }), null, default);
            await Store.WriteAsync("player-membership-links/v1/groups/" + groupKey + "/" + player,
                new PlayerMembershipBinding(profileId), null, default);
        }
        public async Task<InvitationState> State() => (await Store.ReadAsync<InvitationState>(Key, default))!.Value;
    }

    private sealed class TestClock : IInvitationClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }
    private sealed class TestGroups : IInvitationGroupSource
    {
        public bool Enabled { get; set; } = true;
        public List<RosterPlayer> Players { get; } = new() { new("p1", "First"), new("p2", "Waiting"), new("p3", "Next") };
        public Task<InvitationGroup> ResolveAsync(string requesterId, string groupReference, CancellationToken ct) =>
            LoadAsync("owner", "group", ct);
        public Task<InvitationGroup> LoadAsync(string ownerId, string groupId, CancellationToken ct) =>
            Task.FromResult(new InvitationGroup(ownerId, groupId, "Group", "Weekly match", "Pitch", null, 1, Players, Enabled, "en"));
    }
    private sealed class TestSender : IInvitationPromotionPushSender
    {
        public bool Enabled { get; set; } = true;
        public int Attempts { get; private set; }
        public Queue<PushResult> Results { get; } = new();
        public List<string> Deliveries { get; } = new();
        public Task<PushResult> SendPromotionAsync(string token, string uid, InvitationState state,
            PromotionNotification promotion, string language, CancellationToken ct)
        {
            Attempts++;
            var result = Results.Count > 0 ? Results.Dequeue() : PushResult.Delivered;
            if (result == PushResult.Delivered) Deliveries.Add(token);
            return Task.FromResult(result);
        }
    }
    private sealed class DigestSender : IInvitationPushSender
    {
        public bool Enabled => true;
        public Task<bool> IsDeliveryConfiguredAsync(CancellationToken ct) => Task.FromResult(true);
        public Task<PushResult> SendAsync(string token, InvitationState state, DigestBatch batch, CancellationToken ct) =>
            Task.FromResult(PushResult.Delivered);
    }
}
