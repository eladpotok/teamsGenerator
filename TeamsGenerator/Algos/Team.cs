using System.Collections.Generic;
using TeamsGenerator.Orchestration.Contracts;

namespace TeamsGenerator.Algos
{
    public class Team : IAITeam
    {
        public List<IPlayer> Players { get; set; }

        public double TotalRank { get; set; }

        public int Index { get; set; }
        public IEnumerable<string> Weakness { get; set; }
        public IEnumerable<string> Strength { get; set; }
        public string Description { get; set; }
        public string PlayStyle { get; set; }

        public Team()
        {
            Players = new List<IPlayer>();
        }

        public Team(int index) : this()
        {
            Index = index;
        }

        internal void AddPlayer(IPlayer player)
        {
            Players.Add(player);
            TotalRank += player.Rank;
        }

    }

    public interface IAITeam
    {
        IEnumerable<string> Weakness { get; set; }
        IEnumerable<string> Strength { get; set; }
        string Description { get; set; }
        string PlayStyle { get; set; }
    }
}
