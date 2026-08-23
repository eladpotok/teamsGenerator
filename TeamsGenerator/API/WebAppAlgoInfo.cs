using System;
using System.Collections.Generic;
using System.Linq;
using TeamsGenerator.Algos.PositionsAlgo;
using TeamsGenerator.Orchestration;

namespace TeamsGenerator.API
{

    public class WebAppAlgoInfo
    {
        public string AlgoName { get; set; }

        public string Description { get; set; }
        public string DisplayName { get; set; }
        public int AlgoKey { get; set; }

        public List<PlayerProperties> PlayerProperties { get; set; }

        public WebAppAlgoInfo(AlgoType algoKey, string displayName, string description)
        {
            Description = description;
            DisplayName = displayName;
            AlgoKey = (int)algoKey;
            AlgoName = algoKey.ToString();
            PlayerProperties = new List<PlayerProperties>();
        }

        public void Init()
        {
            var inputToTypeMapper = new Dictionary<Type, string>() {
                { typeof(Single), "number" },
                { typeof(int), "number" },
                { typeof(string), "text" },
                { typeof(double), "number" },
                { typeof(bool), "boolean" },
                { typeof(List<Position>), "list" },
                { typeof(List<string>), "list" },
            };

            var playerInterface = Type.GetType($"TeamsGenerator.Algos.{AlgoName}Algo.{AlgoName}Player");
            var playerProperties = playerInterface.GetProperties();

            foreach (var prop in playerProperties)
            {
                var propertyAttributes = prop.GetCustomAttributes(true);

                var showInClient = true;
                var displayText = prop.Name;
                string minVersion = null;
                if (propertyAttributes != null)
                {
                    foreach (var att in propertyAttributes)
                    {
                        if(att is EditableInClientAttribute editableInClient)
                        {
                            showInClient = editableInClient.Show;
                        }
                        if(att is DisplayTextAttribute displayTextAtt)
                        {
                            displayText = displayTextAtt.Text;
                        }
                        if (att is VersionAttribute versionAtt)
                        {
                            minVersion = versionAtt.MinVersion;
                        }
                    }
                }

                PlayerProperties.Add(new PlayerProperties()
                {
                    Name = prop.Name,
                    Type = inputToTypeMapper[
                        Nullable.GetUnderlyingType(prop.PropertyType)
                        ?? prop.PropertyType],
                    ShowInClient = showInClient,
                    DisplayText = displayText,
                    MinVersion = minVersion,
                    DefaultValue =
                        (Nullable.GetUnderlyingType(prop.PropertyType)
                            ?? prop.PropertyType) == typeof(Single)
                        || (Nullable.GetUnderlyingType(prop.PropertyType)
                            ?? prop.PropertyType) == typeof(double)
                            ? SkillDefinition.DefaultValue
                            : null
                });
            }

        }

        public WebAppAlgoInfo ForSkills(
            IEnumerable<SkillDefinition> skillDefinitions)
        {
            var result = new WebAppAlgoInfo(
                (AlgoType)AlgoKey,
                DisplayName,
                Description);

            if (AlgoKey != (int)AlgoType.SkillWise
                && AlgoKey != (int)AlgoType.Positions)
            {
                result.PlayerProperties = PlayerProperties
                    .Select(CloneProperty)
                    .ToList();
                return result;
            }

            result.PlayerProperties = PlayerProperties
                .Where(property =>
                    !property.ShowInClient
                    || property.Type != "number")
                .Select(CloneProperty)
                .ToList();
            result.PlayerProperties.AddRange(
                SkillDefinition.Normalize(skillDefinitions)
                    .Select(skill => new PlayerProperties
                    {
                        Name = skill.Id,
                        DisplayText = skill.Name,
                        Type = "number",
                        ShowInClient = true,
                        DefaultValue = SkillDefinition.DefaultValue
                    }));
            return result;
        }

        private static PlayerProperties CloneProperty(
            PlayerProperties property)
        {
            return new PlayerProperties
            {
                Name = property.Name,
                Type = property.Type,
                DisplayText = property.DisplayText,
                ShowInClient = property.ShowInClient,
                MinVersion = property.MinVersion,
                DefaultValue = property.DefaultValue
            };
        }
    }
}
