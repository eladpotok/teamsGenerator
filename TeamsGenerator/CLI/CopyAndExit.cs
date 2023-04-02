using System.Collections.Generic;
using System.Windows;
using TeamsGenerator.Orchestration;
using TeamsGenerator.Utilities;

namespace TeamsGenerator.CLI
{
    public class CopyAndExit : IPrinterOptionCallback
    {
        private readonly List<DisplayTeam> _teams;
        private readonly bool _showPlayerStats;

        public CopyAndExit(List<DisplayTeam> teams, bool showPlayerStats)
        {
            _teams = teams;
            _showPlayerStats = showPlayerStats;
        }

        public string CommandDescription => "Copy and Exit";

        public void DoCommand()
        {
            Clipboard.SetText(Helper.CopyResultsToClipboard(_teams, _showPlayerStats));
        }
    }
}
