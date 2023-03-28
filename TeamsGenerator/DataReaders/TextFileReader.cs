using System;
using System.Collections.Generic;
using System.IO;
using TeamsGenerator.Algos.SkillWiseAlgo;

namespace TeamsGenerator.DataReaders
{
    class TextFileReader : IDataReader<IEnumerable<SkillWisePlayer>>
    {
        private readonly string _filePath;

        public TextFileReader(string filePath)
        {
            _filePath = filePath;
        }

        public IEnumerable<SkillWisePlayer> Read()
        {
            try
            {
                var result = new List<SkillWisePlayer>();
                var fileContentByLines = File.ReadLines(_filePath);

                foreach (var line in fileContentByLines)
                {
                    var splittedLine = line.Split(',');

                    var name = splittedLine[0];
                    var rank = splittedLine[1];

                    var player = new SkillWisePlayer() { Name = name, Rank = float.Parse(rank) };
                    result.Add(player);
                }

                return result;

            }
            catch (Exception)
            {
                return null;
            }
        }

    }
}
