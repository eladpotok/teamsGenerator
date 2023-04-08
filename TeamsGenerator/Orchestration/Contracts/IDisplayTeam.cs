using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TeamsGenerator.Orchestration.Contracts
{
    internal interface IDisplayTeam
    {
        string TeamSymbol { get; set; }
        string Color { get; set; }
        double GetAvarage();
        List<IPlayer> Players { get; set; }

    }
}
