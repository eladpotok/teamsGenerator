﻿using System;

namespace TeamsGenerator.CLI
{
    public class Reshuffle : IPrinterOptionCallback
    {

        private readonly Action _command;


        public Reshuffle(Action command)
        {
            _command = command;
        }

        public string CommandDescription => "re-shuffle";

        public void DoCommand()
        {
            _command?.Invoke();
        }
    }
}
