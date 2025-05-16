using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TeamsGenerator.Utilities
{
    public static class TableCalculator
    {
        public static Dictionary<string, Score> Create(dynamic stats) 
        {
            var teamsScores = new Dictionary<string, Score>();

            // backward compatability

            var matches = stats;
            //if (stats.matches != null)
            //{
            //    matches = stats.matches;
            //}

            foreach (var match in matches)
            {
                var teamA = match.teamA;
                var teamB = match.teamB;

                HandleScore(teamsScores, teamA, teamB);
                HandleScore(teamsScores, teamB, teamA);
            }

            return teamsScores.OrderByDescending(t => t.Value.Points).ThenByDescending(t => t.Value.Gf - t.Value.Ga).ThenByDescending(t => t.Value.Gf).ToDictionary(t => t.Key, t => t.Value);
        }

        private static void HandleScore(Dictionary<string, Score> teamsScores, dynamic teamA, dynamic teamB)
        {
            var color = teamA.color.ToString();
            if (!teamsScores.TryGetValue(color, out Score score))
            {
                teamsScores.Add(color, new Score(int.Parse(teamA.score.ToString()), int.Parse(teamB.score.ToString())));
            }
            else
            {
                score.AddScore(int.Parse(teamA.score.ToString()), int.Parse(teamB.score.ToString()));
            }
        }

    }

    public class Score
    {
        public int W { get; set; }
        public int D { get; set; }
        public int L { get; set; }
        public int GP => W + D + L;

        public int Ga { get; set; }
        public int Gf { get; set; }

        public int Points => W * 3 + D;

        public Score()
        {

        }

        public Score(int myScore, int opponentScore)
        {
            AddScore(myScore, opponentScore);
        }

        internal void AddScore(int myScore, int opponentScore)
        {
            if (myScore == opponentScore)
            {
                D++;
            }
            else if (myScore < opponentScore)
            {
                L++;
            }
            else
            {
                W++;
            }

            Ga += opponentScore;
            Gf += myScore;

        }
    }

}
