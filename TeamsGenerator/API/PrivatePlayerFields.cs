using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TeamsGenerator.Orchestration.Contracts;

namespace TeamsGenerator.API
{
    public static class PrivatePlayerFields
    {
        public static bool IsDedicatedAssessment(string name) =>
            string.Equals(name, "goalkeeperSkill", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "isRecoveringFromInjury", StringComparison.OrdinalIgnoreCase);

        public static JToken NormalizeAssessment(string name, JToken value)
        {
            if (string.Equals(name, "goalkeeperSkill", StringComparison.OrdinalIgnoreCase))
            {
                var rating = PlayerRatings.NormalizeGoalkeeperSkill(value.Value<double?>());
                return rating.HasValue ? new JValue(rating.Value) : JValue.CreateNull();
            }
            if (string.Equals(name, "isRecoveringFromInjury", StringComparison.OrdinalIgnoreCase))
                return new JValue(value.Value<bool?>() ?? false);
            return value.DeepClone();
        }

        public static JToken RemoveFromSnapshot(JToken source)
        {
            var result = source.DeepClone();
            Strip(result);
            return result;
        }

        public static string SanitizeJson(string json) =>
            string.IsNullOrWhiteSpace(json) ? json
                : RemoveFromSnapshot(JToken.Parse(json)).ToString(Formatting.None);

        private static void Strip(JToken token)
        {
            if (token is JObject obj)
            {
                foreach (var property in obj.Properties().ToList())
                {
                    if (IsDedicatedAssessment(property.Name)
                        || string.Equals(property.Name, "effectiveRank", StringComparison.OrdinalIgnoreCase))
                        property.Remove();
                    else
                        Strip(property.Value);
                }
            }
            else if (token is JArray array)
            {
                foreach (var item in array) Strip(item);
            }
        }
    }
}
