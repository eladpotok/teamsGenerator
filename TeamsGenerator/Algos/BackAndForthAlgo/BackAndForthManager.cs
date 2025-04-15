using System.Collections.Generic;
using System.Linq;
using TeamsGenerator.Orchestration;
using TeamsGenerator.Orchestration.Contracts;
using TeamsGenerator.Utilities;

namespace TeamsGenerator.Algos.BackAndForthAlgo
{
    public class BackAndForthManager : AlgoManagerBase, IAlgoManager
    {
        private List<IPlayer> _orderedPlayers;

        public BackAndForthManager(AlgoConfig config) : base(config)
        {
        }

        public List<Team> GenerateTeams(List<IPlayer> players, List<Team> generatedTeamWithLockedPlayers)
        {
            var teams = new List<Team>();
            var teamsCount = _config.TeamsCount;


            if (generatedTeamWithLockedPlayers != null)
            {
                for (int i = 0; i < _config.TeamsCount; i++)
                {
                    teams.Add(generatedTeamWithLockedPlayers.FirstOrDefault(t => t.Index == i) ?? new Team(i));

                }

                foreach (var team in generatedTeamWithLockedPlayers)
                {
                    foreach (var player in team.Players.Cast<BackAndForthPlayer>())
                    {
                        var playerRef = players.FirstOrDefault(t => t.Key == player.Key);
                        players.Remove(playerRef);
                    }
                }

                teams = teams.OrderBy(t=>t.Players.Count).ThenBy(t => t.TotalRank).ToList();
            }
            else
            {
                for (int i = 0; i < _config.TeamsCount; i++)
                {
                    teams.Add(new Team(i));
                }
            }

            players = Helper.SpreadGoalKeepersInDifferentTeams(teams, players);
            _orderedPlayers = Helper.SortPlayersByRank(players);


            // padding teams if locked players required
            if (!teams.All(t=> t.Players.Count == teams[0].Players.Count))
            {
                var maxPlayersInTeam = teams.Max(t => t.Players.Count);
                int avgRankOfTeamWithMaxPlayers = (int)(teams.Where(t => t.Players.Count == maxPlayersInTeam).First().TotalRank) / maxPlayersInTeam;

                for (int i = 0; i < teamsCount; i++)
                {
                    var team = teams[i];
                    while (team.Players.Count < maxPlayersInTeam)
                    {
                        var optionalRank = new List<int>() { avgRankOfTeamWithMaxPlayers, avgRankOfTeamWithMaxPlayers + 1, avgRankOfTeamWithMaxPlayers - 1 };
                        var player = _orderedPlayers.Where(t => optionalRank.Contains((int)t.Rank)).FirstOrDefault();
                        
                        // if no player with this rank, so lets take the middle as default
                        if( player == null)
                        {
                            player = _orderedPlayers[_orderedPlayers.Count / 2];
                        }

                        team.AddPlayer(player);
                        var playerRef = _orderedPlayers.FirstOrDefault(t => t.Key == player.Key);
                        _orderedPlayers.Remove(playerRef);
                    }
                }

                if (!_orderedPlayers.Any())
                {
                    return teams;
                }
            }



            // put the leader in each team
            for (int i = 0; i < teamsCount; i++)
            {
                teams[i].AddPlayer(_orderedPlayers[teamsCount - 1 - i]);
            }

            var playersCount = _orderedPlayers.Count;


            var resultTeams = GetTeams(teams, teamsCount, playersCount);
            return resultTeams;
        }

        private List<Team> GetTeams(List<Team> teams, int teamsCount, int playersCount)
        {
            var toggle = false;
            var innerIndexForToggle = 0;

            teams = teams.OrderBy(t => t.TotalRank).ToList();
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
    }
}
