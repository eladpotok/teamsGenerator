using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TeamsGenerator.Algo.Contracts;
using TeamsGenerator.DataReaders;

namespace TeamsGenerator.SkillWiseAlgo
{
    public class PlayersReader : IPlayersReader
    {
        private readonly string _path;

        public PlayersReader(string path)
        {
            _path = path;
        }

        public List<IPlayer> GetPlayers()
        {
            var reader = new JsonReader<SkillWisePlayer[]>(_path);
            var players = reader.Read();
            foreach (var player in players)
            {
                var totalRank = player.Stamina + player.Leadership + player.Passing + player.Defence + player.Attack;
                var avg = totalRank / 5.0;
                player.Rank = avg;
            }

            return players.Cast<IPlayer>().ToList();
        }
    }
}
