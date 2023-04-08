using System.Collections.Generic;
using TeamsGenerator.Orchestration;

namespace TeamsGenerator.API
{
    public class WebAppConfigResponse
    {
        public int NumberOfTeams { get; set; }
        public List<string> ShirtsColors { get; set; }
    }
}
