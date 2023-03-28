using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TeamsGenerator.Algo.Contracts;
using TeamsGenerator.BackAndForthAlgo;
using TeamsGenerator.SkillWiseAlgo;

namespace TeamsGenerator.Algo
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
            var algoTypeName = ((AlgoType[])Enum.GetValues(typeof(AlgoType)))[(int)algoType-1];
            var playersFilePathWithoutExtension = $"{Environment.CurrentDirectory}\\{algoTypeName}Algo\\players";
            var algoCreator = _algoMappers[algoTypeName];
            var algoManager = algoCreator.Invoke(playersFilePathWithoutExtension);
            return algoManager.GenerateTeams();
        }
    }
}
