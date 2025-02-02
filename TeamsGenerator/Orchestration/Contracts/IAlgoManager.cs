using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TeamsGenerator.Algos;

namespace TeamsGenerator.Orchestration.Contracts
{
    public interface IAlgoManager
    {
        List<Team> GenerateTeams(List<IPlayer> players, List<Team> generatedTeamWithLockedPlayers);
    }
}
