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
            { AlgoType.BackAndForth, (path) => new BackAndForthManager(path) },
            { AlgoType.SkillWise, (path) => new SkillWiseManager(path) },
        };

        public static List<Team> Run(AlgoType algoType)
        {
            var algoTypeName = ((AlgoType[])Enum.GetValues(typeof(AlgoType)))[(int)algoType-1];
            var playersFilePath = $"{Environment.CurrentDirectory}\\{algoTypeName}Algo\\players.json";
            var algoCreator = _algoMappers[algoTypeName];
            var algoManager = algoCreator.Invoke(playersFilePath);
            return algoManager.GenerateTeams();
        }
    }
}
