using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TeamsGenerator.Orchestration.Contracts
{
    public interface ITeamsAlgo
    {
        List<Team> GetTeams(int teamsCount = 3);
    }
}
