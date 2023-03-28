using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TeamsGenerator.DataReaders;
using TeamsGenerator.Orchestration.Contracts;

namespace TeamsGenerator.Algos.BackAndForthAlgo
{
    public class BackAndForthPlayersReader : IPlayersReader
    {
        private readonly string _path;

        public BackAndForthPlayersReader(string path)
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
