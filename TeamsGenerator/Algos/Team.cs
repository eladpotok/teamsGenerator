using System.Collections.Generic;
using TeamsGenerator.Orchestration.Contracts;

namespace TeamsGenerator.Algos
{
    public class Team
    {
        public List<IPlayer> Players { get; set; }

        public double TotalRank { get; set; }

        public Team()
        {
            Players = new List<IPlayer>();
        }

        internal void AddPlayer(IPlayer player)
        {
            Players.Add(player);
            TotalRank += player.Rank;
        }

    }
}
