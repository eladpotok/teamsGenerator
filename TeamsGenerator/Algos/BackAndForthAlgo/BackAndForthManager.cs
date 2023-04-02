using System.Collections.Generic;
using TeamsGenerator.Orchestration;
using TeamsGenerator.Orchestration.Contracts;
using TeamsGenerator.Utilities;

namespace TeamsGenerator.Algos.BackAndForthAlgo
{
    public class BackAndForthManager : AlgoManagerBase, IAlgoManager
    {
        private List<IPlayer> _orderedPlayers;

        public BackAndForthManager(Config config) : base(config)
        {
        }

        public List<Team> GenerateTeams(List<IPlayer> players)
        {
            _orderedPlayers = Helper.SortPlayersByRank(players);

            var playersCount = _orderedPlayers.Count;

            var teams = new List<Team>();
            var teamsCount = _config.TeamsCount;

            // put the leader in each team
            for (int i = 0; i < teamsCount; i++)
            {
                teams.Add(new Team());
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
    }
}
