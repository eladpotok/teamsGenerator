using System;
using System.Collections.Generic;
using TeamsGenerator.Algos.BackAndForthAlgo;
using TeamsGenerator.Algos.SkillWiseAlgo;
using TeamsGenerator.Orchestration.Contracts;

namespace TeamsGenerator.Orchestration
{
    public static class AlgoRunner

    {
        private static Dictionary<AlgoType, Func<string, IAlgoManager>> _algoMappers = new Dictionary<AlgoType, Func<string, IAlgoManager>>()
        {
            { AlgoType.BackAndForth, (path) => new BackAndForthManager(new BackAndForthPlayersReader(path)) },
            { AlgoType.SkillWise, (path) => new SkillWiseManager(new SkillWisePlayersReader(path)) },
        };

        public static List<Team> Run(AlgoType algoType)
        {
            var algoTypeName = ((AlgoType[])Enum.GetValues(typeof(AlgoType)))[(int)algoType - 1];
            var playersFilePathWithoutExtension = $"{Environment.CurrentDirectory}\\algos\\{algoTypeName}Algo\\players";
            var algoCreator = _algoMappers[algoTypeName];
            var algoManager = algoCreator.Invoke(playersFilePathWithoutExtension);
            return algoManager.GenerateTeams();
        }
    }
}
