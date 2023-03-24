using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using TeamsGenerator.Algo;

namespace TeamsGenerator.CLI
{
    internal static class Printer
    {
        public static void Print(List<Team> teams, Dictionary<string, IPrinterOptionCallback> callbackMapper, bool isColorFeatureOn = false)
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
                Console.WriteLine("(1) Press 1 for Copy and Exit\n" +
                              "(2) Press 2 for re-shuffle\n" +
                              "(3) Press 3 for Exit");
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

        private static ConsoleColor GetConsoleColor(ShirtColor color)
        {
            var colorResult = (ConsoleColor)Enum.Parse(typeof(ConsoleColor), color.ToString());
            return colorResult;
        }
    }
}
