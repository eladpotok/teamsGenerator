using System.Collections.Generic;
using TeamsGenerator.Orchestration.Contracts;

namespace TeamsGenerator.Orchestration
{
    public class Config 
    {
        public List<ShirtColor> ShirtColors { get; set; }
        public int TeamsCount { get; set; }
        public string PrintForPlatform { get; set; }
    }

}
