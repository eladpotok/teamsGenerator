using System;
using System.Collections.Generic;
using System.Linq;
using TeamsGenerator.Algos;
using TeamsGenerator.API;

namespace TeamsGenerator.CLI
{
    internal static class Printer
    {
        public static void Print(List<CliDisplayTeam> teams, Dictionary<string, IPrinterOptionCallback> callbackMapper, bool isColorFeatureOn = false)
        {
            var count = 1;
            foreach (var team in teams)
            {
                var teamColor = team.Color;

                if (isColorFeatureOn)
                {
                    Console.ForegroundColor = GetConsoleColor(teamColor);
                    if (Console.ForegroundColor == ConsoleColor.Black) Console.BackgroundColor = ConsoleColor.White;
                    else Console.BackgroundColor = ConsoleColor.Black;
                }

                Console.WriteLine("{0,-20}", $"Team: {count} | Color: {team.Color} ({team.GetAvarage()})");
                foreach (var player in team.Players)
                {
                    Console.WriteLine("{0,-20} {1,5:N1}", player.Name, player.Rank);
                }
                if (isColorFeatureOn) Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine("--------------------------------");
                count++;
            }


            var input = "";
            var optionalKeys = callbackMapper.Keys;
            var commandSelected = false;
            while (!commandSelected && !optionalKeys.Contains(input))
            {
                PrintOptions(callbackMapper);
                input = Console.ReadLine().ToLower();

                if (callbackMapper.Keys.Contains(input))
                {
                    commandSelected = true;
                    Console.Clear();
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Wrong Answer! Try again");
                    Console.ForegroundColor = ConsoleColor.White;
                }

            }

            callbackMapper[input].DoCommand();
        }

        private static void PrintOptions(Dictionary<string, IPrinterOptionCallback> callbackMapper)
        {
            var index = 1;
            foreach (var command in callbackMapper)
            {
                Console.WriteLine($"({index}) Press {index} for {command.Value.CommandDescription}");
                index++;
            }
        }

        private static ConsoleColor GetConsoleColor(string color)
        {
            var colorResult = (ConsoleColor)Enum.Parse(typeof(ConsoleColor), color);
            return colorResult;
        }
    }
}
