using System;
using System.Collections.Generic;
using System.Linq;
using TeamsGenerator.Algos;
using TeamsGenerator.API;
using TeamsGenerator.Orchestration;
using TeamsGenerator.Orchestration.Contracts;

namespace TeamsGenerator.Algos.SkillWiseAlgo
{
    public class SkillWiseManager : AlgoManagerBase, IAlgoManager
    {
        private const int MinimumCandidateCount = 24;
        private const int MaximumCandidateCount = 96;
        private const int CandidatesToOptimize = 6;

        private readonly Random _random = new Random();

        public SkillWiseManager(AlgoConfig config) : base(config)
        {
        }

        public List<Team> GenerateTeams(
            List<IPlayer> players,
            List<Team> generatedTeamWithLockedPlayers)
        {
            if (players == null)
            {
                throw new ArgumentNullException(nameof(players));
            }

            if (_config.TeamsCount <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(_config.TeamsCount),
                    "Teams count must be greater than zero.");
            }

            var skillDefinitions = SkillDefinition.Normalize(
                _config.SkillDefinitions);
            var skillWisePlayers = players.Cast<SkillWisePlayer>().ToList();
            var lockedPlayerKeys = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            var lockedPlayers = new HashSet<IPlayer>();
            var baseTeams = CreateBaseTeams(
                generatedTeamWithLockedPlayers,
                lockedPlayerKeys,
                lockedPlayers);
            var unlockedPlayers = skillWisePlayers
                .Where(player => !IsPreassigned(
                    player,
                    lockedPlayerKeys,
                    lockedPlayers))
                .ToList();
            var candidateCount = Math.Min(
                MaximumCandidateCount,
                Math.Max(MinimumCandidateCount, unlockedPlayers.Count * 2));
            var candidates = new List<Candidate>(candidateCount);

            for (var candidateIndex = 0;
                 candidateIndex < candidateCount;
                 candidateIndex++)
            {
                var teams = CloneTeams(baseTeams);
                FillTeams(
                    teams,
                    unlockedPlayers,
                    skillDefinitions,
                    candidateIndex > 0);
                candidates.Add(new Candidate
                {
                    Teams = teams,
                    Score = SkillWiseTeamOptimizer.CalculateScore(
                        teams,
                        skillDefinitions)
                });
            }

            var finalists = candidates
                .OrderBy(candidate => candidate.Score)
                .Take(CandidatesToOptimize)
                .ToList();

            foreach (var finalist in finalists)
            {
                SkillWiseTeamOptimizer.Optimize(
                    finalist.Teams,
                    skillDefinitions,
                    lockedPlayerKeys,
                    lockedPlayers);
                finalist.Score = SkillWiseTeamOptimizer.CalculateScore(
                    finalist.Teams,
                    skillDefinitions);
            }

            return finalists
                .OrderBy(candidate => candidate.Score)
                .First()
                .Teams;
        }

        private List<Team> CreateBaseTeams(
            IEnumerable<Team> lockedTeams,
            ISet<string> lockedPlayerKeys,
            ISet<IPlayer> lockedPlayers)
        {
            var teams = Enumerable.Range(0, _config.TeamsCount)
                .Select(index => new Team(index))
                .ToList();

            if (lockedTeams == null)
            {
                return teams;
            }

            var teamIndex = 0;
            foreach (var lockedTeam in lockedTeams.Take(_config.TeamsCount))
            {
                foreach (var player in lockedTeam.Players)
                {
                    teams[teamIndex].AddPlayer(player);
                    lockedPlayers.Add(player);
                    if (!string.IsNullOrWhiteSpace(player.Key))
                    {
                        lockedPlayerKeys.Add(player.Key);
                    }
                }

                teamIndex++;
            }

            return teams;
        }

        private void FillTeams(
            IList<Team> teams,
            IEnumerable<SkillWisePlayer> players,
            IReadOnlyList<SkillDefinition> skillDefinitions,
            bool allowCandidateVariation)
        {
            var playersLeft = players.ToList();
            var skills = skillDefinitions.ToList();
            var totalPlayerCount =
                teams.Sum(team => team.Players.Count) + playersLeft.Count;
            var maximumTeamSize = (int)Math.Ceiling(
                (double)totalPlayerCount / teams.Count);
            var skillIndex = skills.Count;

            while (playersLeft.Count > 0)
            {
                if (skillIndex >= skills.Count)
                {
                    skills = Shuffle(skills);
                    skillIndex = 0;
                }

                var skill = skills[skillIndex++];
                var eligibleTeams = teams
                    .Where(team => team.Players.Count < maximumTeamSize)
                    .ToList();
                if (eligibleTeams.Count == 0)
                {
                    eligibleTeams = teams
                        .Where(team => team.Players.Count
                            == teams.Min(candidate => candidate.Players.Count))
                        .ToList();
                }

                var minimumPlayerCount = eligibleTeams
                    .Min(team => team.Players.Count);
                var team = eligibleTeams
                    .Where(candidate =>
                        candidate.Players.Count == minimumPlayerCount)
                    .OrderBy(candidate => GetSkillAverage(candidate, skill.Id))
                    .ThenBy(GetRankAverage)
                    .ThenBy(candidate => _random.Next())
                    .First();
                var orderedPlayers = playersLeft
                    .OrderByDescending(player =>
                        player.GetSkillValue(skill.Id))
                    .ThenByDescending(player => player.Rank)
                    .ThenBy(player => _random.Next())
                    .ToList();
                var selectionWindow = allowCandidateVariation
                    ? Math.Min(3, orderedPlayers.Count)
                    : 1;
                var player = orderedPlayers[
                    _random.Next(selectionWindow)];

                team.AddPlayer(player);
                playersLeft.Remove(player);
            }
        }

        private List<SkillDefinition> Shuffle(
            IEnumerable<SkillDefinition> skills)
        {
            return skills
                .OrderBy(skill => _random.Next())
                .ToList();
        }

        private static List<Team> CloneTeams(IEnumerable<Team> source)
        {
            return source
                .Select(team =>
                {
                    var clone = new Team(team.Index);
                    foreach (var player in team.Players)
                    {
                        clone.AddPlayer(player);
                    }

                    return clone;
                })
                .ToList();
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

        private static bool IsPreassigned(
            SkillWisePlayer player,
            ISet<string> lockedPlayerKeys,
            ISet<IPlayer> lockedPlayers)
        {
            return lockedPlayers.Contains(player)
                || (!string.IsNullOrWhiteSpace(player.Key)
                    && lockedPlayerKeys.Contains(player.Key));
        }

        private sealed class Candidate
        {
            public List<Team> Teams { get; set; }
            public double Score { get; set; }
        }
    }
}
