using System;
using System.Collections.Generic;
using System.Linq;
using TeamsGenerator.CLI;
using TeamsGenerator.Orchestration;

namespace TeamsGenerator
{
    internal class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            PrintMainMenu();
        }

        private static void PrintMainMenu()
        {
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
            result  = - 1;
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

        private static void Generate(AlgoType algoType, bool isColorFeatureOn = false)
        {
            var teams = AlgoRunner.Run(algoType);

            var optionsCallback = new Dictionary<string, IPrinterOptionCallback>() {

                { "1", new CopyAndExit(teams, false) },
                { "2", new Reshuffle(Reshuffle) },
                { "3", new BackToAlgo(Back) },
                { "4", new Exit() }
            };

            Printer.Print(teams, optionsCallback);

            void Reshuffle()
            {
                Generate(algoType, isColorFeatureOn);
            }

            void Back() 
            {
                Console.Clear();
                PrintMainMenu();
            }
        }
    }
}
