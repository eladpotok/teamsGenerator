using System;
using System.Collections.Generic;
using System.Linq;
using TeamsGenerator.Algos;
using TeamsGenerator.Algos.PositionsAlgo;
using TeamsGenerator.Algos.SkillWiseAlgo;
using TeamsGenerator.Orchestration.Contracts;

namespace TeamsGenerator.Orchestration
{
    internal static class ChemistryTeamBalancer
    {
        private const double ChemistryWeight = 0.75;
        private const double ImprovementThreshold = 0.001;
        private const int MaximumIterations = 100;

        internal static void Apply(
            IList<Team> teams,
            AlgoType algoType,
            IReadOnlyDictionary<string, double> chemistryScores)
        {
            if (teams == null
                || chemistryScores == null
                || chemistryScores.Count == 0
                || (algoType != AlgoType.SkillWise && algoType != AlgoType.Positions))
            {
                return;
            }

            for (var iteration = 0; iteration < MaximumIterations; iteration++)
            {
                var currentScore = CalculateObjective(teams, algoType, chemistryScores);
                SwapCandidate bestSwap = null;
                var bestScore = currentScore;

                for (var firstTeamIndex = 0; firstTeamIndex < teams.Count; firstTeamIndex++)
                {
                    for (var secondTeamIndex = firstTeamIndex + 1;
                         secondTeamIndex < teams.Count;
                         secondTeamIndex++)
                    {
                        var firstTeam = teams[firstTeamIndex];
                        var secondTeam = teams[secondTeamIndex];

                        for (var firstPlayerIndex = 0;
                             firstPlayerIndex < firstTeam.Players.Count;
                             firstPlayerIndex++)
                        {
                            if (IsLocked(firstTeam.Players[firstPlayerIndex]))
                            {
                                continue;
                            }

                            for (var secondPlayerIndex = 0;
                                 secondPlayerIndex < secondTeam.Players.Count;
                                 secondPlayerIndex++)
                            {
                                if (IsLocked(secondTeam.Players[secondPlayerIndex]))
                                {
                                    continue;
                                }

                                SwapPlayers(
                                    firstTeam,
                                    firstPlayerIndex,
                                    secondTeam,
                                    secondPlayerIndex);
                                var candidateScore = CalculateObjective(
                                    teams,
                                    algoType,
                                    chemistryScores);
                                SwapPlayers(
                                    firstTeam,
                                    firstPlayerIndex,
                                    secondTeam,
                                    secondPlayerIndex);

                                if (candidateScore + ImprovementThreshold < bestScore)
                                {
                                    bestScore = candidateScore;
                                    bestSwap = new SwapCandidate
                                    {
                                        FirstTeam = firstTeam,
                                        FirstPlayerIndex = firstPlayerIndex,
                                        SecondTeam = secondTeam,
                                        SecondPlayerIndex = secondPlayerIndex
                                    };
                                }
                            }
                        }
                    }
                }

                if (bestSwap == null)
                {
                    break;
                }

                SwapPlayers(
                    bestSwap.FirstTeam,
                    bestSwap.FirstPlayerIndex,
                    bestSwap.SecondTeam,
                    bestSwap.SecondPlayerIndex);
            }
        }

        private static double CalculateObjective(
            IEnumerable<Team> teams,
            AlgoType algoType,
            IReadOnlyDictionary<string, double> chemistryScores)
        {
            var teamList = teams.ToList();
            var effectiveStrengths = teamList
                .Select(team => GetAverageRank(team)
                    + ChemistryWeight * GetTeamChemistry(team, chemistryScores))
                .ToList();
            var effectiveSpread = effectiveStrengths.Max() - effectiveStrengths.Min();
            var skillSpread = GetSkillSpread(teamList);
            var positionPenalty = algoType == AlgoType.Positions
                ? GetPositionPenalty(teamList)
                : 0;
            var goalkeeperPenalty = algoType == AlgoType.SkillWise
                ? GetGoalkeeperPenalty(teamList)
                : 0;

            return effectiveSpread
                + 0.15 * skillSpread
                + 10 * positionPenalty
                + 10 * goalkeeperPenalty;
        }

        private static double GetAverageRank(Team team)
        {
            return team.Players.Count == 0
                ? 0
                : team.Players.Average(player => player.Rank);
        }

        private static double GetTeamChemistry(
            Team team,
            IReadOnlyDictionary<string, double> chemistryScores)
        {
            var scores = new List<double>();
            for (var first = 0; first < team.Players.Count; first++)
            {
                for (var second = first + 1; second < team.Players.Count; second++)
                {
                    var pairKey = ChemistryPairKey.Create(
                        team.Players[first].Key,
                        team.Players[second].Key);
                    if (pairKey != null
                        && chemistryScores.TryGetValue(pairKey, out var score))
                    {
                        scores.Add(score);
                    }
                }
            }

            return scores.Count == 0 ? 0 : scores.Average();
        }

        private static double GetSkillSpread(IList<Team> teams)
        {
            return GetSpread(teams, player => GetSkills(player).Attack)
                + GetSpread(teams, player => GetSkills(player).Defence)
                + GetSpread(teams, player => GetSkills(player).Stamina)
                + GetSpread(teams, player => GetSkills(player).Leadership)
                + GetSpread(teams, player => GetSkills(player).Passing);
        }

        private static double GetSpread(
            IEnumerable<Team> teams,
            Func<IPlayer, double> selector)
        {
            var values = teams
                .Select(team => team.Players.Count == 0
                    ? 0
                    : team.Players.Average(selector))
                .ToList();
            return values.Max() - values.Min();
        }

        private static SkillValues GetSkills(IPlayer player)
        {
            if (player is SkillWisePlayer skillWisePlayer)
            {
                return new SkillValues
                {
                    Attack = skillWisePlayer.Attack,
                    Defence = skillWisePlayer.Defence,
                    Stamina = skillWisePlayer.Stamina,
                    Leadership = skillWisePlayer.Leadership,
                    Passing = skillWisePlayer.Passing
                };
            }

            var positionsPlayer = (PositionsPlayer)player;
            return new SkillValues
            {
                Attack = positionsPlayer.Attack,
                Defence = positionsPlayer.Defence,
                Stamina = positionsPlayer.Stamina,
                Leadership = positionsPlayer.Leadership,
                Passing = positionsPlayer.Passing
            };
        }

        private static double GetPositionPenalty(IEnumerable<Team> teams)
        {
            var teamList = teams.ToList();
            return Enum.GetValues(typeof(Position))
                .Cast<Position>()
                .Sum(position =>
                {
                    var counts = teamList
                        .Select(team => team.Players
                            .Cast<PositionsPlayer>()
                            .Count(player => player.Positions?.Contains(position) == true))
                        .ToList();
                    return counts.Max() - counts.Min();
                });
        }

        private static double GetGoalkeeperPenalty(IEnumerable<Team> teams)
        {
            var counts = teams
                .Select(team => team.Players
                    .Cast<SkillWisePlayer>()
                    .Count(player => player.IsGoalKeeper))
                .ToList();
            return counts.Max() - counts.Min();
        }

        private static bool IsLocked(IPlayer player)
        {
            if (player is SkillWisePlayer skillWisePlayer)
            {
                return skillWisePlayer.IsLocked;
            }

            if (player is PositionsPlayer positionsPlayer)
            {
                return positionsPlayer.IsLocked;
            }

            return false;
        }

        private static void SwapPlayers(
            Team firstTeam,
            int firstPlayerIndex,
            Team secondTeam,
            int secondPlayerIndex)
        {
            var firstPlayer = firstTeam.Players[firstPlayerIndex];
            firstTeam.Players[firstPlayerIndex] = secondTeam.Players[secondPlayerIndex];
            secondTeam.Players[secondPlayerIndex] = firstPlayer;
            firstTeam.TotalRank = firstTeam.Players.Sum(player => player.Rank);
            secondTeam.TotalRank = secondTeam.Players.Sum(player => player.Rank);
        }

        private sealed class SwapCandidate
        {
            public Team FirstTeam { get; set; }
            public int FirstPlayerIndex { get; set; }
            public Team SecondTeam { get; set; }
            public int SecondPlayerIndex { get; set; }
        }

        private sealed class SkillValues
        {
            public double Attack { get; set; }
            public double Defence { get; set; }
            public double Stamina { get; set; }
            public double Leadership { get; set; }
            public double Passing { get; set; }
        }
    }
}
