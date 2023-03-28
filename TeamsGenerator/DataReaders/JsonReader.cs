using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TeamsGenerator.DataReaders
{
    internal class JsonReader<T> : IDataReader<T>
    {
        private readonly string _jsonFileName;

        public JsonReader(string jsonFileName)
        {
            _jsonFileName = jsonFileName;
        }

        public T Read()
        {
            var json = File.ReadAllText($"{_jsonFileName}.json");
            var players = JsonConvert.DeserializeObject<T>(json);
            return players;
        }
    }
}
