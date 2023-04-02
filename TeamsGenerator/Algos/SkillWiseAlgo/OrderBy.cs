using System;

namespace TeamsGenerator.Algos.SkillWiseAlgo
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

}
