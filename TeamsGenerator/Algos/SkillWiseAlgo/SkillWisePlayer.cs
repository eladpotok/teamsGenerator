using TeamsGenerator.API;
using TeamsGenerator.Orchestration.Contracts;

namespace TeamsGenerator.Algos.SkillWiseAlgo
{
    public class SkillWisePlayer : IPlayer
    {
        [EditableInClientAttribute(Show = false)]
        public double Rank { get { return (Defence + Attack + Stamina + Leadership + Passing) / 5; } set { } }
        public string Name { get; set; }
        public float Defence { get; set; }
        public float Attack { get; set; }
        public float Stamina { get; set; }
        public float Leadership { get; set; }
        public float Passing { get; set; }
    }
}
