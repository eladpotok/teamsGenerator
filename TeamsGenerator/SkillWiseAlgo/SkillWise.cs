using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TeamsGenerator.Algo;
using TeamsGenerator.Algo.Contracts;
using TeamsGenerator.Utilities;

namespace TeamsGenerator.SkillWiseAlgo
{

    // Remark:
    // This algo generate teams in this method:
    // First we pick, randomly, one of the skills - Leadership, Attack, Defence, Stamina, Passing.
    // Then we pick 3 players, by order from the top, according to the skill, and locate the player in the specific team.
    // The order of picking a player is: 
    // 1. Take the player with the highest rank of the current skill.
    // 2. If there are two players with the same skill rank, we pick the one with the higher avarage rank.
    // 3. If we still got more than one player, we pick randomly.
    // after than, we pick the next skill. But in advanced, we order the team by the team avarage rank so now the
    // lowest avarage team rank is the first to pick a player.

    internal class OrderBy
    {
        public string Name { get; set; }
        public Func<SkillWisePlayer, double> Invoker { get; set; }
    }

    internal class SkillWise : ITeamsAlgo
    {
        private List<SkillWisePlayer> _players;

        public SkillWise(List<SkillWisePlayer> players)
        {
            _players = new List<SkillWisePlayer>(players);
        }

        public List<Team> GetTeams(int teamsCount = 3)
        {
            var teams = new List<Team>();
            teams.Add(new Team(ShirtColor.Orange));
            teams.Add(new Team(ShirtColor.Black));
            teams.Add(new Team(ShirtColor.White));

            var allTypesOfSkills = new List<OrderBy>()
            {
                new OrderBy() { Name="Leadership", Invoker = t => t.Leadership },
                new OrderBy() { Name="Attack", Invoker = t => t.Attack },
                new OrderBy() { Name="Defence", Invoker = t => t.Defence },
                new OrderBy() { Name="Stamina", Invoker = t => t.Stamina },
                new OrderBy() { Name="Passing", Invoker = t => t.Passing },
            };

            allTypesOfSkills = Helper.Shuffle(allTypesOfSkills);
            var players = new List<SkillWisePlayer>(_players);

            AddSkillPlayerToTeam(ref teams, players, TakeRandomSkill(allTypesOfSkills));
            AddSkillPlayerToTeam(ref teams, players, TakeRandomSkill(allTypesOfSkills));
            AddSkillPlayerToTeam(ref teams, players, TakeRandomSkill(allTypesOfSkills));
            AddSkillPlayerToTeam(ref teams, players, TakeRandomSkill(allTypesOfSkills));
            AddSkillPlayerToTeam(ref teams, players, TakeRandomSkill(allTypesOfSkills));

            return teams;
        }

        private Func<SkillWisePlayer, double> TakeRandomSkill(List<OrderBy> skills)
        {
            var random = new Random();
            var index = random.Next(0, skills.Count);
            var skill = skills[index];
            Log($"Using skill {skill.Name}");
            skills.RemoveAt(index);
            return skill.Invoker;
        }

        private void AddSkillPlayerToTeam(ref List<Team> teams, List<SkillWisePlayer> playersLeft, Func<SkillWisePlayer, double> orderBy)
        {
            var orderedPlayers = OrderByAndShuffleSequence(playersLeft, orderBy);
            teams[0].AddPlayer(TakePlayer(orderedPlayers.Count - 1, orderedPlayers, playersLeft));
            teams[1].AddPlayer(TakePlayer(orderedPlayers.Count - 1, orderedPlayers, playersLeft));
            teams[2].AddPlayer(TakePlayer(orderedPlayers.Count - 1, orderedPlayers, playersLeft));
            teams = teams.OrderBy(t => t.TotalRank).ToList();
        }

        private SkillWisePlayer TakePlayer(int playerIndex, List<SkillWisePlayer> orderedPlayers, List<SkillWisePlayer> originalPlayers)
        {
            var player = orderedPlayers[playerIndex];
            Log($"Using plauers {player.Name}");
            orderedPlayers.RemoveAt(playerIndex);
            originalPlayers.Remove(player);
            return player;
        }

        private List<SkillWisePlayer> OrderByAndShuffleSequence(List<SkillWisePlayer> players, Func<SkillWisePlayer, double> orderBy)
        {
            var random = new Random();
            var orderedPlayers = players.OrderBy(orderBy).ThenBy((p) => p.Rank).ThenBy((p) => random.Next(0, 10));
            return orderedPlayers.ToList();
        }

        private static void Log(string logMsg)
        {
            if (!GlobalParameters.IsLogEnabled) return;
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(logMsg);
            Console.ForegroundColor = ConsoleColor.White;
        }
    }

}
