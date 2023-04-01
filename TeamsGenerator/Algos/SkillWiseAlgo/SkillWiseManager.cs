using System;
using System.Collections.Generic;
using System.Linq;
using TeamsGenerator.Orchestration;
using TeamsGenerator.Orchestration.Contracts;

namespace TeamsGenerator.Algos.SkillWiseAlgo
{
    public class SkillWiseManager : IAlgoManager
    {
        public SkillWiseManager()
        {
        
        }

        public List<Team> GenerateTeams(List<IPlayer> players)
        {
            var algo = new SkillWise(players.Cast<SkillWisePlayer>().ToList());
            return algo.GetTeams(3);
        }

    }
}
