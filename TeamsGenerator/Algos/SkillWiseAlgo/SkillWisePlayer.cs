using TeamsGenerator.Orchestration.Contracts;

namespace TeamsGenerator.Algos.SkillWiseAlgo
{
    public class SkillWisePlayer : IPlayer
    {
        public double Rank { get; set; }
        public string Name { get; set; }
        public float Defence { get; set; }
        public float Attack { get; set; }
        public float Stamina { get; set; }
        public float Leadership { get; set; }
        public float Passing { get; set; }
    }
}
