using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TeamsGenerator.Orchestration.Contracts;

namespace TeamsGenerator.CLI
{
    public class CliDisplayTeam  : IDisplayTeam
    {
        public List<IPlayer> Players { get; set; }
        public double Rank { get; set; }

        public string TeamSymbol { get; set; }
        public string Color { get; set; }
        public string TeamName { get; set; }


        public double GetAvarage()
        {
            return Rank / Players.Count;
        }
    }
}
