using System;
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
            players = players?
                .Where(player => player != null)
                .ToList()
                ?? new List<IPlayer>();

            if (teamsCount <= 0)
            {
                return teams;
            }

            if (generatedTeamWithLockedPlayers != null)
            {
                for (int i = 0; i < _config.TeamsCount; i++)
                {
                    teams.Add(generatedTeamWithLockedPlayers.FirstOrDefault(t => t.Index == i) ?? new Team(i));

                }

                foreach (var team in generatedTeamWithLockedPlayers)
                {
                    foreach (var player in team.Players
                        .OfType<BackAndForthPlayer>())
                    {
                        RemoveMatchingPlayer(players, player);
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
                    while (team.Players.Count < maxPlayersInTeam
                        && _orderedPlayers.Any())
                    {
                        var optionalRank = new List<int>() { avgRankOfTeamWithMaxPlayers, avgRankOfTeamWithMaxPlayers + 1, avgRankOfTeamWithMaxPlayers - 1 };
                        var player = _orderedPlayers.Where(t => optionalRank.Contains((int)t.Rank)).FirstOrDefault();
                        
                        // if no player with this rank, so lets take the middle as default
                        if( player == null)
                        {
                            player = _orderedPlayers[_orderedPlayers.Count / 2];
                        }
                        team.AddPlayer(player);
                        _orderedPlayers.Remove(player);
                    }
                }

                if (!_orderedPlayers.Any())
                {
                    return teams;
                }
            }



            // put the leader in each team
            var leadersCount = Math.Min(teamsCount, _orderedPlayers.Count);
            for (int i = 0; i < leadersCount; i++)
            {
                teams[i].AddPlayer(_orderedPlayers[leadersCount - 1 - i]);
            }

            var playersCount = _orderedPlayers.Count;


            var resultTeams = GetTeams(
                teams,
                teamsCount,
                playersCount,
                leadersCount);
            return resultTeams;
        }

        private List<Team> GetTeams(
            List<Team> teams,
            int teamsCount,
            int playersCount,
            int firstUnassignedPlayer)
        {
            var toggle = false;
            var innerIndexForToggle = 0;

            teams = teams.OrderBy(t => t.TotalRank).ToList();
            for (int i = firstUnassignedPlayer; i < playersCount; i++)
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

        private static void RemoveMatchingPlayer(
            List<IPlayer> players,
            BackAndForthPlayer lockedPlayer)
        {
            var player = !string.IsNullOrWhiteSpace(lockedPlayer.Key)
                ? players.FirstOrDefault(item => item.Key == lockedPlayer.Key)
                : !string.IsNullOrWhiteSpace(lockedPlayer.Id)
                    ? players.FirstOrDefault(item => item.Id == lockedPlayer.Id)
                    : players.FirstOrDefault(item =>
                        ReferenceEquals(item, lockedPlayer)
                        || item is BackAndForthPlayer candidate
                        && candidate.Name == lockedPlayer.Name
                        && candidate.Rank == lockedPlayer.Rank
                        && candidate.IsGoalKeeper
                            == lockedPlayer.IsGoalKeeper);

            if (player != null)
            {
                players.Remove(player);
            }
        }
    }
}
