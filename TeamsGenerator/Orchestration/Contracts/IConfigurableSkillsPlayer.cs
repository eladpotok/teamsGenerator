using TeamsGenerator.API;

namespace TeamsGenerator.Orchestration.Contracts
{
    public interface IConfigurableSkillsPlayer
    {
        double GetSkillValue(string skillId);
        void SetActiveSkills(IEnumerable<SkillDefinition> skillDefinitions);
    }
}
