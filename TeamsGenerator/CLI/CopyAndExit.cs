using System.Collections.Generic;
using System.Windows;
using TeamsGenerator.Algo;
using TeamsGenerator.Utilities;

namespace TeamsGenerator.CLI
{
    public class CopyAndExit : IPrinterOptionCallback
    {
        private readonly List<Team> _teams;
        private readonly bool _showPlayerStats;

        public CopyAndExit(List<Team> teams, bool showPlayerStats)
        {
            _teams = teams;
            _showPlayerStats = showPlayerStats;
        }


        public void DoCommand()
        {
            Clipboard.SetText(Helper.CopyToClipboard(_teams, _showPlayerStats));
        }
    }
}
