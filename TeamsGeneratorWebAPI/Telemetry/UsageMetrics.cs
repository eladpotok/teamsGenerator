using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using TeamsGenerator.API;
using TeamsGeneratorWebAPI.ConfigBlob;
using TeamsGeneratorWebAPI.Premium;
using TeamsGeneratorWebAPI.PlayersBlob;

namespace TeamsGeneratorWebAPI.Telemetry;

public static class UsageMetrics
{
    public static bool IsVerificationClient(string? version) =>
        version?.Trim().StartsWith("metrics-verification", StringComparison.OrdinalIgnoreCase) == true;

    public static string Flag(bool value) => value ? "true" : "false";

    public static string ResourceKey(string kind, params string[] parts) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"teamify-metrics-v2:{kind}:" + System.Text.Json.JsonSerializer.Serialize(parts))))
            .ToLowerInvariant();

    public static string AlgorithmName(int algorithm) => algorithm switch
    {
        0 => "skillwise",
        1 => "back_and_forth",
        2 => "positions",
        3 => "ai",
        _ => "unknown"
    };

    public static void TrackGlobalPreferences(
        this IUsageTelemetry telemetry, string? version, string userId,
        UserConfigResponse? config, AccountEntitlements entitlements,
        string reason, UserConfigResponse? previous = null)
    {
        var properties = GlobalProperties(config ?? new UserConfigResponse(), entitlements);
        properties["preference_source"] = config == null ? "default" : "saved";
        properties["observation_reason"] = reason;
        properties["preference_scope"] = "global";
        telemetry.Track("GlobalPreferencesObserved", version, userId, properties);
        if (reason == "saved" && previous != null)
        {
            TrackChanges(telemetry, version, userId, properties,
                GlobalProperties(previous, entitlements));
        }
    }

    public static void TrackGroupPreferences(
        this IUsageTelemetry telemetry, string? version, string actorId,
        string ownerId, string groupId, UserConfigResponse? config,
        string reason, string source, UserConfigResponse? previous = null)
    {
        var properties = GroupProperties(config ?? new UserConfigResponse());
        properties["group_key"] = ResourceKey("group", ownerId, groupId);
        properties["preference_source"] = source;
        properties["observation_reason"] = reason;
        properties["preference_scope"] = "group";
        telemetry.Track("GroupPreferencesObserved", version, actorId, properties);
        telemetry.TrackGroupSkills(version, actorId, ownerId, groupId, config, reason, source);
        if (reason == "saved" && previous != null)
        {
            TrackChanges(telemetry, version, actorId, properties, GroupProperties(previous));
        }
    }

    public static void TrackGroupSkills(
        this IUsageTelemetry telemetry, string? version, string actorId,
        string ownerId, string groupId, UserConfigResponse? config,
        string reason, string source, bool deleted = false)
    {
        var skills = deleted ? new List<SkillDefinition>()
            : SkillDefinition.Normalize(config?.SkillDefinitions);
        var defaults = SkillDefinition.CreateDefaults();
        // A renamed built-in ID is custom too. Never export arbitrary labels or IDs:
        // skill fields can contain personal information, not just football terminology.
        var standardSkills = skills.Where(skill => defaults.Any(standard =>
            standard.Id == skill.Id
            && string.Equals(standard.Name, skill.Name, StringComparison.OrdinalIgnoreCase)))
            .Select(skill => skill.Id).OrderBy(id => id, StringComparer.Ordinal).ToList();
        telemetry.Track("GroupSkillsObserved", version, actorId, new Dictionary<string, string?>
        {
            ["skills_version"] = "1",
            ["group_key"] = ResourceKey("group", ownerId, PlayersStorageBlobConnector.NormalizeGroupId(groupId)),
            ["owner_key"] = UsageTelemetry.CreateAnonymousUserId(ownerId),
            ["observation_reason"] = reason,
            ["preference_source"] = source,
            ["deleted"] = Flag(deleted),
            ["algorithm_name"] = deleted ? "none" : AlgorithmName(config?.SelectedAlgoKey ?? 0),
            ["algorithm_uses_configured_skills"] = Flag(!deleted && (config?.SelectedAlgoKey ?? 0) is 0 or 2),
            ["standard_skills"] = System.Text.Json.JsonSerializer.Serialize(standardSkills),
            ["skill_count"] = skills.Count.ToString(CultureInfo.InvariantCulture),
            ["custom_skill_count"] = (skills.Count - standardSkills.Count).ToString(CultureInfo.InvariantCulture)
        });
    }

    private static Dictionary<string, string?> GlobalProperties(
        UserConfigResponse config, AccountEntitlements entitlements) => new()
    {
        ["template"] = UserConfigScopes.NormalizeShareImageTemplate(config.ShareImageTemplate),
        ["timer_enabled"] = Flag(config.EnableTimer),
        ["match_minutes"] = config.MatchTimeMinutes.ToString(CultureInfo.InvariantCulture),
        ["extra_minutes"] = config.ExtraTimeMinutes.ToString(CultureInfo.InvariantCulture),
        ["chemistry_enabled"] = Flag(config.UseChemistry),
        ["invitations_enabled"] = Flag(config.EnableMatchInvitations),
        ["scoreboard_enabled"] = Flag(config.AllowOnlineScoreboard),
        ["language"] = config.Language?.ToLowerInvariant() switch
        {
            "en" or "en-us" => "en",
            "he" or "he-il" => "he",
            "ar" => "ar",
            _ => "other"
        },
        ["account_tier"] = entitlements.IsPremium ? "premium" : "free",
        ["premium_templates_eligible"] = Flag(entitlements.CanUsePremiumShareTemplates),
        ["chemistry_eligible"] = Flag(entitlements.CanUseChemistry),
        ["invitations_eligible"] = Flag(entitlements.CanUseMatchInvitations)
    };

    private static Dictionary<string, string?> GroupProperties(UserConfigResponse config) => new()
    {
        ["algorithm"] = config.SelectedAlgoKey.ToString(CultureInfo.InvariantCulture),
        ["algorithm_name"] = AlgorithmName(config.SelectedAlgoKey),
        ["team_count"] = config.NumberOfTeams.ToString(CultureInfo.InvariantCulture),
        ["player_limit"] = config.MaxMatchdayPlayers.ToString(CultureInfo.InvariantCulture),
        ["custom_player_limit"] = Flag(config.HasCustomMatchdayPlayerLimit),
        ["skill_count"] = SkillDefinition.Normalize(config.SkillDefinitions).Count.ToString(CultureInfo.InvariantCulture),
        ["custom_logo_present"] = Flag(!string.IsNullOrWhiteSpace(config.GroupLogoDataUrl)),
        ["matchday_rules_present"] = Flag(config.MatchdayRules != null)
    };

    private static void TrackChanges(
        IUsageTelemetry telemetry, string? version, string userId,
        Dictionary<string, string?> current, Dictionary<string, string?> previous)
    {
        foreach (var property in previous)
        {
            if (current[property.Key] == property.Value
                || property.Key.EndsWith("_eligible", StringComparison.Ordinal)
                || property.Key is "account_tier" or "algorithm_name")
                continue;
            var changes = new Dictionary<string, string?>
            {
                ["preference_scope"] = current["preference_scope"],
                ["setting"] = property.Key,
                ["previous_value"] = property.Value,
                ["value"] = current[property.Key],
                ["outcome"] = "succeeded"
            };
            if (current.TryGetValue("group_key", out var groupKey))
                changes["group_key"] = groupKey;
            telemetry.Track("PreferenceChanged", version, userId, changes);
        }
    }
}
