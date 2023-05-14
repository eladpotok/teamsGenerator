using System.Collections.Generic;
using TeamsGenerator.Orchestration;

namespace TeamsGenerator.API
{
    public class WebAppConfigResponse
    {
        public int NumberOfTeams { get; set; }
        public Dictionary<string, string> ShirtsColors { get; set; }
    }
}
