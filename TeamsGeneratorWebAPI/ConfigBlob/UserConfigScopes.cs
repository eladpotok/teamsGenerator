using Newtonsoft.Json;
using TeamsGenerator.API;

namespace TeamsGeneratorWebAPI.ConfigBlob;

internal static class UserConfigScopes
{
    internal static UserConfigResponse Merge(
        UserConfigResponse groupConfig,
        UserConfigResponse globalConfig)
    {
        var merged = Clone(groupConfig ?? globalConfig)
            ?? new UserConfigResponse();
        if (groupConfig == null)
        {
            merged.MatchdayRules = null;
            merged.GroupLogoDataUrl = null;
        }
        ApplyGlobal(merged, globalConfig ?? new UserConfigResponse());
        return merged;
    }

    internal static UserConfigResponse InheritGroupSettings(
        UserConfigResponse ownerDefault)
    {
        var inherited = Clone(ownerDefault) ?? new UserConfigResponse();
        // Keep legacy settings inheritance, but never inherit another group's rules.
        inherited.MatchdayRules = null;
        inherited.GroupLogoDataUrl = null;
        return inherited;
    }

    internal static UserConfigResponse WithGroupSettings(
        UserConfigResponse existing,
        UserConfigResponse incoming)
    {
        var result = Clone(existing) ?? new UserConfigResponse();
        result.TeamName = incoming.TeamName;
        result.Location = incoming.Location;
        result.NumberOfTeams = incoming.NumberOfTeams;
        result.MaxMatchdayPlayers = incoming.MaxMatchdayPlayers;
        result.HasCustomMatchdayPlayerLimit =
            incoming.HasCustomMatchdayPlayerLimit;
        result.ShirtsColors = incoming.ShirtsColors;
        result.EventDate = incoming.EventDate;
        result.EventTime = incoming.EventTime;
        result.ScheduleType = incoming.ScheduleType;
        result.RepeatDay = incoming.RepeatDay;
        result.RepeatTime = incoming.RepeatTime;
        result.SelectedAlgoKey = incoming.SelectedAlgoKey;
        result.SkillDefinitions = incoming.SkillDefinitions;
        result.ArchivedSkillDefinitions =
            incoming.ArchivedSkillDefinitions;
        if (incoming.MatchdayRules != null)
        {
            result.MatchdayRules = incoming.MatchdayRules.Clone();
        }
        result.GroupLogoDataUrl = incoming.GroupLogoDataUrl;
        return result;
    }

    internal static UserConfigResponse WithGlobalSettings(
        UserConfigResponse existing,
        UserConfigResponse incoming)
    {
        var result = Clone(existing) ?? new UserConfigResponse();
        ApplyGlobal(result, incoming);
        return result;
    }

    private static void ApplyGlobal(
        UserConfigResponse target,
        UserConfigResponse source)
    {
        target.ShowWhoBegins = source.ShowWhoBegins;
        target.ShowFirstGoalKeeper = source.ShowFirstGoalKeeper;
        target.AllowOnlineScoreboard = source.AllowOnlineScoreboard;
        target.MatchTimeMinutes = source.MatchTimeMinutes;
        target.ExtraTimeMinutes = source.ExtraTimeMinutes;
        target.EnableTimer = source.EnableTimer;
        target.Language = source.Language;
        target.UseChemistry = source.UseChemistry;
        target.ShareImageTemplate = NormalizeShareImageTemplate(
            source.ShareImageTemplate);
        target.AvailableLanguages = source.AvailableLanguages;
    }

    internal static string NormalizeShareImageTemplate(string value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "stadium" => "stadium",
            "midnight" => "midnight",
            "sunset" => "sunset",
            "neon" => "neon",
            "minimal" => "minimal",
            _ => "classic"
        };
    }

    internal static bool IsPremiumShareImageTemplate(string value)
    {
        var normalized = NormalizeShareImageTemplate(value);
        return normalized != "classic" && normalized != "stadium";
    }

    private static UserConfigResponse Clone(UserConfigResponse config)
    {
        return config == null
            ? null
            : JsonConvert.DeserializeObject<UserConfigResponse>(
                JsonConvert.SerializeObject(config));
    }
}
