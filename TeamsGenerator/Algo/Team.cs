using System.Collections.Generic;
using TeamsGenerator.Algo.Contracts;

namespace TeamsGenerator.Algo
{
    public class Team
    {
        public ShirtColor Color { get; set; }
        public List<IPlayer> Players { get; set; }

        public double TotalRank { get; set; }

        public Team(ShirtColor color)
        {
            Color = color;
            Players = new List<IPlayer>();
        }

        internal void AddPlayer(IPlayer player)
        {
            Players.Add(player);
            TotalRank += player.Rank;
        }

        public double GetAvarage()
        {
            return TotalRank / Players.Count;
        }
    }
}
