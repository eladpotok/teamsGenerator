using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TeamsGenerator.API;
using TeamsGenerator.Orchestration.Contracts;

namespace TeamsGenerator.Algos.SkillWiseAlgo
{
    public class SkillWisePlayer : IPlayer, IGoalKeeperSupport, IConfigurableSkillsPlayer
    {
        private IReadOnlyList<SkillDefinition> _activeSkills =
            SkillDefinition.CreateDefaults();

        [EditableInClientAttribute(Show = false)]
        public double Rank
        {
            get
            {
                return _activeSkills.Count == 0
                    ? 0
                    : _activeSkills.Average(skill => GetSkillValue(skill.Id));
            }
            set { }
        }
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
        public int? WaitingListOrder { get; set; }

        [VersionAttribute(MinVersion = "8.0.1")]
        [DisplayTextAttribute(Text = "GoalKeeper")]
        public bool IsGoalKeeper { get; set; }
        [EditableInClientAttribute(Show = false)]
        public bool IsLocked { get; set; }

        [JsonExtensionData]
        private IDictionary<string, JToken> AdditionalValues { get; set; } =
            new Dictionary<string, JToken>(System.StringComparer.OrdinalIgnoreCase);

        public double GetSkillValue(string skillId)
        {
            if (string.IsNullOrWhiteSpace(skillId))
            {
                return SkillDefinition.DefaultValue;
            }

            switch (skillId.Trim().ToLowerInvariant())
            {
                case "attack": return Attack;
                case "defence": return Defence;
                case "stamina": return Stamina;
                case "leadership": return Leadership;
                case "passing": return Passing;
            }

            return AdditionalValues.TryGetValue(skillId, out var value)
                && double.TryParse(value.ToString(), out var parsed)
                    ? parsed
                    : SkillDefinition.DefaultValue;
        }

        public void SetActiveSkills(
            IEnumerable<SkillDefinition> skillDefinitions)
        {
            _activeSkills = SkillDefinition.Normalize(skillDefinitions);
        }
    }
}
