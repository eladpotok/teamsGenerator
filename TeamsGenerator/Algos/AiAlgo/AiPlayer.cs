using System.Collections.Generic;
using TeamsGenerator.API;
using TeamsGenerator.Orchestration.Contracts;

namespace TeamsGenerator.Algos.AiAlgo
{
    public class AiPlayer : IPlayer
    {
        public double Rank { get { return 0; } set { } }
        public string Name { get; set; }

        [EditableInClient(Show = false)]
        public string Key { get; set; }

        [EditableInClient(Show = false)]
        public string ModifyTime { get; set; }

        [EditableInClient(Show = false)]
        public string Id { get; set; }
        [EditableInClient(Show = false)]
        public bool IsArrived { get; set; }
        public string Description { get; set; }
    }

    public class AiTeam
    {
        public List<string> Players { get; set; }

        public int TeamIndex { get; set; }
        public IEnumerable<string> Weakness { get; set; }
        public IEnumerable<string> Strength { get; set; }
        public string Description { get; set; }
        public string PlayStyle { get; set; }
        public float Defence { get; set; }
        public float Attack { get; set; }
        public float Stamina { get; set; }
        public float Leadership { get; set; }
        public float Passing { get; set; }
    }
}
