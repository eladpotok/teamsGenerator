﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TeamsGenerator.DataReaders;
using TeamsGenerator.Orchestration.Contracts;

namespace TeamsGenerator.Algos.SkillWiseAlgo
{
    public class SkillWisePlayersReader : IPlayersReader
    {
        private readonly string _path;

        public SkillWisePlayersReader(string path)
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
