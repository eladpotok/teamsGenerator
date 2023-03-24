using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TeamsGenerator.Algo;
using TeamsGenerator.Algo.Contracts;

namespace TeamsGenerator.BackAndForthAlgo
{
    public class BackAndForthManager : IAlgoManager
    {
        public ITeamsAlgo Algo { get; set; }
        public IPlayersReader PlayersReader { get; set; }

        public BackAndForthManager(string path)
        {
            PlayersReader = new PlayersReader(path);
        }

        public List<Team> GenerateTeams()
        {
            var players = PlayersReader.GetPlayers();
            Algo = new BackAndForth(players);
            return Algo.GetTeams(3);
        }
    }
}
