using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TeamsGenerator.API
{
    public class GetTeamsResponse
    {
        public List<WebAppTeam> Teams { get; set; }
        public string TeamsResultAsCopyText { get; set; }
    }
}
