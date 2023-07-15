using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TeamsGenerator.API;
using TeamsGenerator.DataReaders;

namespace TeamsGenerator.Orchestration.Configuration
{
    public static class ConfigurationManager
    {
        public static string Version;
        public static int NumberOfTeams;
        public static List<PlayerShirt> ShirtsColorNameToSymbolMapper;

        public static void Init()
        {
            var config = ReadConfig();

            ShirtsColorNameToSymbolMapper = config.ColorNameToSymbol;
            NumberOfTeams = config.TeamsCount;
            Version = config.Version;
        }

        private static Config ReadConfig()
        {
            return new Config()
            {
                ColorNameToSymbol = new List<PlayerShirt>()
                {
                    new PlayerShirt() { ColorName = "Red", Symbol = "🟥", IsMarked = true },
                    new PlayerShirt() { ColorName = "Green", Symbol = "🟩", IsMarked = true },
                    new PlayerShirt() { ColorName = "Yellow", Symbol = "🟨", IsMarked = true },
                    new PlayerShirt() { ColorName = "White", Symbol = "⬜" },
                    new PlayerShirt() { ColorName = "Black", Symbol = "⬛" },
                    new PlayerShirt() { ColorName = "Blue", Symbol = "🟦" },
                    new PlayerShirt() { ColorName = "Orange", Symbol = "🟧" },
                    new PlayerShirt() { ColorName = "Purple", Symbol = "🟪" },
                },
                TeamsCount = 3,
                Version = "1.0"
            };
        }
    }
}
