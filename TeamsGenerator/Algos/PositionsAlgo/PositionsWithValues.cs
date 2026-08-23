using System;
using System.Collections.Generic;
using System.Linq;
using TeamsGenerator.Orchestration;
using TeamsGenerator.Orchestration.Contracts;
using TeamsGenerator.Utilities;

namespace TeamsGenerator.Algos.PositionsAlgo
{
    public class PositionsWithValues : AlgoManagerBase, IAlgoManager
    {
        private static readonly Random _random = new Random();


        public PositionsWithValues(AlgoConfig config) : base(config)
        {
        }

        public List<Team> GenerateTeams(List<IPlayer> players, List<Team> generatedTeamWithLockedPlayers)
        {
            var teamsResult = new List<Team>();
            var positionsPlayers = players.Cast<PositionsPlayer>().ToList();

            var numberOfPositions = Enum.GetValues(typeof(Position)).Length;
            
            if (generatedTeamWithLockedPlayers != null)
            {
                for (int i = 0; i < _config.TeamsCount; i++)
                {
                    teamsResult.Add(generatedTeamWithLockedPlayers.FirstOrDefault(t => t.Index == i) ?? new Team(i));
                }

                foreach (var team in generatedTeamWithLockedPlayers)
                {
                    foreach (var player in team.Players.Cast<PositionsPlayer>())
                    {
                        var playerRef = positionsPlayers.FirstOrDefault(t => t.Key == player.Key);
                        positionsPlayers.Remove(playerRef);
                    }
                }
            }
            else
            {
                for (int i = 0; i < _config.TeamsCount; i++)
                {
                    teamsResult.Add(new Team(i));
                }
            }

            while (positionsPlayers.Any())
            {
                var playersCountBeforePass = positionsPlayers.Count;

                for (int i = 0; i < numberOfPositions; i++)
                {
                    var position = (Position)i;
                    var playersOfCurrentPosition = positionsPlayers
                        .Where(p => HasPosition(p, position))
                        .ToList();

                    playersOfCurrentPosition = OrderForPosition(
                        playersOfCurrentPosition,
                        position);

                    // Count how many players per team already play in this position
                    var teamPositionCounts = teamsResult
                        .Select(t => new {
                            Team = t,
                            Count = t.Players
                                .Cast<PositionsPlayer>()
                                .Count(p => HasPosition(p, position))
                        })
                        .ToList();

                    // Get the minimum count for this position
                    int minPositionCount = teamPositionCounts.Min(t => t.Count);

                    // Only include teams with the minimal count
                    var teamsToFill = teamPositionCounts
                        .Where(t => t.Count == minPositionCount)
                        .Select(t => t.Team)
                        .OrderBy(t => t.Players.Count) // Optional: maintain balance in player count
                        .ThenBy(t => t.TotalRank)
                        .ToList();


                    int playerIndex = 0;
                    for (int teamIndex = 0; teamIndex < Math.Min(teamsToFill.Count, playersOfCurrentPosition.Count); teamIndex++)
                    {
                        var player = playersOfCurrentPosition[playerIndex++];
                        teamsToFill[teamIndex].AddPlayer(player);
                        positionsPlayers.Remove(player);
                    }
                }

                if (positionsPlayers.Count == playersCountBeforePass)
                {
                    var fallbackPlayer = positionsPlayers
                        .OrderByDescending(player => player.Rank)
                        .First();
                    var fallbackTeam = teamsResult
                        .OrderBy(team => team.Players.Count)
                        .ThenBy(team => team.TotalRank)
                        .First();

                    fallbackTeam.AddPlayer(fallbackPlayer);
                    positionsPlayers.Remove(fallbackPlayer);
                }
            }

            return teamsResult;
        }




        private List<PositionsPlayer> OrderForPosition(
            List<PositionsPlayer> players,
            Position position)
        {
            var activeSkillIds = _config.SkillDefinitions
                .Select(skill => skill.Id)
                .ToList();
            var preferredSkillIds = GetPreferredSkillIds(position)
                .Where(skillId => activeSkillIds.Contains(
                    skillId,
                    StringComparer.OrdinalIgnoreCase))
                .ToList();
            var remainingSkillIds = activeSkillIds
                .Where(skillId => !preferredSkillIds.Contains(
                    skillId,
                    StringComparer.OrdinalIgnoreCase))
                .OrderBy(_ => _random.Next())
                .ToList();
            var orderedSkillIds = preferredSkillIds
                .Concat(remainingSkillIds)
                .ToList();

            if (orderedSkillIds.Count == 0)
            {
                return players
                    .OrderByDescending(player => player.Rank)
                    .ToList();
            }

            IOrderedEnumerable<PositionsPlayer> ordered =
                players.OrderByDescending(player =>
                    player.GetSkillValue(orderedSkillIds[0]));
            foreach (var skillId in orderedSkillIds.Skip(1))
            {
                ordered = ordered.ThenByDescending(player =>
                    player.GetSkillValue(skillId));
            }

            return ordered.ToList();
        }

        private static IEnumerable<string> GetPreferredSkillIds(
            Position position)
        {
            var firstAndLast = position switch
            {
                Position.GK => new[] { "defence", "attack" },
                Position.DC => new[] { "defence", "attack" },
                Position.WB => new[] { "defence", "leadership" },
                Position.MC => new[] { "passing", "stamina" },
                Position.AMC => new[] { "attack", "defence" },
                Position.ST => new[] { "attack", "defence" },
                _ => Array.Empty<string>()
            };
            var middle = new[]
                {
                    "attack",
                    "defence",
                    "stamina",
                    "leadership",
                    "passing"
                }
                .Where(skillId =>
                    !firstAndLast.Contains(skillId))
                .OrderBy(_ => _random.Next());

            return firstAndLast.Take(1)
                .Concat(middle)
                .Concat(firstAndLast.Skip(1));
        }

        private static bool HasPosition(
            PositionsPlayer player,
            Position position)
        {
            return player?.Positions != null
                && player.Positions.Contains(position);
        }

    }

    public enum Position
    {
        GK = 0,
        DC = 1,
        WB = 2,
        MC = 3,
        AMC = 4,
        ST = 5,
    }
}
