using System.Collections.Generic;
using TeamsGenerator.Orchestration.Contracts;

namespace TeamsGenerator.Orchestration
{
    public class Config 
    {
        public Dictionary<string, string> ColorNameToSymbol { get; set; }
        public int TeamsCount { get; set; }
        public string PrintForPlatform { get; set; }
        
        public Config()
        {
            TeamsCount = 3;
        }
    }

}
