using System.Collections.Generic;
using TeamsGenerator.Orchestration;
using TeamsGenerator.Orchestration.Contracts;

namespace TeamsGenerator.API
{
    public class GetAppSetupResponse
    {
        public UserConfigResponse Config { get; set; }
        public List<WebAppAlgoInfo> Algos { get; set; }
        public string ServerVersion => "1.0.0";
        public string ClientVersion => "8.0.0";
    }
}
