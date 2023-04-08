using System.Collections.Generic;
using TeamsGenerator.Orchestration;
using TeamsGenerator.Orchestration.Contracts;

namespace TeamsGenerator.API
{
    public class InitialAppConfig
    {
        public WebAppConfigResponse Config { get; set; }
        public List<WebAppAlgoInfo> Algos { get; set; }
    }
}
