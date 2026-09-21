using System;
using System.Collections.Generic;
using System.Diagnostics;
using TeamsGenerator.Algos;
using TeamsGenerator.Algos.AiAlgo;
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
            { AlgoType.Ai, (config) => new AiManager(config) },
        };

        public static List<Algos.Team> Run(AlgoType algoType, List<IPlayer> players, AlgoConfig config, List<Team> generatedTeamsWithLockedPlayers)
        {
            return Run(
                algoType,
                players,
                config,
                generatedTeamsWithLockedPlayers,
                out _);
        }

        internal static List<Algos.Team> Run(
            AlgoType algoType,
            List<IPlayer> players,
            AlgoConfig config,
            List<Team> generatedTeamsWithLockedPlayers,
            out ChemistryOptimizationResult chemistryResult)
        {
            var allPlayers = players.Concat(
                generatedTeamsWithLockedPlayers?.SelectMany(team => team.Players)
                    ?? Enumerable.Empty<IPlayer>());
            foreach (var player in allPlayers.OfType<IConfigurableSkillsPlayer>())
                player.SetActiveSkills(config.SkillDefinitions);
            if (generatedTeamsWithLockedPlayers != null)
            {
                foreach (var team in generatedTeamsWithLockedPlayers)
                {
                    for (var index = 0; index < team.Players.Count; index++)
                    {
                        var locked = team.Players[index];
                        var current = !string.IsNullOrWhiteSpace(locked.Key)
                            ? players.FirstOrDefault(player => player.Key == locked.Key)
                            : null;
                        team.Players[index] = current ?? locked;
                        if (team.Players[index] is SkillWisePlayer skillWise) skillWise.IsLocked = true;
                        if (team.Players[index] is PositionsPlayer positions) positions.IsLocked = true;
                        if (team.Players[index] is BackAndForthPlayer backAndForth) backAndForth.IsLocked = true;
                    }
                    team.TotalRank = team.Players.Sum(PlayerRatings.GetRank);
                }
            }
            var algo = _algoTypeToTeamsGeneratorMapper[algoType];
            var teams = algo.Invoke(config).GenerateTeams(players, generatedTeamsWithLockedPlayers) ?? null;
            chemistryResult = new ChemistryOptimizationResult();
            if (config.UseChemistry)
            {
                chemistryResult = ChemistryTeamBalancer.Apply(
                    teams,
                    algoType,
                    config.ChemistryScores,
                    config.SkillDefinitions);
            }
            return teams;
        }

    }
}
