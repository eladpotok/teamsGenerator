using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TeamsGenerator.Algos.SkillWiseAlgo;
using TeamsGenerator.API;
using TeamsGenerator.Orchestration.Contracts;

namespace TeamsGenerator.Algos.PositionsAlgo
{
    public class PositionsPlayer : IPlayer
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


        [EditableInClientAttribute(Show = false)]
        public bool IsLocked { get; set; }
        public List<Position> Positions { get; set; }
    }
}
