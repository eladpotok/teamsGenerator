using System.Collections.Generic;
using TeamsGenerator.Orchestration;
using TeamsGenerator.Orchestration.Contracts;

namespace TeamsGenerator.Algos.BackAndForthAlgo
{
    public class BackAndForthManager : IAlgoManager
    {
        public BackAndForthManager()
        {
        }

        public List<Team> GenerateTeams(List<IPlayer> players)
        {
            var algo = new BackAndForth(players);
            return algo.GetTeams(3);
        }
    }
}
