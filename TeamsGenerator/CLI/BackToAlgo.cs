using System;

namespace TeamsGenerator.CLI
{
    public class BackToAlgo : IPrinterOptionCallback
    {
        private readonly Action _command;

        public BackToAlgo(Action command)
        {
            _command = command;
        }

        public string CommandDescription => "Going Back to Algorithms";

        public void DoCommand()
        {
            _command?.Invoke();
        }
    }
}
