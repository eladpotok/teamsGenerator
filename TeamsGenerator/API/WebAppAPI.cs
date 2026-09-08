using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using TeamsGenerator.Algos;
using TeamsGenerator.Algos.AiAlgo;
using TeamsGenerator.Algos.BackAndForthAlgo;
using TeamsGenerator.Algos.PositionsAlgo;
using TeamsGenerator.Algos.SkillWiseAlgo;
using TeamsGenerator.Orchestration;
using TeamsGenerator.Orchestration.Configuration;
using TeamsGenerator.Orchestration.Contracts;
using TeamsGenerator.Utilities;

namespace TeamsGenerator.API
{

    public static class WebAppAPI
    {
        private static Dictionary<AlgoType, WebAppAlgoInfo> _algoTypeToInformationMapper;
        public static Dictionary<AlgoType, Func<dynamic, IPlayer[]>> AlgoTypeToPlayerSerializerMapper;

        public static void Init()
        {
            _algoTypeToInformationMapper = new Dictionary<AlgoType, WebAppAlgoInfo>() {
                { AlgoType.SkillWise, new WebAppAlgoInfo(AlgoType.SkillWise, "Skillwise", "Divide the players according to their skills") },
                { AlgoType.BackAndForth, new WebAppAlgoInfo(AlgoType.BackAndForth, "Back And Forth", "Do it in cycle") },
                { AlgoType.Positions, new WebAppAlgoInfo(AlgoType.Positions, "Positions", "Divide by the positions on the field") },
                { AlgoType.Ai, new WebAppAlgoInfo(AlgoType.Ai, "AI", "Using AI algorithm") },
            };

            AlgoTypeToPlayerSerializerMapper = new Dictionary<AlgoType, Func<dynamic, IPlayer[]>>()
            {
                { AlgoType.SkillWise, (json) => JsonConvert.DeserializeObject<SkillWisePlayer[]>(json) },
                { AlgoType.BackAndForth, (json) => JsonConvert.DeserializeObject<BackAndForthPlayer[]>(json) },
                { AlgoType.Positions, (json) => JsonConvert.DeserializeObject<PositionsPlayer[]>(json) },
                { AlgoType.Ai, (json) => JsonConvert.DeserializeObject<AiPlayer[]>(json) },
            };

            foreach (var item in _algoTypeToInformationMapper)
            {
                item.Value.Init();
            }

            ConfigurationManager.Init();
        }

        public static string GetResultString(dynamic json)
        {
            var teamsSerializedObject = JsonConvert.SerializeObject(json.teams, Newtonsoft.Json.Formatting.Indented);
            IEnumerable<WebAppTeam> teams = JsonConvert.DeserializeObject<WebAppTeam[]>(teamsSerializedObject);

            var textAsResult = Helper.GetResultsAsText(teams.Cast<IDisplayTeam>().ToList(), false);
            return textAsResult;
        }

        public static GetTeamsResponse GetTeams(
            dynamic json,
            int algoKey,
            IReadOnlyDictionary<string, double> chemistryScores = null,
            int chemistryMatchdayCount = 0)
        {
            var configSerializedObject = JsonConvert.SerializeObject(json.config);
            UserConfigResponse config = JsonConvert.DeserializeObject<UserConfigResponse>(configSerializedObject);
            config.SkillDefinitions = SkillDefinition.Normalize(
                config.SkillDefinitions);


            var algoKeyEnum = (AlgoType)algoKey;
            var playersSerializedObject = JsonConvert.SerializeObject(json.players, Newtonsoft.Json.Formatting.Indented);
            IEnumerable<IPlayer> playersCollection = AlgoTypeToPlayerSerializerMapper[algoKeyEnum].Invoke(playersSerializedObject);
            SetActiveSkills(
                playersCollection,
                config.SkillDefinitions);

            List<Team> alreadyGeneratedTeams = null;
            if(json.teams != null)
            {
                var indexTeam = 0;
                alreadyGeneratedTeams = new List<Team>();
                foreach (var team in json.teams)
                {
                    var playersInTeamJson = JsonConvert.SerializeObject(team.players, Newtonsoft.Json.Formatting.Indented);
                    IEnumerable<IPlayer> playersInTeam = AlgoTypeToPlayerSerializerMapper[algoKeyEnum].Invoke(playersInTeamJson);
                    SetActiveSkills(
                        playersInTeam,
                        config.SkillDefinitions);
                    var createdTeam = new Team(indexTeam++);
                    playersInTeam.ToList().ForEach(p => createdTeam.AddPlayer(p));
                    alreadyGeneratedTeams.Add(createdTeam);
                }
            }

            var algoConfig = new AlgoConfig()
            {
                TeamsCount = config.NumberOfTeams,
                Language = config.Language,
                UseChemistry = config.UseChemistry,
                ChemistryScores = chemistryScores,
                SkillDefinitions = config.SkillDefinitions
            };
            var teams = AlgoRunner.Run(
                algoKeyEnum,
                playersCollection.ToList(),
                algoConfig,
                alreadyGeneratedTeams,
                out var chemistryResult);
            var teamsResponse = GetDisplayTeams(
                config.ShirtsColors,
                teams,
                config.ShowWhoBegins,
                config.SkillDefinitions);

            var chemistrySupported =
                algoKeyEnum == AlgoType.SkillWise
                || algoKeyEnum == AlgoType.Positions;
            return new GetTeamsResponse()
            {
                Teams = teamsResponse,
                Chemistry = new ChemistryGenerationDiagnostics
                {
                    Requested = config.UseChemistry && chemistrySupported,
                    Applied = chemistryResult.WasEvaluated,
                    HistoryMatchdayCount = chemistryMatchdayCount,
                    PartnershipCount =
                        chemistryResult.PartnershipCount,
                    SwapCount = chemistryResult.SwapCount,
                    BalanceImprovementPercent =
                        chemistryResult.BalanceImprovementPercent,
                    NotablePartnerships =
                        chemistryResult.NotablePartnerships,
                    TeamRatings = chemistryResult.TeamRatings
                }
            };
        }

        public static GetAppSetupResponse GetAppSetup(
            string version,
            UserConfigResponse userConfig = null)
        {
            var shirtsColors = ConfigurationManager.ShirtsColorNameToSymbolMapper;
            var numberOfTeams = ConfigurationManager.NumberOfTeams;

            var skillDefinitions = SkillDefinition.Normalize(
                userConfig?.SkillDefinitions);
            var algos = _algoTypeToInformationMapper.Values
                .Select(algo => algo.ForSkills(skillDefinitions))
                .ToList();
            //foreach (var algo in algos)
            //{
            //    algo.PlayerProperties = algo.PlayerProperties.Where(p => CompareVersion(p.MinVersion, version)).ToList();
            //}

            var config = userConfig ?? new UserConfigResponse();
            if (config.ShirtsColors == null || config.ShirtsColors.Count == 0)
            {
                config.ShirtsColors = shirtsColors
                    .Select(shirt => new PlayerShirt
                    {
                        ColorName = shirt.ColorName,
                        Symbol = shirt.Symbol,
                        IsMarked = shirt.IsMarked
                    })
                    .ToList();
            }
            config.NumberOfTeams = config.NumberOfTeams <= 0
                ? numberOfTeams
                : config.NumberOfTeams;
            config.MaxMatchdayPlayers =
                config.HasCustomMatchdayPlayerLimit
                    ? Math.Clamp(config.MaxMatchdayPlayers, 5, 50)
                    : Math.Clamp(config.NumberOfTeams * 5, 5, 50);
            config.SkillDefinitions = skillDefinitions;

            return new GetAppSetupResponse() { Algos = algos, Config = config };
        }

        public static IEnumerable<PlayerProperties> GetPlayersProperties(int algoType, string version)
        {
            if (!_algoTypeToInformationMapper.ContainsKey((AlgoType)algoType)) return new List<PlayerProperties>();
            var playerProperties = _algoTypeToInformationMapper[(AlgoType)algoType].PlayerProperties;

            var propertiesFilteredByVersion = playerProperties;
            return propertiesFilteredByVersion;
        }

        private static bool CompareVersion(string minVersion, string requriedVersion)
        {
            if (minVersion == null) return true;

            var minVersionParts = minVersion.Split('.');
            var requiredVersionParts = requriedVersion.Split('.');

            for (int i = 0; i < 3; i++)
            {
                if (int.Parse(minVersionParts[i]) > int.Parse(requiredVersionParts[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static List<WebAppTeam> GetDisplayTeams(
            List<PlayerShirt> shirtsColorNames,
            List<Algos.Team> teams,
            bool showWhoBegins,
            IReadOnlyList<SkillDefinition> skillDefinitions)
        {
            var results = new List<WebAppTeam>();
            var selectedShirts = Helper.Shuffle(shirtsColorNames.Where(s=>s.IsMarked).ToList());

            var index = 1;
            foreach (var team in teams)
            {
                var shirtColor = selectedShirts[0];
                results.Add(new WebAppTeam() 
                { 
                    Players = Helper.Shuffle(team.Players), 
                    Rank = team.TotalRank, 
                    Color = shirtColor.ColorName, 
                    TeamSymbol = shirtColor.Symbol, 
                    TeamName = index.ToString(), 
                    TeamId = index,
                    Description = team.Description,
                    PlayStyle = team.PlayStyle,
                    Strength = team.Strength,
                    Weakness = team.Weakness,
                    SkillAverages = GetSkillAverages(
                        team,
                        skillDefinitions)
                });
                index++;
                selectedShirts.RemoveAt(0);
            }

            if(showWhoBegins)
            {
                SetStartingTeamIds(results);
            }

            return results;
        }

        private static void SetActiveSkills(
            IEnumerable<IPlayer> players,
            IEnumerable<SkillDefinition> skillDefinitions)
        {
            foreach (var player in players
                .OfType<IConfigurableSkillsPlayer>())
            {
                player.SetActiveSkills(skillDefinitions);
            }
        }

        private static Dictionary<string, double> GetSkillAverages(
            Algos.Team team,
            IReadOnlyList<SkillDefinition> skillDefinitions)
        {
            if (team.Players.Count == 0
                || !team.Players.All(player =>
                    player is IConfigurableSkillsPlayer))
            {
                return team.SkillAverages;
            }

            return skillDefinitions.ToDictionary(
                skill => skill.Id,
                skill => team.Players
                    .Cast<IConfigurableSkillsPlayer>()
                    .Average(player =>
                        player.GetSkillValue(skill.Id)));
        }


        private static void SetStartingTeamIds(List<WebAppTeam> teams)
        {
            var random = new Random();

            var teamIds = teams.Select(t => t.TeamId).ToList();
            var teamIndexToTake = random.Next(0, teamIds.Count);
            var team1 = teamIds[teamIndexToTake];
            teamIds.RemoveAt(teamIndexToTake);
            teamIndexToTake = random.Next(0, teamIds.Count);
            var team2 = teamIds[teamIndexToTake];

            foreach (var team in teams)
            {
                if(team.TeamId == team1)
                {
                    team.IsStarting = true;
                }
                if (team.TeamId == team2)
                {
                    team.IsStarting = true;
                }
            }
        }
    }
}
