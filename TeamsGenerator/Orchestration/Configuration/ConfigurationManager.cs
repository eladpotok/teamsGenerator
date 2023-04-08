using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TeamsGenerator.DataReaders;

namespace TeamsGenerator.Orchestration.Configuration
{
    public static  class ConfigurationManager
    {
        public static int NumberOfTeams;
        public static Dictionary<string, string> ShirtsColorNameToSymbolMapper;

        public static void Init()
        {
            var config = ReadConfig();

            ShirtsColorNameToSymbolMapper = config.ColorNameToSymbol;
            NumberOfTeams = config.TeamsCount;
        }

        private static Config ReadConfig()
        {
            var configFilePath = $@"{Environment.CurrentDirectory}\config.json";
            var reader = new JsonReader<Config>(configFilePath);
            var config = reader.Read();
            return config;
        }
    }



}
