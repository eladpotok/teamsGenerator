using System;
using System.Collections.Generic;
using System.Diagnostics;
using TeamsGenerator.Algos;
using TeamsGenerator.Algos.BackAndForthAlgo;
using TeamsGenerator.Algos.SkillWiseAlgo;
using TeamsGenerator.API;
using TeamsGenerator.Orchestration.Contracts;
using TeamsGenerator.Utilities;

namespace TeamsGenerator.Orchestration
{

    public static class AlgoRunner
    {

        private static Dictionary<AlgoType, Func<AlgoConfig, IAlgoManager>> _algoTypeToTeamsGeneratorMapper = new Dictionary<AlgoType, Func<AlgoConfig, IAlgoManager>>()
        {
            { AlgoType.BackAndForth, (config) => new BackAndForthManager(config) },
            { AlgoType.SkillWise, (config) => new SkillWiseManager(config) },
        };

        public static List<Algos.Team> Run(AlgoType algoType, List<IPlayer> players, AlgoConfig config)
        {
            //var algoTypeName = ((AlgoType[])Enum.GetValues(typeof(AlgoType)))[(int)algoType - 1];
            //var playersFilePathWithoutExtension = $"{Process.GetCurrentProcess().StartInfo.WorkingDirectory}\\algos\\{algoTypeName}Algo\\players";
            //var playersFilePathWithoutExtension = @"C:\Users\potok\OneDrive\שולחן העבודה\לימודים\Projects\teamsGenerator\TeamsGenerator\Algos\BackAndForthAlgo\players.json";
            //var algoCreator = _algoTypeToPlayersReaderMapper[algoTypeName];
            //var playersReader = algoCreator.Invoke(playersFilePathWithoutExtension);
            //var players = playersReader.GetPlayers();

            var algo = _algoTypeToTeamsGeneratorMapper[algoType];
            var teams = algo.Invoke(config).GenerateTeams(players) ?? null;
            return teams;
        }

    }
}
