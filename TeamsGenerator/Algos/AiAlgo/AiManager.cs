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
            Exception lastError = null;
            for (var attempt = 0; attempt < MaxAttempts; attempt++)
            {
                try
                {
                    return Generate(players, generatedTeamWithLockedPlayers);
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
            IEnumerable<IPlayer> players,
            IEnumerable<Team> generatedTeamWithLockedPlayers)
        {
            var inputPlayers = players.Cast<AiPlayer>().ToList();
            if (inputPlayers.Count < _config.TeamsCount)
            {
                throw new InvalidOperationException(
                    "The number of players must be at least the number of teams.");
            }

            var playersForAssessment = inputPlayers.Select(player => new PlayerJsonToAi
            {
                Name = player.Name,
                Description = SplitDescription(player.Description),
                Key = player.Key,
                ModifiedTime = player.ModifyTime
            }).ToList();

            var assessedPlayers = AssessPlayers(playersForAssessment);
            var lockedTeams = CreateLockedTeamInput(generatedTeamWithLockedPlayers);
            ValidateLockedTeams(lockedTeams, assessedPlayers);
            var generatedTeams = GenerateBalancedTeams(assessedPlayers, lockedTeams);

            ValidateGeneratedTeams(generatedTeams, assessedPlayers, lockedTeams);
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
            IList<LockedTeamInput> lockedTeams)
        {
            var payload = new
            {
                players = assessedPlayers,
                lockedTeams
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
            IList<LockedTeamInput> lockedTeams)
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
            IEnumerable<SkillWisePlayer> players)
        {
            var validPlayerKeys = new HashSet<string>(
                players.Select(player => player.Key),
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
                            IsArrived = true
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
    }
}
