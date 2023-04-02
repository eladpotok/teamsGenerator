using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TeamsGenerator.Orchestration;
using TeamsGenerator.Orchestration.Contracts;

namespace TeamsGenerator.Algos
{
    public abstract class AlgoManagerBase 
    {
        protected readonly Config _config;

        public AlgoManagerBase(Config config)
        {
            _config = config;
        }
    }
}
