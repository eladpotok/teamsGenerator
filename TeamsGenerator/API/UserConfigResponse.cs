using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using TeamsGenerator.Orchestration;

namespace TeamsGenerator.API
{
    public class SkillDefinition
    {
        public const int MinimumCount = 3;
        public const int MaximumCount = 8;
        public const float DefaultValue = 5;

        public string Id { get; set; }
        public string Name { get; set; }

        public static List<SkillDefinition> CreateDefaults()
        {
            return new List<SkillDefinition>
            {
                new SkillDefinition { Id = "leadership", Name = "Leadership" },
                new SkillDefinition { Id = "attack", Name = "Attack" },
                new SkillDefinition { Id = "defence", Name = "Defence" },
                new SkillDefinition { Id = "stamina", Name = "Stamina" },
                new SkillDefinition { Id = "passing", Name = "Passing" }
            };
        }

        public static List<SkillDefinition> Normalize(
            IEnumerable<SkillDefinition> definitions)
        {
            var normalized = definitions?
                .Where(definition =>
                    definition != null
                    && IsValidId(definition.Id)
                    && IsValidName(definition.Name))
                .Select(definition => new SkillDefinition
                {
                    Id = definition.Id.Trim().ToLowerInvariant(),
                    Name = definition.Name.Trim()
                })
                .GroupBy(definition => definition.Id)
                .Select(group => group.First())
                .ToList();

            return normalized is { Count: >= MinimumCount and <= MaximumCount }
                && normalized.Select(definition => definition.Name)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count() == normalized.Count
                ? normalized
                : CreateDefaults();
        }

        public static List<SkillDefinition> NormalizeArchived(
            IEnumerable<SkillDefinition> definitions,
            IEnumerable<SkillDefinition> activeDefinitions)
        {
            var activeIds = new HashSet<string>(
                Normalize(activeDefinitions).Select(skill => skill.Id),
                StringComparer.OrdinalIgnoreCase);
            var seenIds = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

            return definitions?
                .Where(definition =>
                    definition != null
                    && IsValidId(definition.Id)
                    && IsValidName(definition.Name))
                .Select(definition => new SkillDefinition
                {
                    Id = definition.Id.Trim().ToLowerInvariant(),
                    Name = definition.Name.Trim()
                })
                .Where(definition =>
                    !activeIds.Contains(definition.Id)
                    && seenIds.Add(definition.Id))
                .ToList()
                ?? new List<SkillDefinition>();
        }

        public static bool IsValidId(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return false;
            }

            var normalized = id.Trim();
            var reservedIds = new HashSet<string>(
                new[]
                {
                    "name",
                    "key",
                    "id",
                    "rank",
                    "modifytime",
                    "isarrived",
                    "waitinglistorder",
                    "islocked",
                    "isgoalkeeper",
                    "positions",
                    "description",
                    "preferredwithkeys",
                    "avoidwithkeys"
                },
                StringComparer.OrdinalIgnoreCase);
            return normalized.Length <= 64
                && !reservedIds.Contains(normalized)
                && normalized.All(character =>
                    char.IsLetterOrDigit(character)
                    || character == '_');
        }

        public static bool IsValidName(string name)
        {
            return !string.IsNullOrWhiteSpace(name)
                && name.Trim().Length <= 24;
        }
    }

    public class Lang
    {
        public string Value { get; set; }
        public string Label { get; set; }
    }

    public class UserConfigResponse
    {
        public int NumberOfTeams { get; set; }
        public List<PlayerShirt> ShirtsColors { get; set; }
        public bool ShowWhoBegins { get; set; }
        public bool ShowFirstGoalKeeper { get; set; }
        public DateTime EventDate { get; set; }
        public DateTime EventTime { get; set; }
        public int SelectedAlgoKey { get; set; }
        public string TeamName { get; set; }
        public string Location { get; set; }
        public bool AllowOnlineScoreboard { get; set; }
        public string CurrentVersion { get; set; }
        public int MatchTimeMinutes { get; set; }
        public int ExtraTimeMinutes { get; set; }
        public bool EnableTimer { get; set; }
        public string ScheduleType { get; set; }
        public string RepeatDay { get; set; }
        public string RepeatTime { get; set; }
        public string Language { get; set; }
        public bool UseChemistry { get; set; }
        public int MaxMatchdayPlayers { get; set; }
        public bool HasCustomMatchdayPlayerLimit { get; set; }
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<SkillDefinition> SkillDefinitions { get; set; }
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<SkillDefinition> ArchivedSkillDefinitions { get; set; }
        public List<Lang> AvailableLanguages { get; set; }


        public UserConfigResponse()
        {
            ShowWhoBegins = true;
            ShowFirstGoalKeeper = true;
            ShirtsColors = new List<PlayerShirt>();
            EventDate = DateTime.UtcNow;
            EventTime = DateTime.UtcNow;
            SelectedAlgoKey = 0;
            NumberOfTeams = 3;
            TeamName = "";
            Location = "";
            MatchTimeMinutes = 8;
            ExtraTimeMinutes = 2;
            EnableTimer = false;
            ScheduleType = "repeating";
            RepeatDay = "sunday";
            RepeatTime = "12:00";
            AvailableLanguages = new List<Lang>()
            {
                new Lang() { Value ="en", Label="English" },
                new Lang() { Value ="he", Label="עברית (Hebrew)" }
            };
            Language = AvailableLanguages[0].Value;
            UseChemistry = false;
            MaxMatchdayPlayers = 15;
            HasCustomMatchdayPlayerLimit = false;
            SkillDefinitions = SkillDefinition.CreateDefaults();
            ArchivedSkillDefinitions = new List<SkillDefinition>();
        }
    }
}
