using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TeamsGenerator.Orchestration.Contracts;

namespace TeamsGenerator.Algos.BackAndForthAlgo
{
    public class BackAndForthPlayer : IPlayer
    {
        public string Name { get; set; }
        public double Rank { get; set; }
    }
}
