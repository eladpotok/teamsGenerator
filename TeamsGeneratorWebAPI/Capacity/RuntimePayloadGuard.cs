using System.Text.Json;

namespace TeamsGeneratorWebAPI.Capacity;

public static class RuntimePayloadGuard
{
    public const int MaximumJsonDepth = 32;
    // Matchday custom rules already support 1200 characters; do not truncate that contract.
    public const int MaximumPresentationTextLength = 1200;
    private static readonly HashSet<string> PresentationActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "UserPlayers.PostSharePlayers", "Teams.GetTeamsDesign", "Teams.GetAllTeamsDesign",
        "Teams.GetScoresDesign", "Teams.GetNormalizedScoresDesign", "Teams.PostResultString"
    };
    private static readonly HashSet<string> DisplayFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "name", "playerName", "teamName", "matchName", "title", "location", "venue",
        "date", "eventDate", "dayInWeek", "currentCulture", "culture",
        "color", "teamColor", "teamSymbol", "score"
    };

    public static void Validate(JsonElement root, CapacityOptions options, bool heavy, string? operation = null)
    {
        var generation = operation == null ? heavy
            : operation.Equals("Teams.Post", StringComparison.OrdinalIgnoreCase);
        var presentation = operation != null && PresentationActions.Contains(operation);
        var playerLimit = generation ? options.PremiumGenerationPlayers : options.MaximumRenderingEntries;
        Walk(root, options, heavy || presentation, generation, presentation, playerLimit);
        if (!generation && !presentation) return;
        if (TryGetProperty(root, "teams", out var teams)
            && teams.ValueKind == JsonValueKind.Array)
        {
            var total = teams.EnumerateArray().Sum(team =>
                TryGetProperty(team, "players", out var players)
                    && players.ValueKind == JsonValueKind.Array ? players.GetArrayLength() : 0);
            RejectAbove(total, playerLimit, generation ? "generation_players_limit" : "render_players_limit");
        }
    }

    public static void ValidateGeneration(JsonElement root, CapacityOptions options, int playerLimit)
    {
        Validate(root, options, true, "Teams.Post");
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("players", out var players) || players.ValueKind != JsonValueKind.Array
            || !root.TryGetProperty("config", out var config) || config.ValueKind != JsonValueKind.Object)
            throw Invalid("A players array and config object are required.");
        RejectAbove(players.GetArrayLength(), playerLimit, "generation_players_limit");
        var teamCount = 3;
        foreach (var field in config.EnumerateObject())
            if (field.Name.Equals("numberOfTeams", StringComparison.OrdinalIgnoreCase))
            {
                if (field.Value.ValueKind != JsonValueKind.Number
                    || !field.Value.TryGetInt32(out teamCount) || teamCount < 1)
                    throw Invalid("The number of teams must be a positive integer.");
            }
        RejectAbove(teamCount, options.MaximumGenerationTeams, "generation_teams_limit");
        if (root.TryGetProperty("teams", out var teams) && teams.ValueKind != JsonValueKind.Null)
        {
            if (teams.ValueKind != JsonValueKind.Array) throw Invalid("Teams must be an array.");
            RejectAbove(teams.GetArrayLength(), teamCount, "generation_teams_limit");
            var total = 0;
            foreach (var team in teams.EnumerateArray())
            {
                if (team.ValueKind != JsonValueKind.Object || !team.TryGetProperty("players", out var locked)
                    || locked.ValueKind != JsonValueKind.Array)
                    throw Invalid("Every team must contain a players array.");
                total += locked.GetArrayLength();
            }
            RejectAbove(total, playerLimit, "generation_players_limit");
            // Legacy clients may send unlocked players separately; count that union without double counting keys.
            var rosterKeys = players.EnumerateArray().Select(PlayerKey).ToHashSet(StringComparer.Ordinal);
            var lockedKeys = new HashSet<string>(StringComparer.Ordinal);
            var combinedCount = players.GetArrayLength();
            foreach (var team in teams.EnumerateArray())
                foreach (var player in team.GetProperty("players").EnumerateArray())
                {
                    var key = PlayerKey(player);
                    if (!string.IsNullOrEmpty(key) && !lockedKeys.Add(key))
                        throw Invalid("A locked player cannot occur in multiple teams.");
                    if (string.IsNullOrEmpty(key) || !rosterKeys.Contains(key)) combinedCount++;
                }
            RejectAbove(combinedCount, playerLimit, "generation_players_limit");
        }
    }

    private static string PlayerKey(JsonElement player) =>
        player.ValueKind == JsonValueKind.Object
        && player.TryGetProperty("key", out var key) && key.ValueKind == JsonValueKind.String
            ? key.GetString()! : "";

    private static bool TryGetProperty(JsonElement value, string name, out JsonElement result)
    {
        if (value.ValueKind == JsonValueKind.Object)
            foreach (var property in value.EnumerateObject())
                if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    result = property.Value;
                    return true;
                }
        result = default;
        return false;
    }

    private static void Walk(JsonElement value, CapacityOptions options, bool heavy,
        bool generation, bool presentation, int playerLimit)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in value.EnumerateObject())
            {
                if (presentation && names.Count >= 128)
                    throw Invalid("A display object contains too many fields.");
                if (!names.Add(property.Name))
                    throw Invalid("Duplicate JSON properties are not supported.");
                // Scoreboard dictionary keys are rendered too, not just JSON string values.
                if (presentation)
                    RejectAbove(property.Name.Length, options.MaximumPlayerKeyLength, "field_size_limit");
                if (presentation && DisplayFields.Contains(property.Name)
                    && (property.Value.ValueKind == JsonValueKind.Object || property.Value.ValueKind == JsonValueKind.Array))
                    throw Invalid("Display fields must be scalar values.");
                if (heavy && property.Value.ValueKind == JsonValueKind.String)
                {
                    var limit = property.Name.Equals("key", StringComparison.OrdinalIgnoreCase)
                        ? options.MaximumPlayerKeyLength
                        : property.Name.Equals("name", StringComparison.OrdinalIgnoreCase)
                            || property.Name.Equals("playerName", StringComparison.OrdinalIgnoreCase)
                            ? options.MaximumPlayerNameLength
                            : presentation && !property.Name.Equals("groupLogo", StringComparison.OrdinalIgnoreCase)
                                ? MaximumPresentationTextLength : options.MaximumRequestBytes;
                    RejectAbove(property.Value.GetString()!.Length, limit, "field_size_limit");
                }
                if ((generation || presentation) && property.Name.Equals("teams", StringComparison.OrdinalIgnoreCase)
                    && property.Value.ValueKind == JsonValueKind.Array)
                    RejectAbove(property.Value.GetArrayLength(), options.MaximumGenerationTeams, "generation_teams_limit");
                if ((generation || presentation)
                    && (property.Name.Equals("players", StringComparison.OrdinalIgnoreCase)
                        || property.Name.Equals("playerNames", StringComparison.OrdinalIgnoreCase)))
                {
                    if (property.Value.ValueKind != JsonValueKind.Array && property.Value.ValueKind != JsonValueKind.Null)
                        throw Invalid("Players must be an array.");
                    if (property.Value.ValueKind == JsonValueKind.Array)
                    {
                        RejectAbove(property.Value.GetArrayLength(), playerLimit,
                            generation ? "generation_players_limit" : "render_players_limit");
                       foreach (var player in property.Value.EnumerateArray())
                           if (player.ValueKind == JsonValueKind.String)
                               RejectAbove(player.GetString()!.Length, options.MaximumPlayerNameLength, "field_size_limit");
                    }
                }
                if (presentation && property.Value.ValueKind == JsonValueKind.Array
                    && (property.Name.Equals("topScorers", StringComparison.OrdinalIgnoreCase)
                        || property.Name.Equals("topAssists", StringComparison.OrdinalIgnoreCase)))
                    RejectAbove(property.Value.GetArrayLength(), playerLimit, "render_players_limit");
                if (generation && property.Name.Equals("numberOfTeams", StringComparison.OrdinalIgnoreCase))
                {
                    if (property.Value.ValueKind != JsonValueKind.Number
                        || !property.Value.TryGetInt32(out var count) || count < 1)
                        throw Invalid("The number of teams must be a positive integer.");
                    RejectAbove(count, options.MaximumGenerationTeams, "generation_teams_limit");
                }
                Walk(property.Value, options, heavy, generation, presentation, playerLimit);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            // History matches/events are bounded by the request bytes and depth, not generation quotas.
            if (presentation)
                RejectAbove(value.GetArrayLength(), playerLimit, "render_players_limit");
            foreach (var item in value.EnumerateArray())
            {
                if (presentation && item.ValueKind == JsonValueKind.String)
                    RejectAbove(item.GetString()!.Length, MaximumPresentationTextLength, "field_size_limit");
                Walk(item, options, heavy, generation, presentation, playerLimit);
            }
        }
    }

    private static CapacityException Invalid(string message) => new("invalid_payload", message, 400);

    public static void RejectAbove(int current, int limit, string code)
    {
        if (current > limit)
        {
            var message = code switch
            {
                "generation_players_limit" => $"Select at most {limit} players when generating teams.",
                "generation_teams_limit" => $"This operation supports at most {limit} teams.",
                "render_players_limit" => $"This image or text result supports at most {limit} entries.",
                "field_size_limit" => $"Shorten the text to at most {limit} characters.",
                _ => $"This operation supports at most {limit} items."
            };
            throw new CapacityException(code, message, 413, limit, current);
        }
    }
}
