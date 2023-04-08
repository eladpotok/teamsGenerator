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
        private static Dictionary<AlgoType, Func<dynamic, IPlayer[]>> _algoTypeToPlayerSerializerMapper;

        public static void Init()
        {
            _algoTypeToInformationMapper = new Dictionary<AlgoType, WebAppAlgoInfo>() {
                { AlgoType.BackAndForth, new WebAppAlgoInfo(AlgoType.BackAndForth, "Back And Forth", "Do it in cycle") },
                { AlgoType.SkillWise, new WebAppAlgoInfo(AlgoType.SkillWise, "Skillwise", "Divide the players according to their skills") },
            };

            _algoTypeToPlayerSerializerMapper = new Dictionary<AlgoType, Func<dynamic, IPlayer[]>>()
            {
                { AlgoType.BackAndForth, (json) => JsonConvert.DeserializeObject<BackAndForthPlayer[]>(json) },
                { AlgoType.SkillWise, (json) => JsonConvert.DeserializeObject<SkillWisePlayer[]>(json) },
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
            WebAppConfigResponse config = JsonConvert.DeserializeObject<WebAppConfigResponse>(configSerializedObject);


            var algoKeyEnum = (AlgoType)algoKey;
            var playersSerializedObject = JsonConvert.SerializeObject(json.players, Newtonsoft.Json.Formatting.Indented);
            IEnumerable<IPlayer> playersCollection = _algoTypeToPlayerSerializerMapper[algoKeyEnum].Invoke(playersSerializedObject);

            var algoConfig = new AlgoConfig() { TeamsCount = config.NumberOfTeams };
            var teams = AlgoRunner.Run(algoKeyEnum, playersCollection.ToList(), algoConfig);
            var teamsResponse = GetDisplayTeams(config.ShirtsColors, teams);

            var textAsResult = Helper.GetResultsAsText(teamsResponse.Cast<IDisplayTeam>().ToList(), false);
            return new GetTeamsResponse() { Teams = teamsResponse, TeamsResultAsCopyText = textAsResult };
        }

        public static InitialAppConfig GetInitialAlgoConfig()
        {
            var shirtsColors = ConfigurationManager.ShirtsColorNameToSymbolMapper.Keys.ToList();
            var numberOfTeams = ConfigurationManager.NumberOfTeams;

            var algos = _algoTypeToInformationMapper.Values.ToList();
            var config = new WebAppConfigResponse() { ShirtsColors = shirtsColors, NumberOfTeams = numberOfTeams };

            return new InitialAppConfig() { Algos = algos, Config = config };
        }

        public static IEnumerable<PlayerProperties> GetPlayersProperties(int algoType)
        {
            if (!_algoTypeToInformationMapper.ContainsKey((AlgoType)algoType)) return new List<PlayerProperties>();
            return _algoTypeToInformationMapper[(AlgoType)algoType].PlayerProperties;
        }

        private static List<WebAppTeam> GetDisplayTeams(List<string> shirtsColorNames, List<Algos.Team> teams)
        {
            var results = new List<WebAppTeam>();
            var shirtsColors = Helper.Shuffle(shirtsColorNames);

            var index = 1;
            foreach (var team in teams)
            {
                var shirtColor = shirtsColors[0];
                results.Add(new WebAppTeam() { Players = team.Players, Rank = team.TotalRank, Color = shirtColor, TeamSymbol = ConfigurationManager.ShirtsColorNameToSymbolMapper[shirtColor], TeamName = index.ToString(), TeamId = index });
                index++;
                shirtsColors.RemoveAt(0);
            }

            return results;
        }


    }
}
