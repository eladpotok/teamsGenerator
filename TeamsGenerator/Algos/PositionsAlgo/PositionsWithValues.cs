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


        private Dictionary<Position, Func<List<PositionsPlayer>, List<PositionsPlayer>>> _positionToOrderMapper =
               new Dictionary<Position, Func<List<PositionsPlayer>, List<PositionsPlayer>>>()
            {
            { Position.GK, players => OrderWithRandomMiddle(players, p => p.Defence, p => p.Attack, new Func<PositionsPlayer, object>[] { p => p.Stamina, p => p.Passing, p => p.Leadership }) },
            { Position.DC, players => OrderWithRandomMiddle(players, p => p.Defence, p => p.Attack, new Func<PositionsPlayer, object>[] { p => p.Leadership, p => p.Passing, p => p.Stamina }) },
            { Position.WB, players => OrderWithRandomMiddle(players, p => p.Defence, p => p.Leadership, new Func<PositionsPlayer, object>[] { p => p.Stamina, p => p.Passing, p => p.Attack }) },
            { Position.MC, players => OrderWithRandomMiddle(players, p => p.Passing, p => p.Stamina, new Func<PositionsPlayer, object>[] { p => p.Leadership, p => p.Attack, p => p.Defence }) },
            { Position.AMC, players => OrderWithRandomMiddle(players, p => p.Attack, p => p.Defence, new Func<PositionsPlayer, object>[] { p => p.Passing, p => p.Stamina, p => p.Leadership }) },
            { Position.ST, players => OrderWithRandomMiddle(players, p => p.Attack, p => p.Defence, new Func<PositionsPlayer, object>[] { p => p.Stamina, p => p.Passing, p => p.Leadership }) },
            };



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

                    playersOfCurrentPosition = _positionToOrderMapper[position](playersOfCurrentPosition);

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




        private static List<PositionsPlayer> OrderWithRandomMiddle(List<PositionsPlayer> players,
                                                                  Func<PositionsPlayer, object> first,
                                                                  Func<PositionsPlayer, object> last,
                                                                  Func<PositionsPlayer, object>[] middle)
        {
            // Shuffle the middle properties
            var shuffledMiddle = middle.OrderBy(_ => _random.Next()).ToList();

            // Apply ordering step by step
            IOrderedEnumerable<PositionsPlayer> ordered = players.OrderByDescending(first);
            foreach (var prop in shuffledMiddle)
            {
                ordered = ordered.ThenByDescending(prop);
            }
            ordered = ordered.ThenByDescending(last);

            return ordered.ToList();
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
