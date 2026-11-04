using System.Security.Claims;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TeamsGenerator.API;
using TeamsGeneratorWebAPI.Collaboration;
using TeamsGeneratorWebAPI.ConfigBlob;
using TeamsGeneratorWebAPI.Controllers;
using TeamsGeneratorWebAPI.PlayersBlob;
using TeamsGeneratorWebAPI.Premium;
using TeamsGeneratorWebAPI.Storage;
using TeamsGeneratorWebAPI.Telemetry;

internal static class UsageMetricsRegression
{
    public static async Task Run(Action<bool, string> check)
    {
        var context = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("user_id", "organizer") }, "test"))
        };
        context.Request.Headers["client_version"] = "20.11";
        using var channel = new MemoryChannel();
        using var configuration = new TelemetryConfiguration
        {
            ConnectionString = "InstrumentationKey=00000000-0000-0000-0000-000000000001",
            TelemetryChannel = channel
        };
        var usage = new UsageTelemetry(new TelemetryClient(configuration),
            new HostingEnvironment { EnvironmentName = "Production" },
            new HttpContextAccessor { HttpContext = context });
        usage.Track("Example", measurements: new Dictionary<string, double>
            { ["valid"] = 3, ["invalid"] = double.NaN });
        var emitted = channel.Events.Single();
        check(emitted.Properties["schema_version"] == "2"
            && emitted.Properties["client_version"] == "20.11", "metrics schema and header version");
        check(emitted.Context.User.Id.Length == 24 && emitted.Context.User.Id != "organizer"
            && emitted.Properties["identity_source"] == "authenticated", "metrics identity is hashed and authenticated");
        check(!emitted.Metrics.ContainsKey("invalid"), "nonfinite metrics are not emitted");
        usage.Track("Verification", "metrics-verification-private");
        check(channel.Events.Count == 1, "verification client telemetry is excluded consistently");
        usage.Track("ForgedLegacyUid", userId: "someone-else");
        check(channel.Events.Last().Context.User.Id == emitted.Context.User.Id
            && channel.Events.Last().Properties["identity_source"] == "authenticated",
            "authenticated actor always wins over a legacy supplied UID");
        usage.Track("Public", userId: "organizer",
            properties: new Dictionary<string, string?> { ["actor_type"] = "participant" });
        check(string.IsNullOrEmpty(channel.Events.Last().Context.User.Id)
            && channel.Events.Last().Properties["identity_source"] == "unattributed",
            "public requests cannot be attributed to an authenticated organizer");
        var authenticatedUser = context.User;
        context.User = new ClaimsPrincipal(new ClaimsIdentity());
        usage.Track("Legacy", userId: "organizer");
        check(channel.Events.Last().Context.User.Id == emitted.Context.User.Id
            && channel.Events.Last().Properties["identity_source"] == "legacy",
            "legacy and authenticated use retain the same pseudonym without claiming legacy authentication");
        usage.Track("Unknown");
        check(string.IsNullOrEmpty(channel.Events.Last().Context.User.Id)
            && channel.Events.Last().Properties["actor_type"] == "unknown",
            "missing identities cannot manufacture an organizer");
        context.User = authenticatedUser;
        check(UsageMetrics.ResourceKey("group", "ab", "c") != UsageMetrics.ResourceKey("group", "a", "bc")
            && UsageMetrics.ResourceKey("group", "ab", "c") != UsageMetrics.ResourceKey("matchday", "ab", "c"),
            "resource hashes separate components and domains");

        var telemetry = new RecordingTelemetry();
        var premium = new AccountEntitlements { IsPremium = true };
        var saved = new UserConfigResponse
        {
            ShareImageTemplate = "neon", EnableTimer = false, EnableMatchInvitations = true,
            TeamName = "PRIVATE MATCH", Location = "PRIVATE LOCATION", GroupLogoDataUrl = "PRIVATE IMAGE",
            Language = "PRIVATE LANGUAGE"
        };
        telemetry.TrackGlobalPreferences("test", "organizer", saved, premium, "loaded");
        var snapshot = telemetry.Events.Single();
        check(snapshot.Properties["template"] == "neon" && snapshot.Properties["timer_enabled"] == "false"
            && snapshot.Properties["preference_source"] == "saved", "global snapshot captures actual saved preferences");
        check(!JsonConvert.SerializeObject(snapshot).Contains("PRIVATE"),
            "preference snapshots exclude arbitrary strings and image data");
        telemetry.Events.Clear();
        telemetry.TrackGlobalPreferences("test", "organizer", null, premium, "loaded");
        check(telemetry.Events.Single().Properties["preference_source"] == "default",
            "defaults are distinguished from saved preferences");
        telemetry.Events.Clear();
        telemetry.TrackGroupPreferences("test", "owner", "owner", "group", saved, "loaded", "saved");
        telemetry.TrackGroupPreferences("test", "co-organizer", "owner", "group", saved, "loaded", "saved");
        check(telemetry.Events.Select(e => e.Properties["group_key"]).Distinct().Count() == 1
            && telemetry.Events.Select(e => e.UserId).Distinct().Count() == 2,
            "co-organizer observations keep actor identity but share a canonical group key");
        check(!JsonConvert.SerializeObject(telemetry.Events.Select(e => e.Properties)).Contains("PRIVATE"),
            "group snapshots never contain logo, location, names or private fields");
        await PreferenceControllers(check, telemetry, context);
        await Generation(check, telemetry, context);
        await ConfigReadRegression.Run(check);
        await RegistrationRegression.Run(check);
        await AuthenticatedAccountRegression.Run(check);
        await Skills(check, telemetry, context);
    }

    private static async Task Skills(Action<bool, string> check, RecordingTelemetry telemetry, DefaultHttpContext context)
    {
        telemetry.Events.Clear();
        var config = new UserConfigResponse();
        foreach (var algorithm in new[] { 0, 1, 2, 3 })
        {
            config.SelectedAlgoKey = algorithm;
            config.SkillDefinitions = null;
            telemetry.TrackGroupSkills("test", "member", "owner", "group", config, "loaded", "default");
            var emitted = telemetry.Events.Last().Properties;
            check(emitted["skill_count"] == "5" && emitted["custom_skill_count"] == "0"
                && emitted["algorithm_uses_configured_skills"] == UsageMetrics.Flag(algorithm is 0 or 2),
                "all algorithms resolve default definitions but only configurable algorithms consume them");
        }
        config.SkillDefinitions = SkillDefinition.CreateDefaults();
        config.SkillDefinitions[0].Name = "PRIVATE PERSON";
        config.SkillDefinitions.Add(new SkillDefinition { Id = "private_id", Name = "PRIVATE LABEL" });
        config.ArchivedSkillDefinitions = new() { new SkillDefinition { Id = "old", Name = "PRIVATE ARCHIVE" } };
        telemetry.TrackGroupSkills("test", "member", "owner", "group", config, "saved", "saved");
        var custom = telemetry.Events.Last().Properties;
        check(custom["custom_skill_count"] == "2" && custom["skill_count"] == "6"
            && !custom["standard_skills"]!.Contains("leadership"),
            "renaming a built-in and adding a skill both count as custom; old label is absent");
        check(custom["owner_key"] == UsageTelemetry.CreateAnonymousUserId("owner")
            && custom["owner_key"] != UsageTelemetry.CreateAnonymousUserId("member"),
            "skill adoption counts canonical owner, never observing collaborator");
        check(!JsonConvert.SerializeObject(telemetry.Events).Contains("PRIVATE")
            && !JsonConvert.SerializeObject(telemetry.Events).Contains("private_id"),
            "skill telemetry exports no free text labels, IDs, archived definitions or player data");
        check(telemetry.Events.All(e => e.Properties.Values.All(v => v == null || v.Length <= 128)),
            "complete skill snapshots fit the telemetry property limit without truncation");
        telemetry.TrackGroupSkills("test", "owner", "owner", "group", null, "deleted", "deleted", deleted: true);
        check(telemetry.Events.Last().Properties["standard_skills"] == "[]"
            && telemetry.Events.Last().Properties["skill_count"] == "0"
            && telemetry.Events.Last().Properties["deleted"] == "true",
            "deleted groups emit a complete empty snapshot that clears all prior skills");
        config.SkillDefinitions = new List<SkillDefinition>();
        telemetry.TrackGroupSkills("test", "owner", "owner", "group", config, "saved", "saved");
        check(telemetry.Events.Last().Properties["skill_count"] == "5"
            && telemetry.Events.Last().Properties["custom_skill_count"] == "0",
            "an empty saved definition list resets to application defaults, clearing previous custom skills");
        config.SkillDefinitions = Enumerable.Range(0, 8).Select(index =>
            new SkillDefinition { Id = $"custom_{index}", Name = $"PRIVATE {index}" }).ToList();
        telemetry.TrackGroupSkills("test", "owner", "owner", "ABCDEF", config, "saved", "saved");
        check(telemetry.Events.Last().Properties["standard_skills"] == "[]"
            && telemetry.Events.Last().Properties["custom_skill_count"] == "8",
            "a fully custom configuration emits an empty standard snapshot, not stale default labels");
        var upperKey = telemetry.Events.Last().Properties["group_key"];
        telemetry.TrackGroupSkills("test", "owner", "owner", "abcdef", null, "deleted", "deleted", deleted: true);
        check(telemetry.Events.Last().Properties["group_key"] == upperKey,
            "deletion uses the same canonical case normalization as storage group resolution");
        var deletionSuccess = true;
        var players = CallbackProxy.Create<IPlayersStorageBlobConnector>((method, args) =>
            Task.FromResult(deletionSuccess ? new SavePlayersResponse() : SavePlayersResponse.Failure("synthetic")));
        var controller = new UserPlayersController(NullLogger<UserPlayersController>.Instance, players, telemetry,
            null!, null!, null!, null!)
            { ControllerContext = new ControllerContext { HttpContext = context } };
        telemetry.Events.Clear();
        await controller.DeleteGroup("group", "untrusted");
        check(telemetry.Events.Single().Properties["deleted"] == "true"
            && telemetry.Events.Single().Properties["owner_key"] == UsageTelemetry.CreateAnonymousUserId("organizer"),
            "successful group deletion records a tombstone with authenticated owner");
        deletionSuccess = false;
        telemetry.Events.Clear();
        await controller.DeleteGroup("group", "untrusted");
        check(telemetry.Events.Count == 0, "failed deletion cannot erase the latest skill snapshot");
    }

    private static async Task PreferenceControllers(Action<bool, string> check,
        RecordingTelemetry telemetry, DefaultHttpContext context)
    {
        var stored = new Dictionary<string, UserConfigResponse>
        {
            ["default"] = new() { ShareImageTemplate = "neon", EnableTimer = true, EnableMatchInvitations = true },
            ["custom"] = new() { SelectedAlgoKey = 2, NumberOfTeams = 4 }
        };
        var failGlobal = false;
        var readError = false;
        var missing = false;
        var settings = CallbackProxy.Create<IUserConfigAzureStorage>((method, args) =>
        {
            var key = ((UserConfigBlobConfig)args[method.Name == "UploadAsync" ? 1 : 0]!).GroupId ?? "default";
            if (method.Name == "ListAsync")
            {
                if (readError)
                    return Task.FromResult<IResponse>(GetConfigResponse.Failure("storage unavailable"));
                if (missing)
                    return Task.FromResult<IResponse>(GetConfigResponse.Missing());
                return Task.FromResult<IResponse>(stored.TryGetValue(key, out var found)
                    ? new GetConfigResponse(JsonConvert.DeserializeObject<UserConfigResponse>(
                        JsonConvert.SerializeObject(found))!)
                    : GetConfigResponse.Missing());
            }
            if (method.Name == "UploadAsync")
            {
                if (failGlobal && key == "default")
                    return Task.FromResult<IResponse>(new SaveConfigResponse { Success = false });
                stored[key] = (UserConfigResponse)args[0]!;
                return Task.FromResult<IResponse>(new SaveConfigResponse { Success = true });
            }
            throw new InvalidOperationException("Unexpected config call.");
        });
        var collaboration = CallbackProxy.Create<IGroupCollaborationService>((method, args) =>
            Task.FromResult<GroupAccess?>(new GroupAccess
                { OwnerId = "organizer", GroupId = (string?)args[1] ?? "default", IsOwner = true }));
        var players = CallbackProxy.Create<IPlayersStorageBlobConnector>((method, args) =>
            Task.FromResult(new PlayerGroupsResponse { Success = true, Groups = new[]
                { new PlayerGroup { Id = "default" }, new PlayerGroup { Id = "custom" } } }));
        var isPremium = true;
        var entitlements = CallbackProxy.Create<IAccountEntitlementService>((method, args) =>
            Task.FromResult(new AccountEntitlements { IsPremium = isPremium }));
        var controller = new ConfigController(NullLogger<HomeController>.Instance, settings, telemetry,
            entitlements, collaboration, players)
            { ControllerContext = new ControllerContext { HttpContext = context } };
        telemetry.Events.Clear();
        var legacy = JsonConvert.DeserializeObject<UserConfigResponse>("""{"enableTimer":false,"shareImageTemplate":"stadium"}""")!;
        check((await controller.Post("old", legacy, "untrusted", "default", "global")).Success,
            "legacy settings save contract remains successful");
        var snapshot = telemetry.Events.Single(e => e.Name == "GlobalPreferencesObserved");
        check(snapshot.UserId == "organizer" && snapshot.Properties["invitations_enabled"] == "true",
            "metrics use authenticated identity and preserved omitted invitation field");
        check(telemetry.Events.All(e => e.Name != "GroupPreferencesObserved"),
            "global-only save never reports group preferences");
        check(telemetry.Events.Count(e => e.Name == "PreferenceChanged"
            && e.Properties["setting"] == "timer_enabled") == 1,
            "successful save emits an actual timer preference transition");
        telemetry.Events.Clear();
        await controller.Post("old", legacy, "untrusted", "default", "global");
        check(telemetry.Events.All(e => e.Name != "PreferenceChanged"), "identical retries do not count preference changes");
        telemetry.Events.Clear();
        await controller.Post("test", new UserConfigResponse { SelectedAlgoKey = 1 }, "organizer", "custom", "group");
        check(telemetry.Events.Any(e => e.Name == "GroupPreferencesObserved")
            && telemetry.Events.All(e => e.Name != "GlobalPreferencesObserved"),
            "group-only save never fabricates global preferences");
        failGlobal = true;
        telemetry.Events.Clear();
        check(!(await controller.Post("test", new UserConfigResponse { SelectedAlgoKey = 2 },
            "organizer", "custom")).Success, "partial configuration failure remains failure");
        check(telemetry.Events.Count(e => e.Name == "GroupPreferencesObserved") == 1
            && telemetry.Events.All(e => e.Name != "GlobalPreferencesObserved"),
            "partial save observes only successfully persisted scope");
        telemetry.Events.Clear();
        await controller.Post("test", legacy, "organizer", "default", "global");
        check(telemetry.Events.All(e => e.Name is not "GlobalPreferencesObserved" and not "PreferenceChanged"),
            "failed global saves cannot change preference statistics");

        failGlobal = false;
        isPremium = false;
        stored["default"].ShareImageTemplate = "neon";
        using var cache = new MemoryCache(new MemoryCacheOptions());
        cache.Set("latest-release-version", "test");
        var home = new HomeController(NullLogger<HomeController>.Instance, settings, telemetry, null!,
            entitlements, collaboration, players, cache)
            { ControllerContext = new ControllerContext { HttpContext = context } };
        telemetry.Events.Clear();
        var setup = (GetAppSetupResponse)((OkObjectResult)(await home.Get("test", "untrusted")).Result!).Value!;
        check(setup.Config.ShareImageTemplate == "classic"
            && telemetry.Events.Single(e => e.Name == "GlobalPreferencesObserved").Properties["template"] == "neon",
            "stored preferences are not overwritten by effective entitlement fallback in metrics");
        check(telemetry.Events.Single(e => e.Name == "GlobalPreferencesObserved").Properties["account_tier"] == "free",
            "preference adoption can be segmented by entitlement");
        readError = true;
        telemetry.Events.Clear();
        await home.Get("test", "organizer", "custom");
        check(telemetry.Events.All(e => e.Name is not "GlobalPreferencesObserved" and not "GroupPreferencesObserved"),
            "failed configuration reads never overwrite last-known saved preferences");
        readError = false;
        missing = true;
        telemetry.Events.Clear();
        await home.Get("test", "organizer", "custom");
        check(telemetry.Events.Count(e => e.Name.EndsWith("PreferencesObserved")
            && e.Properties["preference_source"] == "default") == 2,
            "confirmed missing configuration can safely report defaults");
        check(!JsonConvert.SerializeObject(GetConfigResponse.Missing()).Contains("IsMissing")
            && !System.Text.Json.JsonSerializer.Serialize(GetConfigResponse.Missing()).Contains("IsMissing"),
            "storage absence metadata does not change either JSON API contract");
    }

    private static async Task Generation(Action<bool, string> check, RecordingTelemetry telemetry, DefaultHttpContext context)
    {
        var isPremium = true;
        var entitlements = CallbackProxy.Create<IAccountEntitlementService>((method, args) =>
            Task.FromResult(new AccountEntitlements { IsPremium = isPremium }));
        var controller = new TeamsController(NullLogger<TeamsController>.Instance, telemetry, null!, null!,
            null!, entitlements, null!)
            { ControllerContext = new ControllerContext { HttpContext = context } };
        var config = WebAppAPI.GetAppSetup("test", new UserConfigResponse { NumberOfTeams = 2 }).Config;
        var body = new JObject
        {
            ["config"] = JObject.FromObject(config),
            ["players"] = new JArray(Enumerable.Range(1, 6).Select(index => new JObject
            {
                ["key"] = $"p{index}", ["name"] = "PRIVATE PLAYER", ["rank"] = index,
                ["attack"] = index, ["defence"] = index, ["passing"] = index,
                ["leadership"] = index, ["stamina"] = index, ["isArrived"] = true
            }))
        };
        telemetry.Events.Clear();
        var response = await controller.Post("20.11", body, 0);
        check(response.Result is OkObjectResult, "generation response remains unchanged");
        var generated = telemetry.Events.Single();
        check(generated.Name == "TeamsGenerated" && generated.Properties["outcome"] == "succeeded"
            && generated.Properties["algorithm_name"] == "skillwise"
            && generated.Measurements["team_count"] == 2 && generated.Measurements["player_count"] == 6,
            "successful generations report algorithm and counts");
        check(generated.Properties["chemistry_eligible"] == "true", "generation includes chemistry entitlement");
        check(generated.Properties["chemistry_applied"] == "false"
            && generated.Measurements["duration_ms"] >= 0
            && !JsonConvert.SerializeObject(generated).Contains("PRIVATE"),
            "generation metrics include duration but never player content");
        telemetry.Events.Clear();
        try
        {
            await controller.Post("20.11", body, 99);
            check(false, "invalid algorithm must still throw");
        }
        catch (KeyNotFoundException)
        {
            check(telemetry.Events.Single().Properties["outcome"] == "failed", "generation exceptions are measured and propagate");
        }
        isPremium = false;
        telemetry.Events.Clear();
        await controller.Post("20.11", body, 0);
        check(telemetry.Events.Single().Properties["chemistry_eligible"] == "false",
            "supported algorithm does not falsely make free accounts chemistry-eligible");
        telemetry.Events.Clear();
        var denied = await controller.Post("20.11", body, 3);
        check(denied.Result is ObjectResult { StatusCode: 403 }
            && telemetry.Events.Single().Properties["outcome"] == "denied", "premium denials cannot count as generated teams");
    }

    private sealed record CapturedEvent(string Name, string? UserId,
        Dictionary<string, string?> Properties, Dictionary<string, double> Measurements);

    private sealed class RecordingTelemetry : IUsageTelemetry
    {
        public List<CapturedEvent> Events { get; } = new();
        public void Track(string eventName, string? clientVersion = null, string? userId = null,
            IReadOnlyDictionary<string, string?>? properties = null,
            IReadOnlyDictionary<string, double>? measurements = null) =>
            Events.Add(new(eventName, userId, properties?.ToDictionary(p => p.Key, p => p.Value) ?? new(),
                measurements?.ToDictionary(p => p.Key, p => p.Value) ?? new()));
    }

    private sealed class MemoryChannel : ITelemetryChannel
    {
        public List<EventTelemetry> Events { get; } = new();
        public bool? DeveloperMode { get; set; }
        public string EndpointAddress { get; set; } = "";
        public void Send(ITelemetry item) { if (item is EventTelemetry e) Events.Add(e); }
        public void Flush() { }
        public void Dispose() { }
    }
}
