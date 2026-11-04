using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Azure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TeamsGeneratorWebAPI.Announcements;
using TeamsGeneratorWebAPI.Controllers;
using TeamsGeneratorWebAPI.Invitations;

internal static class AnnouncementRegression
{
    internal static async Task Run(Action<bool, string> check)
    {
        var documents = new InvitationRegression.MemoryStore { YieldReads = true };
        var settings = Options.Create(new AnnouncementOptions { Enabled = true, AdminUserIds = new[] { "admin" } });
        var clock = new Clock();
        var service = new AccountAnnouncementService(documents, settings, clock);
        var admin = User("admin");
        var alice = User("alice");
        var bob = User("bob");
        PublishAnnouncementRequest Draft(string audience = "all", string[]? targets = null) =>
            new(Guid.NewGuid().ToString(), "News", "Information for your account.", audience, targets, clock.Now.AddDays(7));
        await Expect(() => service.FeedAsync(new ClaimsPrincipal(), default), 401, "announcement_sign_in_required", check);
        await Expect(() => service.FeedAsync(User("guest", true), default), 401, "announcement_sign_in_required", check);
        await Expect(() => service.PublishAsync(alice, Draft(), default), 403, "announcement_admin_required", check);
        await Expect(() => service.ListAsync(alice, default), 403, "announcement_admin_required", check);
        settings.Value.AdminUserIds = Array.Empty<string>();
        await Expect(() => service.PublishAsync(admin, Draft(), default), 403, "announcement_admin_required", check);
        settings.Value.AdminUserIds = new[] { "admin" };
        settings.Value.Enabled = false;
        var disabled = await new AccountAnnouncementService(new UnavailableStore(), settings, clock).FeedAsync(alice, default);
        check(!disabled.Enabled && !disabled.CanManage && disabled.Items.Count == 0,
            "disabled announcements perform no storage work and expose no publishing capability");
        await Expect(() => service.PublishAsync(admin, Draft(), default), 403, "announcement_admin_required", check);
        settings.Value.Enabled = true;
        var allDraft = Draft();
        var all = await service.PublishAsync(admin, allDraft, default);
        var targeted = await service.PublishAsync(admin, Draft("accounts", new[] { "alice", "alice" }), default);
        check(targeted.TargetUserIds.SequenceEqual(new[] { "alice" }), "target IDs are normalized without changing their case-sensitive identity");
        check((await service.FeedAsync(alice, default)).Items.Count == 2
            && (await service.FeedAsync(bob, default)).Items.Single().Id == all.Id,
            "all-account and selected-account notices are filtered by authenticated identity");
        var exposed = JsonSerializer.Serialize(await service.FeedAsync(alice, default));
        check(!exposed.Contains("TargetUserIds") && !exposed.Contains("RequestHash") && !exposed.Contains("alice")
            && !(await service.FeedAsync(alice, default)).CanManage && (await service.FeedAsync(admin, default)).CanManage,
            "recipient feed excludes targeting and idempotency metadata and derives admin capability on the server");
        var same = await service.PublishAsync(admin, allDraft, default);
        check(JsonSerializer.Serialize(same) == JsonSerializer.Serialize(all) && (await service.ListAsync(admin, default)).Count == 2,
            "identical publish retry returns its original notice without a second broadcast");
        await Expect(() => service.PublishAsync(admin, allDraft with { Message = "Changed" }, default),
            409, "announcement_id_conflict", check);
        await Expect(() => service.DismissAsync(bob, targeted.Id, default), 404, "announcement_not_found", check);
        await service.DismissAsync(alice, all.Id, default);
        var restarted = new AccountAnnouncementService(documents, settings, clock);
        await restarted.DismissAsync(alice, all.Id, default);
        check((await restarted.FeedAsync(alice, default)).Items.Single().Id == targeted.Id
            && (await restarted.FeedAsync(bob, default)).Items.Single().Id == all.Id,
            "dismissal survives service/device recreation and cannot dismiss another account's notice");
        await Expect(() => service.WithdrawAsync(bob, all.Id, default), 403, "announcement_admin_required", check);
        await service.WithdrawAsync(admin, all.Id, default);
        await service.WithdrawAsync(admin, all.Id, default);
        check((await service.PublishAsync(admin, allDraft, default)).Withdrawn
            && (await service.FeedAsync(bob, default)).Items.Count == 0,
            "withdrawal and old publish retries cannot resurrect a notice");
        clock.Now = targeted.ExpiresAt;
        check((await service.FeedAsync(alice, default)).Items.Count == 0, "expiry removes notices without a background worker");

        foreach (var invalid in new[] {
            Draft() with { Title = " " }, Draft() with { Message = "" },
            Draft() with { Title = new string('a', 121) }, Draft() with { Message = new string('a', 2001) },
            Draft() with { Message = "bad\0message" }, Draft() with { Audience = "everyone" }
        })
            await Expect(() => service.PublishAsync(admin, invalid, default), 400, "announcement_invalid_content", check);
        foreach (var invalid in new[] {
            Draft("accounts"), Draft("all", new[] { "alice" }), Draft("accounts", new[] { "" }),
            Draft("accounts", new[] { "alice\n" }),
            Draft("accounts", Enumerable.Range(0, 51).Select(i => "target-" + i).ToArray())
        })
            await Expect(() => service.PublishAsync(admin, invalid, default), 400, "announcement_invalid_audience", check);
        await Expect(() => service.PublishAsync(admin, Draft() with { Id = "not-a-uuid" }, default), 400, "announcement_invalid_id", check);
        await Expect(() => service.PublishAsync(admin, Draft() with { ExpiresAt = clock.Now }, default), 400, "announcement_invalid_expiry", check);
        await Expect(() => service.PublishAsync(admin, Draft() with { ExpiresAt = clock.Now.AddDays(91) }, default), 400, "announcement_invalid_expiry", check);
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            try { await service.PublishAsync(admin, Draft(), cancellation.Token); check(false, "cancelled publish"); }
            catch (OperationCanceledException) { check(true, "cancelled publication cannot be accepted"); }
        }
        documents.FailNextWrites = 8;
        await Expect(() => service.PublishAsync(admin, Draft(), default), 409, "announcements_changed", check);
        var concurrent = await Task.WhenAll(service.PublishAsync(admin, Draft(), default),
            restarted.PublishAsync(admin, Draft(), default));
        check((await service.FeedAsync(bob, default)).Items.Count == 2
            && concurrent.Select(item => item.Id).Distinct().Count() == 2,
            "concurrent publishers preserve both notices through catalog ETag retries");
        await Task.WhenAll(service.DismissAsync(bob, concurrent[0].Id, default),
            restarted.DismissAsync(bob, concurrent[1].Id, default));
        check((await restarted.FeedAsync(bob, default)).Items.Count == 0, "concurrent device dismissals merge rather than overwrite");

        var raceDocuments = new InvitationRegression.MemoryStore();
        var baseService = new AccountAnnouncementService(raceDocuments, settings, clock);
        var old = await baseService.PublishAsync(admin, Draft(), default);
        var hooked = new BeforeDismissalRead(raceDocuments);
        hooked.Callback = async () =>
        {
            var added = await baseService.PublishAsync(admin, Draft(), default);
            await baseService.DismissAsync(alice, added.Id, default);
        };
        await new AccountAnnouncementService(hooked, settings, clock).DismissAsync(alice, old.Id, default);
        check((await baseService.FeedAsync(alice, default)).Items.Count == 0,
            "dismissal cannot prune another device's newly published notice using an older catalog snapshot");

        var bounded = new AccountAnnouncementService(new InvitationRegression.MemoryStore(), settings, clock);
        for (var i = 0; i < AnnouncementOptions.MaximumAnnouncements; i++) await bounded.PublishAsync(admin, Draft(), default);
        await Expect(() => bounded.PublishAsync(admin, Draft(), default), 409, "announcement_limit", check);
        clock.Now = clock.Now.AddDays(8);
        await bounded.PublishAsync(admin, Draft(), default);
        check((await bounded.ListAsync(admin, default)).Count == 1, "new publication prunes expired history within the bounded catalog");
        await CheckHttp(check);
    }

    private static async Task CheckHttp(Action<bool, string> check)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddControllers().AddApplicationPart(typeof(AnnouncementsController).Assembly).AddNewtonsoftJson();
        builder.Services.AddAuthentication("test").AddScheme<AuthenticationSchemeOptions, TestAuthentication>("test", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton<IInvitationDocumentStore>(new InvitationRegression.MemoryStore());
        builder.Services.AddSingleton<IOptions<AnnouncementOptions>>(Options.Create(
            new AnnouncementOptions { Enabled = true, AdminUserIds = new[] { "admin" } }));
        builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
        builder.Services.AddSingleton<AccountAnnouncementService>();
        builder.Services.AddScoped<AnnouncementExceptionFilter>();
        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync();
        try
        {
            using var http = new HttpClient { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
            var draft = new PublishAnnouncementRequest(Guid.NewGuid().ToString(), "News", "Plain <script>text</script>",
                "accounts", new[] { "alice" }, DateTimeOffset.UtcNow.AddDays(1));
            check((await http.GetAsync("/api/announcements")).StatusCode == HttpStatusCode.Unauthorized
                && (await http.PostAsJsonAsync("/api/announcements/admin", draft)).StatusCode == HttpStatusCode.Unauthorized,
                "HTTP feed and publishing routes require authenticated sessions");
            http.DefaultRequestHeaders.Authorization = new("Bearer", "alice");
            check((await http.PostAsJsonAsync("/api/announcements/admin?uid=admin", draft)).StatusCode == HttpStatusCode.Forbidden
                && (await http.GetAsync("/api/announcements/admin")).StatusCode == HttpStatusCode.Forbidden,
                "ordinary account and forged UID query cannot access publishing or the target list");
            http.DefaultRequestHeaders.Authorization = new("Bearer", "admin");
            using var created = await http.PostAsJsonAsync("/api/announcements/admin", draft);
            check(created.StatusCode == HttpStatusCode.OK, "configured administrator can publish through the real MVC contract");
            http.DefaultRequestHeaders.Authorization = new("Bearer", "alice");
            using var feed = await http.GetAsync("/api/announcements");
            using var json = JsonDocument.Parse(await feed.Content.ReadAsStringAsync());
            check(json.RootElement.GetProperty("enabled").GetBoolean()
                && !json.RootElement.GetProperty("canManage").GetBoolean()
                && json.RootElement.GetProperty("items").GetArrayLength() == 1
                && !json.RootElement.GetProperty("items")[0].TryGetProperty("targetUserIds", out _)
                && feed.Headers.CacheControl?.NoStore == true,
                "real feed is camelCase, private/no-store, and does not expose the admin audience contract");
            check((await http.PostAsync("/api/announcements/" + draft.Id + "/dismiss", null)).StatusCode == HttpStatusCode.NoContent,
                "dismissal wire contract confirms persisted completion with 204");
            http.DefaultRequestHeaders.Authorization = new("Bearer", "bob");
            check((await http.PostAsync("/api/announcements/" + draft.Id + "/dismiss?uid=alice", null)).StatusCode == HttpStatusCode.NotFound,
                "a forged recipient query cannot dismiss a targeted account's notice");
            http.DefaultRequestHeaders.Authorization = new("Bearer", "anonymous");
            check((await http.GetAsync("/api/announcements")).StatusCode == HttpStatusCode.Unauthorized,
                "Firebase anonymous sessions are not announcement accounts");
        }
        finally { await app.StopAsync(); }
    }

    private static ClaimsPrincipal User(string uid, bool anonymous = false) => new(new ClaimsIdentity(new[] {
        new Claim("user_id", uid), new Claim("firebase", anonymous ? """{"sign_in_provider":"anonymous"}""" : """{"sign_in_provider":"password"}""")
    }, "test"));

    private static async Task Expect(Func<Task> operation, int status, string code, Action<bool, string> check)
    {
        try { await operation(); check(false, "Expected announcement error " + code); }
        catch (AnnouncementException ex) { check(ex.Status == status && ex.Code == code, "Announcement error " + code); }
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 10, 17, 20, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class BeforeDismissalRead : IInvitationDocumentStore
    {
        private readonly IInvitationDocumentStore _inner;
        public Func<Task>? Callback;
        public BeforeDismissalRead(IInvitationDocumentStore inner) => _inner = inner;
        public async Task<StoredDocument<T>?> ReadAsync<T>(string key, CancellationToken ct)
        {
            if (key.StartsWith("account-announcements/v1/dismissals/", StringComparison.Ordinal) && Callback != null)
            {
                var callback = Callback;
                Callback = null;
                await callback();
            }
            return await _inner.ReadAsync<T>(key, ct);
        }
        public Task<bool> WriteAsync<T>(string key, T value, string? etag, CancellationToken ct) => _inner.WriteAsync(key, value, etag, ct);
        public Task<DocumentPage> ListAsync(string prefix, string? cursor, int size, CancellationToken ct) => _inner.ListAsync(prefix, cursor, size, ct);
    }

    private sealed class UnavailableStore : IInvitationDocumentStore
    {
        public Task<StoredDocument<T>?> ReadAsync<T>(string key, CancellationToken ct) => throw new RequestFailedException(503, "Synthetic unavailable store");
        public Task<bool> WriteAsync<T>(string key, T value, string? etag, CancellationToken ct) => throw new RequestFailedException(503, "Synthetic unavailable store");
        public Task<DocumentPage> ListAsync(string prefix, string? cursor, int size, CancellationToken ct) => throw new RequestFailedException(503, "Synthetic unavailable store");
    }

    private sealed class TestAuthentication : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public TestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
            : base(options, logger, encoder) { }
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var value = Request.Headers.Authorization.ToString();
            var uid = value.StartsWith("Bearer ", StringComparison.Ordinal) ? value[7..] : "";
            return Task.FromResult(uid is "admin" or "alice" or "bob" or "anonymous"
                ? AuthenticateResult.Success(new AuthenticationTicket(User(uid, uid == "anonymous"), Scheme.Name))
                : AuthenticateResult.NoResult());
        }
    }
}
