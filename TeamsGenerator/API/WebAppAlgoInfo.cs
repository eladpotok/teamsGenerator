using System;
using System.Collections.Generic;
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
                { typeof(string), "text" },
                { typeof(double), "number" },

            };

            var playerInterface = Type.GetType($"TeamsGenerator.Algos.{AlgoName}Algo.{AlgoName}Player");
            var playerProperties = playerInterface.GetProperties();

            foreach (var prop in playerProperties)
            {
                var propertyAttributes = prop.GetCustomAttributes(true);

                var showInClient = true;
                if (propertyAttributes != null)
                {
                    foreach (var att in propertyAttributes)
                    {
                        if(att is EditableInClientAttribute editableInClient)
                        {
                            showInClient = editableInClient.Show;
                        }
                    }
                }

                PlayerProperties.Add(new PlayerProperties() { Name = prop.Name, Type = inputToTypeMapper[prop.PropertyType] , ShowInClient = showInClient });
            }

        }
    }
}
