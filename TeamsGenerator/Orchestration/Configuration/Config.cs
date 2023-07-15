using System.Collections.Generic;
using TeamsGenerator.API;
using TeamsGenerator.Orchestration.Contracts;

namespace TeamsGenerator.Orchestration
{
    public class Config 
    {
        public List<PlayerShirt> ColorNameToSymbol { get; set; }
        public int TeamsCount { get; set; }
        public string PrintForPlatform { get; set; }

        public string Version { get; set; }
        public Config()
        {
            ColorNameToSymbol = new List<PlayerShirt>();
            TeamsCount = 3;
        }
    }

}
