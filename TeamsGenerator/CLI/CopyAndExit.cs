using System.Collections.Generic;
using System.Linq;
using System.Windows;
using TeamsGenerator.Algos;
using TeamsGenerator.API;
using TeamsGenerator.Orchestration.Contracts;
using TeamsGenerator.Utilities;

namespace TeamsGenerator.CLI
{
    public class CopyAndExit : IPrinterOptionCallback
    {
        private readonly List<CliDisplayTeam> _teams;
        private readonly bool _showPlayerStats;

        public CopyAndExit(List<CliDisplayTeam> teams, bool showPlayerStats)
        {
            _teams = teams;
            _showPlayerStats = showPlayerStats;
        }

        public string CommandDescription => "Copy and Exit";

        public void DoCommand()
        {
            Clipboard.SetText(Helper.GetResultsAsText(_teams.Cast<IDisplayTeam>().ToList(), _showPlayerStats));
        }
    }
}
