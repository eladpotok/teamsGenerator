using System;
using System.Collections.Generic;
using System.Diagnostics;
using TeamsGenerator.Algos;
using TeamsGenerator.Algos.BackAndForthAlgo;
using TeamsGenerator.Algos.PositionsAlgo;
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
            { AlgoType.Positions, (config) => new PositionsWithValues(config) },
        };

        public static List<Algos.Team> Run(AlgoType algoType, List<IPlayer> players, AlgoConfig config, List<Team> generatedTeamsWithLockedPlayers)
        {
            var algo = _algoTypeToTeamsGeneratorMapper[algoType];
            var teams = algo.Invoke(config).GenerateTeams(players, generatedTeamsWithLockedPlayers) ?? null;
            return teams;
        }

    }
}
