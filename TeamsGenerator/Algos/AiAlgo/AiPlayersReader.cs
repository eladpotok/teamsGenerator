using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TeamsGenerator.DataReaders;
using TeamsGenerator.Orchestration.Contracts;

namespace TeamsGenerator.Algos.AiAlgo
{
    public class AiPlayersReader : IPlayersReader
    {
        private readonly string _path;

        public AiPlayersReader(string path)
        {
            _path = path;
        }

        public List<IPlayer> GetPlayers()
        {
            var reader = new JsonReader<AiPlayer[]>(_path);
            var players = reader.Read();


            return players.Cast<IPlayer>().ToList();
        }
    }
}
