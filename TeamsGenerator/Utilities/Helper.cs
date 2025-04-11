using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using TeamsGenerator.Algos;
using TeamsGenerator.API;
using TeamsGenerator.CLI;
using TeamsGenerator.DataReaders;
using TeamsGenerator.Orchestration;
using TeamsGenerator.Orchestration.Contracts;

namespace TeamsGenerator.Utilities
{
    internal static class Helper
    {
        public static List<T> Shuffle<T>(List<T> items)
        {
            var result = new List<T>();

            var count = items.Count - 1;
            var random = new Random();
            for (int i = 0; i < items.Count; i++)
            {
                var playerToAddIndex = random.Next(0, count + 1);
                result.Add(items[playerToAddIndex]);

                var p1 = items[count];
                var p2 = items[playerToAddIndex];

                items[count] = p2;
                items[playerToAddIndex] = p1;
                count--;
            }

            return result;
        }

        public static string GetResultsAsText(List<IDisplayTeam> teams, bool showPlayerStats)
        {
            System.Threading.Thread.CurrentThread.CurrentUICulture = CultureInfo.GetCultureInfo("en-us");
            var textToCopy = "*" + DateTime.Now.ToString("dddd", new CultureInfo("en-us")) + "  " + DateTime.Now.ToShortDateString() + "*\n";
            var count = 1;
            foreach (var team in teams)
            {
                var teamColor = team.Color;
                textToCopy += $"{team.TeamSymbol}Team: {count} | Color: {team.Color}";
                textToCopy += showPlayerStats ? $"({team.GetAvarage()})\n" : "\n";
                foreach (var player in team.Players)
                {
                    var playerDetail = $"⚽ {player.Name}".PadRight(20);
                    playerDetail += showPlayerStats ? $"{player.Rank}" : "\n";
                    textToCopy += playerDetail;
                }
                textToCopy += "--------------------------------\n";
                count++;
            }

            return textToCopy;
        }

        /// <summary>
        /// Sort players by rank. for sequence of players with the same rank, it sorts them randomly.
        /// </summary>
        /// <param name="players"></param>
        /// <returns></returns>
        public static List<IPlayer> SortPlayersByRank(List<IPlayer> players)
        {
            var result = new List<IPlayer>();
            var orederedPlayers = players.OrderBy(p => p.Rank).ToList();
            var currPlayer = orederedPlayers[0];

            var playersToShuffle = new List<IPlayer>();
            playersToShuffle.Add(currPlayer);
            for (int i = 1; i < orederedPlayers.Count; i++)
            {
                if (orederedPlayers[i].Rank == currPlayer.Rank)
                {
                    playersToShuffle.Add(orederedPlayers[i]);
                }
                else
                {
                    var shuffledPlayers = Shuffle(playersToShuffle);
                    result.AddRange(shuffledPlayers);
                    currPlayer = orederedPlayers[i];
                    playersToShuffle.Clear();
                    playersToShuffle.Add(currPlayer);
                }
            }

            if (playersToShuffle.Any())
            {
                var shuffledPlayers = Shuffle(playersToShuffle);
                result.AddRange(shuffledPlayers);
            }

            result.Reverse();
            return result;
        }

        public static List<IPlayer> SpreadGoalKeepersInDifferentTeams(List<Team> teams, List<IPlayer> players)
        {
            var goalKeepers = players.Cast<IGoalKeeperSupport>().Where(p => p.IsGoalKeeper);

            var playersToRemove = new List<IGoalKeeperSupport>();
            var teamIndices = 0;
            foreach (var gk in goalKeepers)
            {
                if (teamIndices + 1 > teams.Count) break;
                playersToRemove.Add(gk);
                teams[teamIndices++].AddPlayer((IPlayer)gk);
            }

            players.RemoveAll((p) => playersToRemove.Select(t=>t.Key).Contains(p.Key) );
            return players;
        }
    }

}
