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
}
