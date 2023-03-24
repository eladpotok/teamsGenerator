using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TeamsGenerator.Algo;
using TeamsGenerator.Algo.Contracts;

namespace TeamsGenerator.SkillWiseAlgo
{
    public class SkillWiseManager : IAlgoManager
    {
        public ITeamsAlgo Algo { get; set; }
        public IPlayersReader PlayersReader { get; set; }

        public SkillWiseManager(string path)
        {
            PlayersReader = new PlayersReader(path);
        }

        public List<Team> GenerateTeams()
        {
            var players = PlayersReader.GetPlayers();
            Algo = new SkillWise(players.Cast<SkillWisePlayer>().ToList());
            return Algo.GetTeams(3);
        }
    }
}
