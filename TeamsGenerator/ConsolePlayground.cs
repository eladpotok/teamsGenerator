using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TeamsGenerator.Algos;
using TeamsGenerator.Algos.BackAndForthAlgo;
using TeamsGenerator.Algos.SkillWiseAlgo;
using TeamsGenerator.API;
using TeamsGenerator.CLI;
using TeamsGenerator.Orchestration;
using TeamsGenerator.Orchestration.Configuration;
using TeamsGenerator.Orchestration.Contracts;
using TeamsGenerator.Utilities;

namespace TeamsGenerator
{
    public class ConsolePlayground
    {
        private static Dictionary<AlgoType, Func<string, IPlayersReader>> _algoTypeToPlayersReaderMapper = new Dictionary<AlgoType, Func<string, IPlayersReader>>()
        {
            { AlgoType.BackAndForth, (path) => new BackAndForthPlayersReader(path) },
            { AlgoType.SkillWise, (path) => new SkillWisePlayersReader(path) },
        };

        public static void Start()
        {
            ConfigurationManager.Init();

            Console.WriteLine("Hello There! Please provide the algorithm number you want to use from the following list:");

            var allAlgos = GetListOfAlgo();
            PrintAlgoOptions(allAlgos);

            int algoTypeToUse = ReadAlgoInput(allAlgos);

            Generate((AlgoType)algoTypeToUse);
            Console.Clear();
        }

        private static void PrintAlgoOptions(string[] allAlgos)
        {
            var index = 1;
            foreach (var algo in allAlgos)
            {
                Console.WriteLine($"({index++}) {algo}");
            }
        }

        private static int ReadAlgoInput(string[] allAlgos)
        {
            var algoTypeToUse = -1;
            var algoToUseInput = Console.ReadLine();
            while (!ValidateAlgoInput(algoToUseInput, allAlgos.Length, out algoTypeToUse))
            {
                Console.WriteLine("Wrong answer! Please try again");
                algoToUseInput = Console.ReadLine();
            }

            return algoTypeToUse;
        }

        private static bool ValidateAlgoInput(string algoToUseInput, int rangeOfAnswers, out int result)
        {
            result = -1;
            if (!int.TryParse(algoToUseInput, out var algoToUse)) return false;
            if (algoToUse < 1 || algoToUse > rangeOfAnswers) return false;

            result = algoToUse;
            return true;
        }

        private static string[] GetListOfAlgo()
        {
            var listOfAlgo = (AlgoType[])Enum.GetValues(typeof(AlgoType));
            return listOfAlgo.Select(a => a.ToString()).ToArray();
        }

        public static void Generate(AlgoType algoType, bool isColorFeatureOn = false)
        {
            List<CliDisplayTeam> teamsToDisplay = GetTeams(algoType);

            var optionsCallback = new Dictionary<string, IPrinterOptionCallback>() {

                { "1", new CopyAndExit(teamsToDisplay, false) },
                { "2", new Reshuffle(Reshuffle) },
                { "3", new BackToAlgo(Back) },
                { "4", new Exit() }
            };

            Printer.Print(teamsToDisplay, optionsCallback);

            void Reshuffle()
            {
                Generate(algoType, isColorFeatureOn);
            }

            void Back()
            {
                Console.Clear();
                Start();
            }
        }

        public static List<CliDisplayTeam> GetTeams(AlgoType algoType)
        {
            var algoToUse = (int)algoType - 1;
            var algoTypeName = ((AlgoType[])Enum.GetValues(typeof(AlgoType)))[algoToUse];
            var playersFilePathWithoutExtension = $"{Environment.CurrentDirectory}\\algos\\{algoTypeName}Algo\\players";

            var algoCreator = _algoTypeToPlayersReaderMapper[algoTypeName];
            var playersReader = algoCreator.Invoke(playersFilePathWithoutExtension);
            var players = playersReader.GetPlayers();

            var config = new AlgoConfig() { TeamsCount = ConfigurationManager.NumberOfTeams };
            var teams = AlgoRunner.Run((AlgoType)algoToUse, players, config);
            var teamsToDisplay = GetCliDisplayTeam(ConfigurationManager.ShirtsColorNameToSymbolMapper.Keys.ToList(), teams);
            return teamsToDisplay;
        }

        private static List<CliDisplayTeam> GetCliDisplayTeam(List<string> shirtsColorNames, List<Algos.Team> teams)
        {
            var results = new List<CliDisplayTeam>();
            var shirtsColors = Helper.Shuffle(shirtsColorNames);

            var index = 1;
            foreach (var team in teams)
            {
                var shirtColor = shirtsColors[0];
                results.Add(new CliDisplayTeam() { Players = team.Players, Rank = team.TotalRank, Color = shirtColor, TeamSymbol = ConfigurationManager.ShirtsColorNameToSymbolMapper[shirtColor], TeamName = index.ToString() });
                index++;
                shirtsColors.RemoveAt(0);
            }

            return results;
        }

    }
}
