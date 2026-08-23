using System;
using System.Collections.Generic;
using System.Linq;
using TeamsGenerator.Algos;
using TeamsGenerator.Algos.PositionsAlgo;
using TeamsGenerator.Algos.SkillWiseAlgo;
using TeamsGenerator.API;
using TeamsGenerator.Orchestration.Contracts;

namespace TeamsGenerator.Orchestration
{
    internal static class ChemistryTeamBalancer
    {
        private const double ChemistryWeight = 0.75;
        private const double GoodChemistryThreshold = 0.05;
        private const double ImprovementThreshold = 0.001;
        private const int MaximumIterations = 100;

        internal static ChemistryOptimizationResult Apply(
            IList<Team> teams,
            AlgoType algoType,
            IReadOnlyDictionary<string, double> chemistryScores,
            IReadOnlyList<SkillDefinition> skillDefinitions)
        {
            if (teams == null
                || teams.Count < 2
                || chemistryScores == null
                || chemistryScores.Count == 0
                || (algoType != AlgoType.SkillWise && algoType != AlgoType.Positions))
            {
                return new ChemistryOptimizationResult();
            }

            var relevantPartnershipCount = CountRelevantPartnerships(
                teams,
                chemistryScores);
            if (relevantPartnershipCount == 0)
            {
                return new ChemistryOptimizationResult();
            }

            var initialScore = CalculateObjective(
                teams,
                algoType,
                chemistryScores,
                skillDefinitions);
            var swapCount = 0;

            for (var iteration = 0; iteration < MaximumIterations; iteration++)
            {
                var currentScore = CalculateObjective(
                    teams,
                    algoType,
                    chemistryScores,
                    skillDefinitions);
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
                                    chemistryScores,
                                    skillDefinitions);
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
                swapCount++;
            }

            var finalScore = CalculateObjective(
                teams,
                algoType,
                chemistryScores,
                skillDefinitions);
            var improvementPercent = initialScore <= 0
                ? 0
                : Math.Max(0, (initialScore - finalScore) / initialScore * 100);

            return new ChemistryOptimizationResult
            {
                WasEvaluated = true,
                PartnershipCount = relevantPartnershipCount,
                SwapCount = swapCount,
                InitialScore = initialScore,
                FinalScore = finalScore,
                BalanceImprovementPercent = Math.Round(
                    improvementPercent,
                    1),
                NotablePartnerships = GetNotablePartnerships(
                    teams,
                    chemistryScores),
                TeamRatings = GetTeamRatings(
                    teams,
                    chemistryScores)
            };
        }

        private static double CalculateObjective(
            IEnumerable<Team> teams,
            AlgoType algoType,
            IReadOnlyDictionary<string, double> chemistryScores,
            IReadOnlyList<SkillDefinition> skillDefinitions)
        {
            var teamList = teams.ToList();
            var effectiveStrengths = teamList
                .Select(team => GetAverageRank(team)
                    + ChemistryWeight * GetTeamChemistry(team, chemistryScores))
                .ToList();
            var effectiveSpread = effectiveStrengths.Max() - effectiveStrengths.Min();
            var skillSpread = GetSkillSpread(
                teamList,
                skillDefinitions);
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

        private static double GetSkillSpread(
            IList<Team> teams,
            IReadOnlyList<SkillDefinition> skillDefinitions)
        {
            return SkillDefinition.Normalize(skillDefinitions)
                .Sum(skill => GetSpread(
                    teams,
                    player => GetSkillValue(
                        player,
                        skill.Id)));
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

        private static double GetSkillValue(
            IPlayer player,
            string skillId)
        {
            return player is IConfigurableSkillsPlayer configurablePlayer
                ? configurablePlayer.GetSkillValue(skillId)
                : SkillDefinition.DefaultValue;
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

        private static List<ChemistryPartnership> GetNotablePartnerships(
            IEnumerable<Team> teams,
            IReadOnlyDictionary<string, double> chemistryScores)
        {
            return teams
                .SelectMany(team => GetTeamPartnerships(
                    team,
                    chemistryScores))
                .OrderByDescending(partnership => partnership.Score)
                .Take(2)
                .Select(partnership => new ChemistryPartnership
                {
                    FirstPlayerName = partnership.FirstPlayerName,
                    SecondPlayerName = partnership.SecondPlayerName
                })
                .ToList();
        }

        private static List<ChemistryTeamRating> GetTeamRatings(
            IEnumerable<Team> teams,
            IReadOnlyDictionary<string, double> chemistryScores)
        {
            return teams
                .Select(team => CreateTeamRating(
                    team,
                    chemistryScores))
                .Where(rating => rating.EvidenceCount > 0)
                .ToList();
        }

        private static ChemistryTeamRating CreateTeamRating(
            Team team,
            IReadOnlyDictionary<string, double> chemistryScores)
        {
            var partnerships = GetTeamPartnerships(
                    team,
                    chemistryScores)
                .ToList();
            var allPairScores = GetTeamPairScores(
                    team,
                    chemistryScores)
                .ToList();
            var links = partnerships
                .SelectMany(partnership => new[]
                {
                    new
                    {
                        PlayerKey = partnership.FirstPlayerKey,
                        PartnerName = partnership.SecondPlayerName
                    },
                    new
                    {
                        PlayerKey = partnership.SecondPlayerKey,
                        PartnerName = partnership.FirstPlayerName
                    }
                })
                .GroupBy(link =>
                    link.PlayerKey,
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => new ChemistryPlayerLink
                {
                    PlayerKey = group.Key,
                    PartnerNames = group
                        .Select(link => link.PartnerName)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(name => name)
                        .ToList()
                })
                .ToList();
            var averageScore = allPairScores.Count == 0
                ? 0
                : allPairScores.Average();

            return new ChemistryTeamRating
            {
                TeamIndex = team.Index,
                Score = (int)Math.Round(
                    Math.Clamp((averageScore + 1) * 50, 0, 100)),
                EvidenceCount = allPairScores.Count,
                PlayerLinks = links
            };
        }

        private static IEnumerable<double> GetTeamPairScores(
            Team team,
            IReadOnlyDictionary<string, double> chemistryScores)
        {
            for (var first = 0; first < team.Players.Count; first++)
            {
                for (var second = first + 1;
                     second < team.Players.Count;
                     second++)
                {
                    var pairKey = ChemistryPairKey.Create(
                        team.Players[first].Key,
                        team.Players[second].Key);
                    if (pairKey != null
                        && chemistryScores.TryGetValue(
                            pairKey,
                            out var score))
                    {
                        yield return score;
                    }
                }
            }
        }

        private static int CountRelevantPartnerships(
            IEnumerable<Team> teams,
            IReadOnlyDictionary<string, double> chemistryScores)
        {
            var players = teams
                .SelectMany(team => team.Players)
                .GroupBy(player => player.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
            var count = 0;

            for (var first = 0; first < players.Count; first++)
            {
                for (var second = first + 1;
                     second < players.Count;
                     second++)
                {
                    var pairKey = ChemistryPairKey.Create(
                        players[first].Key,
                        players[second].Key);
                    if (pairKey != null
                        && chemistryScores.ContainsKey(pairKey))
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        private static IEnumerable<ScoredPartnership> GetTeamPartnerships(
            Team team,
            IReadOnlyDictionary<string, double> chemistryScores)
        {
            for (var first = 0; first < team.Players.Count; first++)
            {
                for (var second = first + 1;
                     second < team.Players.Count;
                     second++)
                {
                    var pairKey = ChemistryPairKey.Create(
                        team.Players[first].Key,
                        team.Players[second].Key);
                    if (pairKey != null
                        && chemistryScores.TryGetValue(
                            pairKey,
                            out var score)
                        && score >= GoodChemistryThreshold)
                    {
                        yield return new ScoredPartnership
                        {
                            FirstPlayerKey = team.Players[first].Key,
                            FirstPlayerName = team.Players[first].Name,
                            SecondPlayerKey = team.Players[second].Key,
                            SecondPlayerName = team.Players[second].Name,
                            Score = score
                        };
                    }
                }
            }
        }

        private sealed class SwapCandidate
        {
            public Team FirstTeam { get; set; }
            public int FirstPlayerIndex { get; set; }
            public Team SecondTeam { get; set; }
            public int SecondPlayerIndex { get; set; }
        }

        private sealed class ScoredPartnership
        {
            public string FirstPlayerKey { get; set; }
            public string FirstPlayerName { get; set; }
            public string SecondPlayerKey { get; set; }
            public string SecondPlayerName { get; set; }
            public double Score { get; set; }
        }
    }

    internal class ChemistryOptimizationResult
    {
        internal bool WasEvaluated { get; set; }
        internal int PartnershipCount { get; set; }
        internal int SwapCount { get; set; }
        internal double InitialScore { get; set; }
        internal double FinalScore { get; set; }
        internal double BalanceImprovementPercent { get; set; }
        internal List<ChemistryPartnership> NotablePartnerships { get; set; } =
            new List<ChemistryPartnership>();
        internal List<ChemistryTeamRating> TeamRatings { get; set; } =
            new List<ChemistryTeamRating>();
    }
}
