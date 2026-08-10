using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using TeamsGenerator.Ai;
using TeamsGenerator.Algos.SkillWiseAlgo;
using TeamsGenerator.Orchestration;
using TeamsGenerator.Orchestration.Contracts;

namespace TeamsGenerator.Algos.AiAlgo
{
    public class PlayerJsonToAi
    {
        public string Name { get; set; }
        public List<string> Description { get; set; }
        public string Key { get; set; }
        public string ModifiedTime { get; set; }
        public List<string> PreferredWithKeys { get; set; }
        public List<string> AvoidWithKeys { get; set; }
    }

    public class TeamsAiJsonOutput
    {
        public string Name { get; set; }
        public IEnumerable<string> Players { get; set; }
        public int Rating { get; set; }
        public string Strength { get; set; }
        public string Weakness { get; set; }
    }

    public class TeamsWrapper
    {
        public List<TeamsAiJsonOutput> Teams { get; set; }
    }

    public class AiManager : AlgoManagerBase, IAlgoManager
    {
        private const int MaxAttempts = 3;
        private readonly OpenAiService _aiService;

        public AiManager(AlgoConfig config) : base(config)
        {
            _aiService = new OpenAiService();
        }

        public List<Team> GenerateTeams(
            List<IPlayer> players,
            List<Team> generatedTeamWithLockedPlayers)
        {
            var inputPlayers = players.Cast<AiPlayer>().ToList();
            var lockedTeamList = generatedTeamWithLockedPlayers ?? new List<Team>();
            ValidateGenerationInput(inputPlayers, lockedTeamList);

            Exception lastError = null;
            for (var attempt = 0; attempt < MaxAttempts; attempt++)
            {
                try
                {
                    return Generate(inputPlayers, lockedTeamList);
                }
                catch (Exception exception)
                {
                    lastError = exception;
                }
            }

            throw new InvalidOperationException(
                "AI team generation failed after multiple attempts.",
                lastError);
        }

        private List<Team> Generate(
            IEnumerable<AiPlayer> players,
            IEnumerable<Team> generatedTeamWithLockedPlayers)
        {
            var inputPlayers = players.ToList();

            var playersForAssessment = inputPlayers.Select(player => new PlayerJsonToAi
            {
                Name = player.Name,
                Description = SplitDescription(player.Description),
                Key = player.Key,
                ModifiedTime = player.ModifyTime,
                PreferredWithKeys = player.PreferredWithKeys ?? new List<string>(),
                AvoidWithKeys = player.AvoidWithKeys ?? new List<string>()
            }).ToList();

            var assessedPlayers = AssessPlayers(playersForAssessment);
            var lockedTeams = CreateLockedTeamInput(generatedTeamWithLockedPlayers);
            ValidateLockedTeams(
                lockedTeams,
                assessedPlayers.Select(player => player.Key));
            var preferences = CreatePreferenceInput(inputPlayers);
            ValidatePreferences(
                preferences,
                assessedPlayers.Select(player => player.Key));
            var generatedTeams = GenerateBalancedTeams(
                assessedPlayers,
                lockedTeams,
                preferences);

            ValidateGeneratedTeams(
                generatedTeams,
                assessedPlayers,
                lockedTeams,
                preferences);
            return MapTeams(generatedTeams, playersForAssessment);
        }

        private List<SkillWisePlayer> AssessPlayers(
            IList<PlayerJsonToAi> inputPlayers)
        {
            var response = GetAiResponse(
                AiTeamPrompts.CreatePlayerAssessmentPrompt(),
                inputPlayers);
            var assessedPlayers =
                JsonConvert.DeserializeObject<List<SkillWisePlayer>>(response);

            ValidateAssessedPlayers(inputPlayers, assessedPlayers);

            var inputByKey = inputPlayers.ToDictionary(
                player => player.Key,
                StringComparer.OrdinalIgnoreCase);
            foreach (var player in assessedPlayers)
            {
                var input = inputByKey[player.Key];
                player.Name = input.Name;
                player.ModifyTime = input.ModifiedTime;
                player.Id = input.Key;
                player.IsArrived = true;
                player.IsLocked = false;
            }

            return assessedPlayers;
        }

        private List<AiTeam> GenerateBalancedTeams(
            IList<SkillWisePlayer> assessedPlayers,
            IList<LockedTeamInput> lockedTeams,
            IList<PlayerPreferenceInput> preferences)
        {
            var payload = new
            {
                players = assessedPlayers,
                lockedTeams,
                preferences
            };
            var response = GetAiResponse(
                AiTeamPrompts.CreateTeamGenerationPrompt(
                    _config.TeamsCount,
                    _config.Language),
                payload);

            return JsonConvert.DeserializeObject<List<AiTeam>>(response);
        }

        private string GetAiResponse(string prompt, object input)
        {
            return _aiService
                .GetResponseFromAgentForTeams(
                    prompt,
                    JsonConvert.SerializeObject(input))
                .GetAwaiter()
                .GetResult();
        }

        private void ValidateAssessedPlayers(
            IList<PlayerJsonToAi> inputPlayers,
            IList<SkillWisePlayer> assessedPlayers)
        {
            if (assessedPlayers == null || assessedPlayers.Count != inputPlayers.Count)
            {
                throw new InvalidOperationException(
                    "The AI player assessment did not return every player.");
            }

            var expectedKeys = new HashSet<string>(
                inputPlayers.Select(player => player.Key),
                StringComparer.OrdinalIgnoreCase);
            var returnedKeys = new HashSet<string>(
                assessedPlayers.Select(player => player.Key),
                StringComparer.OrdinalIgnoreCase);

            if (returnedKeys.Count != assessedPlayers.Count
                || !expectedKeys.SetEquals(returnedKeys))
            {
                throw new InvalidOperationException(
                    "The AI player assessment changed, omitted, or duplicated player keys.");
            }

            foreach (var player in assessedPlayers)
            {
                ValidateSkill(player.Attack, player.Key, "attack");
                ValidateSkill(player.Defence, player.Key, "defence");
                ValidateSkill(player.Stamina, player.Key, "stamina");
                ValidateSkill(player.Leadership, player.Key, "leadership");
                ValidateSkill(player.Passing, player.Key, "passing");
            }
        }

        private void ValidateGeneratedTeams(
            IList<AiTeam> teams,
            IList<SkillWisePlayer> players,
            IList<LockedTeamInput> lockedTeams,
            IList<PlayerPreferenceInput> preferences)
        {
            if (teams == null || teams.Count != _config.TeamsCount)
            {
                throw new InvalidOperationException(
                    "The AI did not return the requested number of teams.");
            }

            var expectedIndexes = new HashSet<int>(
                Enumerable.Range(0, _config.TeamsCount));
            if (!expectedIndexes.SetEquals(teams.Select(team => team.TeamIndex)))
            {
                throw new InvalidOperationException(
                    "The AI returned invalid or duplicate team indexes.");
            }

            var expectedKeys = new HashSet<string>(
                players.Select(player => player.Key),
                StringComparer.OrdinalIgnoreCase);
            var returnedPlayerKeys = teams
                .SelectMany(team => team.Players ?? Enumerable.Empty<string>())
                .ToList();
            var uniqueReturnedKeys = new HashSet<string>(
                returnedPlayerKeys,
                StringComparer.OrdinalIgnoreCase);

            if (returnedPlayerKeys.Count != players.Count
                || uniqueReturnedKeys.Count != returnedPlayerKeys.Count
                || !expectedKeys.SetEquals(uniqueReturnedKeys))
            {
                throw new InvalidOperationException(
                    "The AI omitted, added, or duplicated players between teams.");
            }

            var teamSizes = teams.Select(team => team.Players.Count()).ToList();
            if (teamSizes.Max() - teamSizes.Min() > 1)
            {
                throw new InvalidOperationException(
                    "The generated team sizes are not balanced.");
            }

            ValidateTeamAverages(teams, players);
            ValidateAvoidPreferences(teams, preferences);
            if (lockedTeams.Count == 0)
            {
                ValidateGoalkeeperDistribution(teams, players);
            }

            foreach (var lockedTeam in lockedTeams)
            {
                var generatedTeam = teams.Single(
                    team => team.TeamIndex == lockedTeam.TeamIndex);
                var generatedKeys = new HashSet<string>(
                    generatedTeam.Players,
                    StringComparer.OrdinalIgnoreCase);
                if (lockedTeam.PlayerKeys.Any(key => !generatedKeys.Contains(key)))
                {
                    throw new InvalidOperationException(
                        "The AI changed a locked player assignment.");
                }
            }
        }

        private static void ValidateTeamAverages(
            IEnumerable<AiTeam> teams,
            IEnumerable<SkillWisePlayer> players)
        {
            var playersByKey = players.ToDictionary(
                player => player.Key,
                StringComparer.OrdinalIgnoreCase);

            foreach (var team in teams)
            {
                var teamPlayers = team.Players
                    .Select(key => playersByKey[key])
                    .ToList();

                ValidateAverage(team.Attack, teamPlayers.Average(player => player.Attack));
                ValidateAverage(team.Defence, teamPlayers.Average(player => player.Defence));
                ValidateAverage(team.Stamina, teamPlayers.Average(player => player.Stamina));
                ValidateAverage(team.Leadership, teamPlayers.Average(player => player.Leadership));
                ValidateAverage(team.Passing, teamPlayers.Average(player => player.Passing));
            }
        }

        private static void ValidateGoalkeeperDistribution(
            IEnumerable<AiTeam> teams,
            IEnumerable<SkillWisePlayer> players)
        {
            var goalkeepers = new HashSet<string>(
                players
                    .Where(player => player.IsGoalKeeper)
                    .Select(player => player.Key),
                StringComparer.OrdinalIgnoreCase);
            var counts = teams
                .Select(team => team.Players.Count(goalkeepers.Contains))
                .ToList();

            if (counts.Max() - counts.Min() > 1)
            {
                throw new InvalidOperationException(
                    "The generated teams do not distribute goalkeepers evenly.");
            }
        }

        private static void ValidateAverage(float returnedValue, double actualValue)
        {
            if (Math.Abs(returnedValue - Math.Round(actualValue, 1)) > 0.01)
            {
                throw new InvalidOperationException(
                    "The AI returned an incorrect team attribute average.");
            }
        }

        private void ValidateLockedTeams(
            IEnumerable<LockedTeamInput> lockedTeams,
            IEnumerable<string> playerKeys)
        {
            var validPlayerKeys = new HashSet<string>(
                playerKeys,
                StringComparer.OrdinalIgnoreCase);
            var lockedPlayerKeys = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);

            foreach (var lockedTeam in lockedTeams)
            {
                if (lockedTeam.TeamIndex < 0
                    || lockedTeam.TeamIndex >= _config.TeamsCount)
                {
                    throw new InvalidOperationException(
                        "A locked team has an invalid team index.");
                }

                foreach (var playerKey in lockedTeam.PlayerKeys)
                {
                    if (!validPlayerKeys.Contains(playerKey))
                    {
                        throw new InvalidOperationException(
                            "A locked team contains a player who is not in the input.");
                    }

                    if (!lockedPlayerKeys.Add(playerKey))
                    {
                        throw new InvalidOperationException(
                            "A player is locked to more than one team.");
                    }
                }
            }
        }

        private List<Team> MapTeams(
            IEnumerable<AiTeam> generatedTeams,
            IEnumerable<PlayerJsonToAi> inputPlayers)
        {
            var playersByKey = inputPlayers.ToDictionary(
                player => player.Key,
                StringComparer.OrdinalIgnoreCase);

            return generatedTeams
                .OrderBy(team => team.TeamIndex)
                .Select(team => new Team
                {
                    Index = team.TeamIndex,
                    Description = team.Description,
                    PlayStyle = team.PlayStyle,
                    Strength = team.Strength,
                    Weakness = team.Weakness,
                    SkillAverages = new Dictionary<string, double>
                    {
                        ["attack"] = team.Attack,
                        ["defence"] = team.Defence,
                        ["stamina"] = team.Stamina,
                        ["leadership"] = team.Leadership,
                        ["passing"] = team.Passing
                    },
                    Players = team.Players.Select(key =>
                    {
                        var player = playersByKey[key];
                        return (IPlayer)new AiPlayer
                        {
                            Description = string.Join(", ", player.Description),
                            Key = player.Key,
                            Id = player.Key,
                            ModifyTime = player.ModifiedTime,
                            Name = player.Name,
                            IsArrived = true,
                            PreferredWithKeys = player.PreferredWithKeys,
                            AvoidWithKeys = player.AvoidWithKeys
                        };
                    }).ToList()
                })
                .ToList();
        }

        private static List<LockedTeamInput> CreateLockedTeamInput(
            IEnumerable<Team> lockedTeams)
        {
            return lockedTeams == null
                ? new List<LockedTeamInput>()
                : lockedTeams.Select(team => new LockedTeamInput
                {
                    TeamIndex = team.Index,
                    PlayerKeys = team.Players.Select(player => player.Key).ToList()
                }).ToList();
        }

        private static List<PlayerPreferenceInput> CreatePreferenceInput(
            IEnumerable<AiPlayer> players)
        {
            var playerList = players.ToList();
            var selectedKeys = new HashSet<string>(
                playerList.Select(player => player.Key),
                StringComparer.OrdinalIgnoreCase);

            return playerList.Select(player => new PlayerPreferenceInput
            {
                PlayerKey = player.Key,
                PreferredWithKeys = (player.PreferredWithKeys ?? new List<string>())
                    .Where(selectedKeys.Contains)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                AvoidWithKeys = (player.AvoidWithKeys ?? new List<string>())
                    .Where(selectedKeys.Contains)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList()
            }).ToList();
        }

        private static void ValidatePreferences(
            IEnumerable<PlayerPreferenceInput> preferences,
            IEnumerable<string> playerKeys)
        {
            var validKeys = new HashSet<string>(
                playerKeys,
                StringComparer.OrdinalIgnoreCase);

            foreach (var preference in preferences)
            {
                var relatedKeys = preference.PreferredWithKeys
                    .Concat(preference.AvoidWithKeys)
                    .ToList();

                if (relatedKeys.Any(key =>
                    !validKeys.Contains(key) ||
                    string.Equals(key, preference.PlayerKey, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidOperationException(
                        "A player preference references an invalid player.");
                }

                var preferred = new HashSet<string>(
                    preference.PreferredWithKeys,
                    StringComparer.OrdinalIgnoreCase);
                if (preference.AvoidWithKeys.Any(preferred.Contains))
                {
                    throw new InvalidOperationException(
                        "A player cannot both prefer and avoid the same player.");
                }
            }
        }

        private void ValidateGenerationInput(
            IList<AiPlayer> players,
            IList<Team> generatedTeamWithLockedPlayers)
        {
            if (players.Count < _config.TeamsCount)
            {
                throw new InvalidOperationException(
                    "The number of players must be at least the number of teams.");
            }

            var playerKeys = players.Select(player => player.Key).ToList();
            var preferences = CreatePreferenceInput(players);
            ValidatePreferences(preferences, playerKeys);

            var lockedTeams = CreateLockedTeamInput(generatedTeamWithLockedPlayers);
            ValidateLockedTeams(lockedTeams, playerKeys);

            var maximumTeamSize =
                (int)Math.Ceiling((double)players.Count / _config.TeamsCount);
            if (lockedTeams.Any(team => team.PlayerKeys.Count > maximumTeamSize))
            {
                throw new InvalidOperationException(
                    "A locked team contains more players than a balanced team can hold.");
            }

            if (_config.TeamsCount == 1
                && preferences.Any(preference => preference.AvoidWithKeys.Count > 0))
            {
                throw new InvalidOperationException(
                    "Avoid preferences require at least two teams.");
            }

            var preferenceByPlayer = preferences.ToDictionary(
                preference => preference.PlayerKey,
                StringComparer.OrdinalIgnoreCase);
            foreach (var lockedTeam in lockedTeams)
            {
                var lockedKeys = new HashSet<string>(
                    lockedTeam.PlayerKeys,
                    StringComparer.OrdinalIgnoreCase);
                if (lockedTeam.PlayerKeys.Any(playerKey =>
                    preferenceByPlayer[playerKey].AvoidWithKeys.Any(lockedKeys.Contains)))
                {
                    throw new InvalidOperationException(
                        "Players with an avoid preference cannot be locked to the same team.");
                }
            }
        }

        private static void ValidateAvoidPreferences(
            IEnumerable<AiTeam> teams,
            IEnumerable<PlayerPreferenceInput> preferences)
        {
            var teamByPlayer = teams
                .SelectMany(team => team.Players.Select(playerKey => new
                {
                    playerKey,
                    team.TeamIndex
                }))
                .ToDictionary(
                    item => item.playerKey,
                    item => item.TeamIndex,
                    StringComparer.OrdinalIgnoreCase);

            foreach (var preference in preferences)
            {
                if (preference.AvoidWithKeys.Any(avoidedKey =>
                    teamByPlayer[avoidedKey] == teamByPlayer[preference.PlayerKey]))
                {
                    throw new InvalidOperationException(
                        "The AI placed players together despite an avoid preference.");
                }
            }
        }

        private static List<string> SplitDescription(string description)
        {
            return string.IsNullOrWhiteSpace(description)
                ? new List<string>()
                : description
                    .Split(',')
                    .Select(value => value.Trim())
                    .Where(value => value.Length > 0)
                    .ToList();
        }

        private static void ValidateSkill(float value, string playerKey, string skill)
        {
            if (value < 1 || value > 10)
            {
                throw new InvalidOperationException(
                    $"The AI returned an invalid {skill} value for player '{playerKey}'.");
            }
        }

        private sealed class LockedTeamInput
        {
            [JsonProperty("teamIndex")]
            public int TeamIndex { get; set; }

            [JsonProperty("playerKeys")]
            public List<string> PlayerKeys { get; set; }
        }

        private sealed class PlayerPreferenceInput
        {
            [JsonProperty("playerKey")]
            public string PlayerKey { get; set; }

            [JsonProperty("preferredWithKeys")]
            public List<string> PreferredWithKeys { get; set; }

            [JsonProperty("avoidWithKeys")]
            public List<string> AvoidWithKeys { get; set; }
        }
    }
}
