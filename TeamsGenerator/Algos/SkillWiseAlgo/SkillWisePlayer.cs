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

        [EditableInClientAttribute(Show = false)]
        public string Key { get; set; }

        [EditableInClientAttribute(Show = false)]
        public string ModifyTime { get; set; }

        [EditableInClientAttribute(Show = false)]
        public string Id { get; set; }
        [EditableInClientAttribute(Show = false)]
        public bool IsArrived { get; set; }

        [VersionAttribute(MinVersion = "8.0.1")]
        [DisplayTextAttribute(Text = "GoalKeeper")]
        public bool IsGoalKeeper { get; set; }
        [EditableInClientAttribute(Show = false)]
        public bool IsLocked { get; set; }
    }
}
