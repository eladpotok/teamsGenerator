using System;

namespace TeamsGenerator.CLI
{
    public class Exit : IPrinterOptionCallback
    {
        public string CommandDescription => "Exit";

        public void DoCommand()
        {
            Environment.Exit(0);
        }
    }
}
