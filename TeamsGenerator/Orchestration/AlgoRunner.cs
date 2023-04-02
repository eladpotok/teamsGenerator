using System;
using System.Collections.Generic;
using TeamsGenerator.Algos;
using TeamsGenerator.Algos.BackAndForthAlgo;
using TeamsGenerator.Algos.SkillWiseAlgo;
using TeamsGenerator.Orchestration.Contracts;
using TeamsGenerator.Utilities;

namespace TeamsGenerator.Orchestration
{
    public static class AlgoRunner

    {
        private static Dictionary<AlgoType, Func<string, IPlayersReader>> _algoTypeToPlayersReaderMapper = new Dictionary<AlgoType, Func<string, IPlayersReader>>()
        {
            { AlgoType.BackAndForth, (path) => new BackAndForthPlayersReader(path) },
            { AlgoType.SkillWise, (path) => new SkillWisePlayersReader(path) },
        };

        private static Dictionary<AlgoType, Func<Config, IAlgoManager>> _algoTypeToTeamsGeneratorMapper = new Dictionary<AlgoType, Func<Config, IAlgoManager>>()
        {
            { AlgoType.BackAndForth, (config) => new BackAndForthManager(config) },
            { AlgoType.SkillWise, (config) => new SkillWiseManager(config) },
        };

        public static List<Team> Run(AlgoType algoType, Config config)
        {
            var algoTypeName = ((AlgoType[])Enum.GetValues(typeof(AlgoType)))[(int)algoType - 1];
            var playersFilePathWithoutExtension = $"{Environment.CurrentDirectory}\\algos\\{algoTypeName}Algo\\players";
            var algoCreator = _algoTypeToPlayersReaderMapper[algoTypeName];
            var playersReader = algoCreator.Invoke(playersFilePathWithoutExtension);
            var players = playersReader.GetPlayers();

            var algo = _algoTypeToTeamsGeneratorMapper[algoTypeName];
            var teams = algo.Invoke(config).GenerateTeams(players) ?? null;
            return teams;
        }

    }
}
