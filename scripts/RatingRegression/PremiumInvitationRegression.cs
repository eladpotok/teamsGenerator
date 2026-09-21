using System.Security.Claims;
using System.Text;
using Azure;
using Azure.Data.Tables;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using TeamsGenerator.Algos.SkillWiseAlgo;
using TeamsGenerator.API;
using TeamsGeneratorWebAPI.Collaboration;
using TeamsGeneratorWebAPI.ConfigBlob;
using TeamsGeneratorWebAPI.Controllers;
using TeamsGeneratorWebAPI.Invitations;
using TeamsGeneratorWebAPI.PlayersBlob;
using TeamsGeneratorWebAPI.Premium;
using TeamsGeneratorWebAPI.Telemetry;

internal static class PremiumInvitationRegression
{
    public static async Task Run(Action<bool, string> check)
    {
        var table = new MemoryTable();
        const string email = "owner@example.test";
        var emailKey = Convert.ToBase64String(Encoding.UTF8.GetBytes(email))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var grant = new PremiumAccountEntity
            { PartitionKey = "Email", RowKey = emailKey, IsPremium = true };
        table.Rows[("Email", emailKey)] = grant;
        var configuration = new ConfigurationBuilder().Build();
        var accounts = new PremiumAccountStore(new MemoryTables(table), configuration);
        var entitlements = new AccountEntitlementService(configuration, accounts);
        var owner = new UserConfigResponse();
        var member = new UserConfigResponse();
        var settings = CallbackProxy.Create<IUserConfigAzureStorage>((method, args) =>
        {
            if (method.Name == "ListAsync")
                return Task.FromResult<IResponse>(new GetConfigResponse(
                    ((UserConfigBlobConfig)args[0]!).UId == "owner" ? owner : member));
            if (method.Name == "UploadAsync")
            {
                owner = (UserConfigResponse)args[0]!;
                return Task.FromResult<IResponse>(new SaveConfigResponse { Success = true });
            }
            throw new InvalidOperationException("Unexpected config operation.");
        });
        var collaboration = CallbackProxy.Create<IGroupCollaborationService>((method, args) =>
            Task.FromResult<GroupAccess?>(new GroupAccess
                { OwnerId = "owner", GroupId = "default", IsOwner = (string)args[0]! == "owner" }));
        var players = CallbackProxy.Create<IPlayersStorageBlobConnector>((method, args) =>
            method.Name == "ListGroupsAsync"
                ? Task.FromResult(new PlayerGroupsResponse { Success = true,
                    Groups = new[] { new PlayerGroup { Id = "default", Name = "Group" } } })
                : method.Name == "ListAsync"
                    ? Task.FromResult<IResponse>(new GetPlayersResponse(new[]
                        { new SkillWisePlayer { Key = "p1", Name = "Player" } }))
                    : throw new InvalidOperationException("Unexpected roster operation."));
        var telemetry = CallbackProxy.Create<IUsageTelemetry>((method, args) => null);
        var identity = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("user_id", "owner"), new Claim("email", email), new Claim("email_verified", "true")
        }, "test"));
        var context = new ControllerContext { HttpContext = new DefaultHttpContext { User = identity } };
        await accounts.UpdateVerifiedIdentityAsync(identity);
        var config = new ConfigController(NullLogger<HomeController>.Instance, settings, telemetry,
            entitlements, collaboration, players) { ControllerContext = context };
        check((await config.Post("test", new UserConfigResponse { EnableMatchInvitations = true },
            "untrusted", "default", "global")).Success && owner.EnableMatchInvitations,
            "verified email-only Premium owner can save invitation toggle");
        foreach (var scope in new string?[] { "global", null })
        {
            var legacyConfig = JsonConvert.DeserializeObject<UserConfigResponse>(
                "{\"language\":\"he\",\"matchTimeMinutes\":12}")!;
            check((await config.Post("test", legacyConfig, "untrusted", "default", scope)).Success
                && owner.EnableMatchInvitations && owner.Language == "he",
                "old client omission preserves enabled invitation preference for scope " + (scope ?? "combined"));
        }
        check(!JsonConvert.SerializeObject(owner).Contains("HasEnableMatchInvitations"),
            "invitation field-presence metadata is not part of the public or persisted contract");
        check((await config.Post("test", JsonConvert.DeserializeObject<UserConfigResponse>(
            "{\"enableMatchInvitations\":false}")!, "untrusted", "default", "global")).Success
            && !owner.EnableMatchInvitations, "explicit false still disables invitations");
        check((await config.Post("test", JsonConvert.DeserializeObject<UserConfigResponse>("{}")!,
            "untrusted", "default", "global")).Success && !owner.EnableMatchInvitations,
            "old client omission preserves disabled invitation preference");
        check((await config.Post("test", new UserConfigResponse { EnableMatchInvitations = true },
            "untrusted", "default", "global")).Success && owner.EnableMatchInvitations,
            "Premium owner can explicitly reenable invitations");
        using var cache = new MemoryCache(new MemoryCacheOptions());
        cache.Set("latest-release-version", "test");
        var home = new HomeController(NullLogger<HomeController>.Instance, settings, telemetry, null!,
            entitlements, collaboration, players, cache) { ControllerContext = context };
        async Task<GetAppSetupResponse> Setup() =>
            (GetAppSetupResponse)((OkObjectResult)(await home.Get("test", "untrusted", "shared.default")).Result!).Value!;
        var setup = await Setup();
        check(setup.Entitlements.IsPremium && setup.Config.EnableMatchInvitations && setup.InvitationAccess.Enabled,
            "email-only Premium save must also enable canonical owner invitation policy");
        check(!await accounts.IsPremiumAsync("untrusted")
            && table.Rows.Keys.All(key => key.Partition != "Account"),
            "verified email binding neither trusts query UID nor manufactures an Account grant");
        var bindingWrites = table.Writes;
        await accounts.UpdateVerifiedIdentityAsync(identity);
        check(table.Writes == bindingWrites, "unchanged verified identity needs no repeated storage write");

        accounts = new PremiumAccountStore(new MemoryTables(table), configuration);
        var restartedEntitlements = new AccountEntitlementService(configuration, accounts);
        check((await restartedEntitlements.GetAsync("owner")).CanUseMatchInvitations,
            "UID-only email entitlement survives a new account service without an owner session");
        var source = new InvitationGroupSource(collaboration, players, settings, restartedEntitlements);
        var documents = new InvitationRegression.MemoryStore();
        var service = new InvitationService(documents, source, new InvitationClock());
        var ct = CancellationToken.None;
        var request = new CreateInvitationRequest("default", DateTimeOffset.UtcNow.AddDays(7).ToString("O"), "UTC");
        var invite = await service.CreateAsync("owner", request, ct);
        context.HttpContext.User = Principal("member", "member@example.test");
        await accounts.UpdateVerifiedIdentityAsync(context.HttpContext.User);
        setup = await Setup();
        check(setup.InvitationAccess.Enabled && !setup.Entitlements.IsPremium && !setup.Config.EnableMatchInvitations,
            "free co-organizer sees email-Premium owner's policy without inheriting owner entitlement/preferences");
        check((await service.CreateAsync("member", request with { GroupId = "shared.default" }, ct)).Id == invite.Id,
            "free co-organizer creation uses same email-Premium canonical owner");
        check((await service.RespondAsync(invite.Token, new("p1", "in"), ct)).Players.Single().Status == "in",
            "anonymous RSVP resolves email-Premium owner without any supplied identity or email");
        var before = await documents.ReadAsync<InvitationState>(InvitationKeys.State(invite.Id), ct);
        table.Rows[("Account", "member")] = new PremiumAccountEntity
            { PartitionKey = "Account", RowKey = "member", IsPremium = true };
        foreach (var change in new[] { "disabled", "deleted", "expired", "toggle" })
        {
            grant.IsPremium = change != "disabled";
            grant.ExpiresAt = change == "expired" ? DateTimeOffset.UtcNow.AddSeconds(-1) : null;
            if (change == "deleted") table.Rows.Remove(("Email", emailKey));
            owner.EnableMatchInvitations = change != "toggle";
            check(!(await Setup()).InvitationAccess.Enabled,
                "live owner policy rejects " + change + " email grant/setting despite Premium member");
            check((await service.PublicAsync(invite.Token, ct)).Status == "closed",
                "anonymous read closes original occurrence for " + change);
            await Error(() => service.CreateAsync("member", request, ct), 403, check);
            await Error(() => service.RespondAsync(invite.Token, new("p1", "out"), ct), 409, check);
            var after = await documents.ReadAsync<InvitationState>(InvitationKeys.State(invite.Id), ct);
            check(after!.ETag == before!.ETag && JsonConvert.SerializeObject(after.Value) == JsonConvert.SerializeObject(before.Value),
                "rejected " + change + " policy does not mutate responses/journal/delivery");
            grant.IsPremium = true;
            grant.ExpiresAt = null;
            table.Rows[("Email", emailKey)] = grant;
            owner.EnableMatchInvitations = true;
            check((await service.PublicAsync(invite.Token, ct)).Status == "open",
                "restored live grant/setting reopens original future occurrence after " + change);
        }

        await accounts.UpdateVerifiedIdentityAsync(Principal("owner", "replacement@example.test"));
        check(!await accounts.IsPremiumAsync("owner"), "verified email change does not retain previous email's grant");
        check(await accounts.IsPremiumAsync("owner", email), "explicit verified email path remains supported");
        check(!await accounts.IsPremiumAsync("owner"), "entitlement reads cannot modify the validated identity binding");
        await accounts.UpdateVerifiedIdentityAsync(identity);
        await accounts.UpdateVerifiedIdentityAsync(Principal("owner", email, "false"));
        check(!await accounts.IsPremiumAsync("owner"), "unverified email clears previously verified binding");
        await accounts.UpdateVerifiedIdentityAsync(identity);
        await accounts.UpdateVerifiedIdentityAsync(Principal("owner", email, null));
        check(!await accounts.IsPremiumAsync("owner"), "missing email verification clears previous binding");
        await accounts.UpdateVerifiedIdentityAsync(identity);
        await accounts.UpdateVerifiedIdentityAsync(Principal("owner", null));
        check(!await accounts.IsPremiumAsync("owner"), "missing email clears previous binding");
        await accounts.UpdateVerifiedIdentityAsync(Principal("stranger", email, "false"));
        check(!await accounts.IsPremiumAsync("stranger"), "unverified claimed Premium email cannot establish entitlement");
        foreach (var invalid in new[] {
            new ClaimsPrincipal(new ClaimsIdentity(identity.Claims)),
            new ClaimsPrincipal(new ClaimsIdentity(identity.Claims.Where(claim => claim.Type != "user_id"), "test")) })
        {
            var writes = table.Writes;
            try
            {
                await accounts.UpdateVerifiedIdentityAsync(invalid);
                throw new Exception("Invalid identity was accepted.");
            }
            catch (InvalidOperationException)
            {
                check(table.Writes == writes, "anonymous/missing UID principal cannot persist a binding");
            }
        }
        await accounts.UpdateVerifiedIdentityAsync(Principal("owner", " OWNER@EXAMPLE.TEST "));
        check(await accounts.IsPremiumAsync("owner"), "verified email binding follows existing grant normalization");
        table.Rows[("Account", "owner")] = new PremiumAccountEntity
            { PartitionKey = "Account", RowKey = "owner", IsPremium = true };
        await accounts.UpdateVerifiedIdentityAsync(Principal("owner", null));
        check(await accounts.IsPremiumAsync("owner"), "independent UID grants still work without a verified email");
        ((PremiumAccountEntity)table.Rows[("Account", "owner")]).ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1);
        check(!await accounts.IsPremiumAsync("owner"), "independent UID grant expiry is preserved");
        table.FailWrites = true;
        try
        {
            await accounts.UpdateVerifiedIdentityAsync(identity);
            throw new Exception("Binding storage failure was swallowed.");
        }
        catch (RequestFailedException ex)
        {
            check(ex.Status == 503, "binding storage failures surface rather than authenticate with stale policy");
        }
    }

    private static ClaimsPrincipal Principal(string uid, string? email, string? verified = "true")
    {
        var claims = new List<Claim> { new("user_id", uid) };
        if (email != null) claims.Add(new("email", email));
        if (verified != null) claims.Add(new("email_verified", verified));
        return new(new ClaimsIdentity(claims, "test"));
    }

    private static async Task Error(Func<Task> action, int status, Action<bool, string> check)
    {
        try { await action(); throw new Exception("Expected invitation rejection."); }
        catch (InvitationException ex) { check(ex.StatusCode == status, "email grant invitation rejection " + status); }
    }

    private sealed class MemoryTables : TableServiceClient
    {
        private readonly MemoryTable _table;
        public MemoryTables(MemoryTable table) => _table = table;
        public override TableClient GetTableClient(string tableName) => _table;
    }

    private sealed class MemoryTable : TableClient
    {
        public Dictionary<(string Partition, string Key), ITableEntity> Rows { get; } = new();
        public int Writes { get; private set; }
        public bool FailWrites { get; set; }
        public override Response<Azure.Data.Tables.Models.TableItem> CreateIfNotExists(
            CancellationToken cancellationToken = default) =>
            Response.FromValue<Azure.Data.Tables.Models.TableItem>(null!, new TableResponse());

        public override Task<NullableResponse<T>> GetEntityIfExistsAsync<T>(string partitionKey, string rowKey,
            IEnumerable<string>? select = null, CancellationToken cancellationToken = default)
        {
            Rows.TryGetValue((partitionKey, rowKey), out var entity);
            return Task.FromResult<NullableResponse<T>>(Response.FromValue((T)entity!, new TableResponse()));
        }

        public override Task<Response> UpsertEntityAsync<T>(T entity, TableUpdateMode mode = TableUpdateMode.Merge,
            CancellationToken cancellationToken = default)
        {
            if (FailWrites) throw new RequestFailedException(503, "Injected in-memory failure.");
            Writes++;
            Rows[(entity.PartitionKey, entity.RowKey)] = entity;
            return Task.FromResult<Response>(new TableResponse());
        }
    }

    private sealed class TableResponse : Response
    {
        public override int Status => 200;
        public override string ReasonPhrase => "OK";
        public override Stream? ContentStream { get; set; }
        public override string ClientRequestId { get; set; } = "";
        public override void Dispose() { }
        protected override bool ContainsHeader(string name) => false;
        protected override IEnumerable<Azure.Core.HttpHeader> EnumerateHeaders() => Array.Empty<Azure.Core.HttpHeader>();
        protected override bool TryGetHeader(string name, out string value) { value = ""; return false; }
        protected override bool TryGetHeaderValues(string name, out IEnumerable<string> values)
        { values = Array.Empty<string>(); return false; }
    }
}
