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
            var positionsPlayers = players.Cast<PositionsPlayer>().ToList();
            var teamsResult = new List<Team>();

            var numberOfPositions = Enum.GetValues(typeof(Position)).Length;
            for (int i = 0; i < _config.TeamsCount; i++)
            {
                teamsResult.Add(new Team());
            }

            while (positionsPlayers.Any())
            {
                for (int i = 0; i < numberOfPositions; i++)
                {
                    var position = (Position)i;
                    var playersOfCurrentPosition = positionsPlayers.Where(p => p.Positions.Contains(position)).ToList();
                    playersOfCurrentPosition = _positionToOrderMapper[position](playersOfCurrentPosition);

                    teamsResult = teamsResult.OrderBy(t => t.Players.Count).OrderBy(t => t.TotalRank).ToList();

                    var playerIndex = 0;
                    for (int teamIndex = 0; teamIndex < Math.Min(_config.TeamsCount, playersOfCurrentPosition.Count); teamIndex++)
                    {
                        var player = playersOfCurrentPosition[playerIndex++];
                        teamsResult[teamIndex].AddPlayer(player);
                        positionsPlayers.Remove(player);
                    }
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
