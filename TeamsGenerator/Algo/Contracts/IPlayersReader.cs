﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TeamsGenerator.Algo.Contracts
{
    public interface IPlayersReader
    {
        List<IPlayer> GetPlayers();
    }
}