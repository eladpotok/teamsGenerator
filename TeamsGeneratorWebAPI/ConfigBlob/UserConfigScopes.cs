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
        ApplyGlobal(merged, globalConfig ?? new UserConfigResponse());
        return merged;
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
        target.AvailableLanguages = source.AvailableLanguages;
    }

    private static UserConfigResponse Clone(UserConfigResponse config)
    {
        return config == null
            ? null
            : JsonConvert.DeserializeObject<UserConfigResponse>(
                JsonConvert.SerializeObject(config));
    }
}
