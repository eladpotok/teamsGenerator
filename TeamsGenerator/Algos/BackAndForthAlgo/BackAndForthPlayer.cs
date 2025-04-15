using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TeamsGenerator.API;
using TeamsGenerator.Orchestration.Contracts;

namespace TeamsGenerator.Algos.BackAndForthAlgo
{
    public class BackAndForthPlayer : IPlayer, IGoalKeeperSupport
    {
        public string Name { get; set; }
        public double Rank { get; set; }

        [EditableInClientAttribute(Show = false)]
        public string Key { get; set; }

        [EditableInClientAttribute(Show = false)]
        public string ModifyTime { get; set; }

        [EditableInClientAttribute(Show = false)]
        public string Id { get; set; }
        
        [EditableInClientAttribute(Show = false)]
        public bool IsArrived { get; set; }

        [VersionAttribute(MinVersion = "8.0.1")]
        [DisplayTextAttribute(Text = "Goal Keeper")]
        public bool IsGoalKeeper { get; set; }

        [EditableInClientAttribute(Show = false)]
        public bool IsLocked { get; set; }
    }
}
