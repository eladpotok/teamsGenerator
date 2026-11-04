using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using TeamsGenerator.API;
using TeamsGeneratorWebAPI.Collaboration;
using TeamsGeneratorWebAPI.ConfigBlob;
using TeamsGeneratorWebAPI.Controllers;
using TeamsGeneratorWebAPI.Invitations;
using TeamsGeneratorWebAPI.PlayersBlob;
using TeamsGeneratorWebAPI.Premium;
using TeamsGeneratorWebAPI.Telemetry;

internal static class ConfigReadRegression
{
    public static async Task Run(Action<bool, string> check)
    {
        var stored = new Dictionary<string, UserConfigResponse>
        {
            ["owner/default"] = new() { TeamName = "Owner match", NumberOfTeams = 4, Language = "he" },
            ["owner/custom"] = new() { TeamName = "Custom match", NumberOfTeams = 3 },
            ["member/default"] = new() { TeamName = "Member match", NumberOfTeams = 5, Language = "en" }
        };
        string? failedKey = null;
        IResponse failure = GetConfigResponse.Failure("transient storage error");
        var writes = 0;
        var settings = CallbackProxy.Create<IUserConfigAzureStorage>((method, args) =>
        {
            var config = (UserConfigBlobConfig)args[method.Name == "UploadAsync" ? 1 : 0]!;
            var key = config.UId + "/" + config.GroupId;
            if (method.Name == "ListAsync")
                return Task.FromResult(key == failedKey ? failure : stored.TryGetValue(key, out var found)
                    ? new GetConfigResponse(JsonConvert.DeserializeObject<UserConfigResponse>(JsonConvert.SerializeObject(found))!)
                    : (IResponse)GetConfigResponse.Missing());
            if (method.Name == "UploadAsync")
            {
                writes++;
                stored[key] = (UserConfigResponse)args[0]!;
                return Task.FromResult<IResponse>(new SaveConfigResponse());
            }
            throw new InvalidOperationException(method.Name);
        });
        var accessAllowed = true;
        var groupsAvailable = true;
        var groupExists = true;
        var collaboration = CallbackProxy.Create<IGroupCollaborationService>((method, args) =>
            method.Name == "ResolveAccessAsync"
                ? Task.FromResult<GroupAccess?>(!accessAllowed ? null : new GroupAccess { OwnerId = "owner",
                    GroupId = args[1]?.ToString()?.Replace("shared.", "") ?? "default", IsOwner = args[0]?.ToString() == "owner" })
                : method.Name == "ListMembershipsAsync"
                    ? Task.FromResult<IReadOnlyList<GroupMembership>>(Array.Empty<GroupMembership>())
                    : throw new InvalidOperationException(method.Name));
        var players = CallbackProxy.Create<IPlayersStorageBlobConnector>((method, args) =>
            method.Name == "ListGroupsAsync"
                ? Task.FromResult(new PlayerGroupsResponse { Success = groupsAvailable, Groups = !groupExists
                    ? Array.Empty<PlayerGroup>() : new[]
                    { new PlayerGroup { Id = "default", IsDefault = true }, new PlayerGroup { Id = "custom" } } })
                : throw new InvalidOperationException("Unexpected roster read or migration: " + method.Name));
        var entitlements = CallbackProxy.Create<IAccountEntitlementService>((_, _) =>
            Task.FromResult(new AccountEntitlements { IsPremium = true }));
        var events = new List<string>();
        var telemetry = CallbackProxy.Create<IUsageTelemetry>((_, args) => { events.Add((string)args[0]!); return null; });
        var context = new ControllerContext { HttpContext = new DefaultHttpContext() };
        void User(string id) => context.HttpContext.User =
            new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("user_id", id) }, "test"));
        User("member");
        var controller = new ConfigController(NullLogger<HomeController>.Instance, settings, telemetry,
            entitlements, collaboration, players) { ControllerContext = context };
        using var cache = new MemoryCache(new MemoryCacheOptions());
        cache.Set("latest-release-version", "synthetic");
        var home = new HomeController(NullLogger<HomeController>.Instance, settings, telemetry, null!,
            entitlements, collaboration, players, cache) { ControllerContext = context };
        var before = JsonConvert.SerializeObject(stored);
        foreach (var key in stored.Keys.ToArray())
        {
            failedKey = key;
            foreach (var scope in new[] { "global", "group", "both" })
                check(!(await controller.Post("test", new UserConfigResponse(), "ignored", "shared.custom", scope)).Success,
                    $"failed {key} read blocks {scope} settings save");
            check((await controller.Get("test", "ignored", "shared.custom") as ObjectResult)?.StatusCode == 503,
                "config GET surfaces failed " + key + " without successful defaults");
            check((await home.Get("test", "ignored", "shared.custom")).Result is ObjectResult { StatusCode: 503 },
                "Home surfaces failed " + key + " without successful defaults");
        }
        check(writes == 0 && before == JsonConvert.SerializeObject(stored),
            "every required read completes successfully before any scoped write");
        check(events.Count == 0, "failed settings reads emit no successful setup or preference snapshots");
        User("owner");
        failedKey = "owner/default";
        foreach (var invalid in new IResponse[] { GetConfigResponse.Failure("config isn't found"),
            new GetConfigResponse(null!), new SaveConfigResponse() })
        {
            failure = invalid;
            check((await controller.Get("test", "ignored") as ObjectResult)?.StatusCode == 503,
                "absence text, malformed success and wrong response type are not confirmed absence");
            check(!(await controller.Post("test", new UserConfigResponse(), "ignored", scope: "global")).Success,
                "malformed config response cannot erase group fields on global save");
        }
        var invitation = new InvitationGroupSource(collaboration, players, settings, entitlements);
        failure = GetConfigResponse.Failure("config isn't found");
        try { await invitation.LoadAsync("owner", "default", default); check(false, "failed invitation config must throw"); }
        catch (InvitationException ex) { check(ex.StatusCode == 503, "invitation source uses explicit absence rather than error text"); }
        var groups = new UserPlayersController(NullLogger<UserPlayersController>.Instance, players, telemetry,
            entitlements, collaboration, null!, settings) { ControllerContext = context };
        check(!(await groups.GetGroups("ignored")).Success, "failed config does not report a default group-name migration as loaded");

        failedKey = null;
        var readOnlyGroups = await groups.GetGroups("ignored", readOnly: true);
        check(readOnlyGroups.Success && readOnlyGroups.Groups.Single(group => group.IsDefault).Name == "Owner match",
            "profile reads the legacy configured group name without persisting a migration");
        var previousName = stored["owner/default"].TeamName;
        stored["owner/default"].TeamName = "";
        readOnlyGroups = await groups.GetGroups("ignored", readOnly: true);
        check(readOnlyGroups.Success && readOnlyGroups.Groups.All(group => !group.IsDefault),
            "profile does not invent a managed default group before the organizer sets one up");
        stored["owner/default"].TeamName = previousName;
        check(writes == 0, "profile group discovery never writes settings or initializes a default group");
        groupsAvailable = false;
        check((await controller.Get("test", "ignored", "custom") as ObjectResult)?.StatusCode == 503,
            "group-list storage failure is not a missing group in config GET");
        check((await home.Get("test", "ignored", "custom")).Result is ObjectResult { StatusCode: 503 },
            "group-list storage failure is not a missing group in Home");
        groupsAvailable = true;
        groupExists = false;
        check(await controller.Get("test", "ignored", "custom") is NotFoundResult,
            "confirmed missing group remains 404 in config GET");
        check((await home.Get("test", "ignored", "custom")).Result is NotFoundResult,
            "confirmed missing group remains 404 in Home");
        groupExists = true;
        accessAllowed = false;
        check(await controller.Get("test", "ignored", "custom") is ForbidResult,
            "explicit access denial remains 403 in config GET");
        check((await home.Get("test", "ignored", "custom")).Result is ForbidResult,
            "explicit access denial remains 403 in Home");
        accessAllowed = true;
        context.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity());
        check(await controller.Get("test", "ignored", "shared.custom") is UnauthorizedResult,
            "anonymous shared-group config GET remains 401");
        check((await home.Get("test", "ignored", "shared.custom")).Result is UnauthorizedResult,
            "anonymous shared-group Home remains 401");
        User("owner");
        var saved = await controller.Post("test", new UserConfigResponse { Language = "en" }, "ignored", scope: "global");
        check(saved.Success && stored["owner/default"].TeamName == "Owner match"
            && stored["owner/default"].NumberOfTeams == 4 && stored["owner/default"].Language == "en",
            "successful global-only save preserves unrelated group settings");
        stored.Remove("owner/custom");
        var inherited = (GetConfigResponse)((OkObjectResult)await controller.Get("test", "ignored", "custom")).Value!;
        check(inherited.Success && inherited.Config.NumberOfTeams == 4,
            "confirmed missing custom configuration still inherits owner settings");
        stored.Clear();
        check(await controller.Get("test", "ignored") is OkObjectResult
            && (await home.Get("test", "ignored", "custom")).Result is OkObjectResult,
            "confirmed first-use absence still returns default config and setup");
        check((await controller.Post("test", new UserConfigResponse(), "ignored", scope: "global")).Success,
            "confirmed first-use absence permits initial save");
    }
}
