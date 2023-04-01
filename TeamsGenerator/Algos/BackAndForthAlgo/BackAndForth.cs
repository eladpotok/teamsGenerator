using System.Collections.Generic;
using System.Linq;
using TeamsGenerator.Orchestration;
using TeamsGenerator.Orchestration.Contracts;
using TeamsGenerator.Utilities;

namespace TeamsGenerator.Algos.BackAndForthAlgo
{

    /// <summary>
    /// This algorithem works like this:
    /// Set randomly an order of the teams. The first team pick a player, the second team pick the next player, the third team pick two players.
    /// After this round, it goes back. After the third team pick two players, then the second team pick a player, and then the first team pick two players. and so on.
    /// The team picks a player according to the rank order.
    /// </summary>

    internal class BackAndForth
    {

        private List<IPlayer> _orderedPlayers;

        public BackAndForth(List<IPlayer> players)
        {
            _orderedPlayers = players;
        }

        public List<Team> GetTeams(int teamsCount)
        {
            _orderedPlayers = Helper.SortPlayersByRank(_orderedPlayers);
            var playersCount = _orderedPlayers.Count;

            var teams = new List<Team>();
            var colors = Helper.Shuffle(ShirtColorManager.GetAllShirtColors());

            // put the leader in each team
            for (int i = 0; i < teamsCount; i++)
            {
                teams.Add(new Team(colors[i]));
                teams[i].AddPlayer(_orderedPlayers[teamsCount - 1 - i]);
            }

            var resultTeams = GetTeams(teams, teamsCount, playersCount);
            return resultTeams;
        }

        private List<Team> GetTeams(List<Team> teams, int teamsCount, int playersCount)
        {
            var toggle = false;
            var innerIndexForToggle = 0;

            teams = Helper.Shuffle(teams);
            for (int i = teamsCount; i < playersCount; i++)
            {
                var teamNumber = toggle ? teamsCount - i % teamsCount - 1 : i % teamsCount;
                teams[teamNumber].AddPlayer(_orderedPlayers[i]);

                innerIndexForToggle++;
                // final round
                if (innerIndexForToggle == teamsCount)
                {
                    toggle = !toggle;
                    innerIndexForToggle = 0;
                }

            }

            return teams;
        }

        private static void SetPlayerWithColor(List<Team> teams, string name, ShirtColor color)
        {
            var isPlayerParticipated = teams.Any(t => t.Players.Any(p => p.Name == name));
            if (!isPlayerParticipated) return;
            var teamsContainsThisColor = teams.Any(t => t.Color == color);
            if (!teamsContainsThisColor)
            {
                var teamOfCurrentPlayer = teams.First(t => t.Players.Any(p => p.Name == name));
                teamOfCurrentPlayer.Color = color;
            }
            else
            {
                var teamWithTheGivenColor = teams.First(t => t.Color == color);
                var isPlayerWithTheGivenColor = teamWithTheGivenColor.Players.Any(p => p.Name == name);
                if (isPlayerWithTheGivenColor) return;

                var teamOfGivenPlayer = teams.First(t => t.Players.Any(p => p.Name == name));
                SwapColors(teamWithTheGivenColor, teamOfGivenPlayer);
            }
        }

        private static void SwapColors(Team team1, Team team2)
        {
            var otherTeamColor = team2.Color;
            team2.Color = team1.Color;
            team1.Color = otherTeamColor;
        }
    }
}
