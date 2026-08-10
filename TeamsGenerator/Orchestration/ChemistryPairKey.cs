using System;

namespace TeamsGenerator.Orchestration
{
    public static class ChemistryPairKey
    {
        public static string Create(string firstPlayerKey, string secondPlayerKey)
        {
            if (string.IsNullOrWhiteSpace(firstPlayerKey)
                || string.IsNullOrWhiteSpace(secondPlayerKey))
            {
                return null;
            }

            return string.Compare(
                firstPlayerKey,
                secondPlayerKey,
                StringComparison.OrdinalIgnoreCase) <= 0
                ? $"{firstPlayerKey}::{secondPlayerKey}"
                : $"{secondPlayerKey}::{firstPlayerKey}";
        }
    }
}
