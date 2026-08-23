using System;
using System.Collections.Generic;
using System.Linq;
using TeamsGenerator.API;
using TeamsGenerator.Orchestration.Contracts;

namespace TeamsGenerator.Algos.SkillWiseAlgo
{
    internal static class SkillWiseTeamOptimizer
    {
        private const double ImprovementThreshold = 0.0001;
        private const int MaximumIterations = 30;
        private const double RankVarianceWeight = 0.5;
        private const double SkillSpreadWeight = 0.5;
        private const double GoalkeeperVarianceWeight = 4;
        private const double TeamSizeVarianceWeight = 100;

        internal static void Optimize(
            IList<Team> teams,
            IReadOnlyList<SkillDefinition> skillDefinitions,
            ISet<string> lockedPlayerKeys,
            ISet<IPlayer> lockedPlayers)
        {
            for (var iteration = 0;
                 iteration < MaximumIterations;
                 iteration++)
            {
                var currentScore = CalculateScore(
                    teams,
                    skillDefinitions);
                var bestScore = currentScore;
                SwapCandidate bestSwap = null;

                for (var firstTeamIndex = 0;
                     firstTeamIndex < teams.Count;
                     firstTeamIndex++)
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
                            if (IsLocked(
                                firstTeam.Players[firstPlayerIndex],
                                lockedPlayerKeys,
                                lockedPlayers))
                            {
                                continue;
                            }

                            for (var secondPlayerIndex = 0;
                                 secondPlayerIndex < secondTeam.Players.Count;
                                 secondPlayerIndex++)
                            {
                                if (IsLocked(
                                    secondTeam.Players[secondPlayerIndex],
                                    lockedPlayerKeys,
                                    lockedPlayers))
                                {
                                    continue;
                                }

                                SwapPlayers(
                                    firstTeam,
                                    firstPlayerIndex,
                                    secondTeam,
                                    secondPlayerIndex);
                                var candidateScore = CalculateScore(
                                    teams,
                                    skillDefinitions);
                                SwapPlayers(
                                    firstTeam,
                                    firstPlayerIndex,
                                    secondTeam,
                                    secondPlayerIndex);

                                if (candidateScore
                                    + ImprovementThreshold < bestScore)
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
                    return;
                }

                SwapPlayers(
                    bestSwap.FirstTeam,
                    bestSwap.FirstPlayerIndex,
                    bestSwap.SecondTeam,
                    bestSwap.SecondPlayerIndex);
            }
        }

        internal static double CalculateScore(
            IEnumerable<Team> teams,
            IReadOnlyList<SkillDefinition> skillDefinitions)
        {
            var teamList = teams.ToList();
            if (teamList.Count == 0)
            {
                return 0;
            }

            var skills = SkillDefinition.Normalize(skillDefinitions);
            var skillVariance = skills.Average(skill =>
                GetVariance(teamList.Select(team =>
                    GetSkillAverage(team, skill.Id))));
            var skillSpread = skills.Average(skill =>
                GetSpread(teamList.Select(team =>
                    GetSkillAverage(team, skill.Id))));
            var rankVariance = GetVariance(teamList.Select(GetRankAverage));
            var goalkeeperVariance = GetVariance(teamList.Select(team =>
                (double)team.Players
                    .Cast<SkillWisePlayer>()
                    .Count(player => player.IsGoalKeeper)));
            var teamSizeVariance = GetVariance(teamList.Select(team =>
                (double)team.Players.Count));

            return skillVariance
                + SkillSpreadWeight * skillSpread
                + RankVarianceWeight * rankVariance
                + GoalkeeperVarianceWeight * goalkeeperVariance
                + TeamSizeVarianceWeight * teamSizeVariance;
        }

        private static double GetSkillAverage(
            Team team,
            string skillId)
        {
            return team.Players.Count == 0
                ? 0
                : team.Players
                    .Cast<SkillWisePlayer>()
                    .Average(player => player.GetSkillValue(skillId));
        }

        private static double GetRankAverage(Team team)
        {
            return team.Players.Count == 0
                ? 0
                : team.Players.Average(player => player.Rank);
        }

        private static double GetVariance(IEnumerable<double> source)
        {
            var values = source.ToList();
            if (values.Count == 0)
            {
                return 0;
            }

            var average = values.Average();
            return values.Average(value =>
                Math.Pow(value - average, 2));
        }

        private static double GetSpread(IEnumerable<double> source)
        {
            var values = source.ToList();
            return values.Count == 0
                ? 0
                : values.Max() - values.Min();
        }

        private static bool IsLocked(
            IPlayer player,
            ISet<string> lockedPlayerKeys,
            ISet<IPlayer> lockedPlayers)
        {
            var skillWisePlayer = (SkillWisePlayer)player;
            return lockedPlayers.Contains(player)
                || skillWisePlayer.IsLocked
                || (!string.IsNullOrWhiteSpace(player.Key)
                    && lockedPlayerKeys.Contains(player.Key));
        }

        private static void SwapPlayers(
            Team firstTeam,
            int firstPlayerIndex,
            Team secondTeam,
            int secondPlayerIndex)
        {
            var firstPlayer = firstTeam.Players[firstPlayerIndex];
            firstTeam.Players[firstPlayerIndex] =
                secondTeam.Players[secondPlayerIndex];
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
    }
}
