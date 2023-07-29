using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using TeamsGenerator.Algos;
using TeamsGenerator.Algos.BackAndForthAlgo;
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
            };

            AlgoTypeToPlayerSerializerMapper = new Dictionary<AlgoType, Func<dynamic, IPlayer[]>>()
            {
                { AlgoType.SkillWise, (json) => JsonConvert.DeserializeObject<SkillWisePlayer[]>(json) },
                { AlgoType.BackAndForth, (json) => JsonConvert.DeserializeObject<BackAndForthPlayer[]>(json) },
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

        public static GetTeamsResponse GetTeams(dynamic json, int algoKey)
        {
            var configSerializedObject = JsonConvert.SerializeObject(json.config);
            UserConfigResponse config = JsonConvert.DeserializeObject<UserConfigResponse>(configSerializedObject);


            var algoKeyEnum = (AlgoType)algoKey;
            var playersSerializedObject = JsonConvert.SerializeObject(json.players, Newtonsoft.Json.Formatting.Indented);
            IEnumerable<IPlayer> playersCollection = AlgoTypeToPlayerSerializerMapper[algoKeyEnum].Invoke(playersSerializedObject);


            var algoConfig = new AlgoConfig() { TeamsCount = config.NumberOfTeams };
            var teams = AlgoRunner.Run(algoKeyEnum, playersCollection.ToList(), algoConfig);
            var teamsResponse = GetDisplayTeams(config.ShirtsColors, teams, config.ShowWhoBegins);

            return new GetTeamsResponse() { Teams = teamsResponse };
        }

        public static GetAppSetupResponse GetAppSetup(string version)
        {
            var shirtsColors = ConfigurationManager.ShirtsColorNameToSymbolMapper;
            var numberOfTeams = ConfigurationManager.NumberOfTeams;

            var algos = _algoTypeToInformationMapper.Values.ToList();
            foreach (var algo in algos)
            {
                algo.PlayerProperties = algo.PlayerProperties.Where(p => CompareVersion(p.MinVersion, version)).ToList();
            }

            var config = new UserConfigResponse() { ShirtsColors = shirtsColors, NumberOfTeams = numberOfTeams };

            return new GetAppSetupResponse() { Algos = algos, Config = config };
        }

        public static IEnumerable<PlayerProperties> GetPlayersProperties(int algoType, string version)
        {
            if (!_algoTypeToInformationMapper.ContainsKey((AlgoType)algoType)) return new List<PlayerProperties>();
            var playerProperties = _algoTypeToInformationMapper[(AlgoType)algoType].PlayerProperties;

            var propertiesFilteredByVersion = playerProperties.Where(p => CompareVersion(p.MinVersion, version));
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

        private static List<WebAppTeam> GetDisplayTeams(List<PlayerShirt> shirtsColorNames, List<Algos.Team> teams, bool showWhoBegins)
        {
            var results = new List<WebAppTeam>();
            var selectedShirts = Helper.Shuffle(shirtsColorNames.Where(s=>s.IsMarked).ToList());

            var index = 1;
            foreach (var team in teams)
            {
                var shirtColor = selectedShirts[0];
                results.Add(new WebAppTeam() { Players = team.Players, Rank = team.TotalRank, Color = shirtColor.ColorName, TeamSymbol = shirtColor.Symbol, TeamName = index.ToString(), TeamId = index });
                index++;
                selectedShirts.RemoveAt(0);
            }

            if(showWhoBegins)
            {
                SetStartingTeamIds(results);
            }

            return results;
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
