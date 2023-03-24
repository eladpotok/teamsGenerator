using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TeamsGenerator.Algo.Contracts;
using TeamsGenerator.DataReaders;

namespace TeamsGenerator.BackAndForthAlgo
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
            var reader = new JsonReader<BackAndForthPlayer[]>(_path);
            var players = reader.Read();
            return players.Cast<IPlayer>().ToList();
        }
    }
}
