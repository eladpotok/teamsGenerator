using System.Collections.Generic;
using TeamsGenerator.Orchestration;
using TeamsGenerator.Orchestration.Contracts;

namespace TeamsGenerator.Algos.BackAndForthAlgo
{
    public class BackAndForthManager : IAlgoManager
    {
        public ITeamsAlgo Algo { get; set; }
        public IPlayersReader PlayersReader { get; set; }

        public BackAndForthManager(IPlayersReader playerReader)
        {
            PlayersReader = playerReader;
        }

        public List<Team> GenerateTeams()
        {
            var players = PlayersReader.GetPlayers();
            Algo = new BackAndForth(players);
            return Algo.GetTeams(3);
        }
    }
}
