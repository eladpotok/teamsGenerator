using System;

namespace TeamsGenerator.CLI
{
    public class Exit : IPrinterOptionCallback
    {
        public void DoCommand()
        {
            Environment.Exit(0);
        }
    }
}
