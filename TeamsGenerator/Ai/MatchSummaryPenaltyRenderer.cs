using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TeamsGenerator.Ai
{
    internal static class MatchSummaryPenaltyRenderer
    {
        private static readonly Regex PlayerRatingsStart = new Regex(
            @"(?m)^(?=1\.\s)",
            RegexOptions.Compiled);

        internal static string PrepareForAiNarrative(string factSheet)
        {
            var factSheetToken = JObject.Parse(factSheet);
            var patterns = factSheetToken["verifiedPatterns"] as JArray;
            if (patterns == null || !HasAggregatePenaltyPattern(patterns))
            {
                return factSheet;
            }

            factSheetToken["penaltyGoals"] = new JArray();
            foreach (var pattern in patterns
                .OfType<JObject>()
                .Where(IsAggregatePenaltyPattern)
                .ToList())
            {
                pattern.Remove();
            }

            return factSheetToken.ToString(Formatting.None);
        }

        internal static string AddVerifiedPenaltySummary(
            string report,
            string factSheet,
            string language)
        {
            if (string.IsNullOrWhiteSpace(report))
            {
                throw new ArgumentException("The AI report is required.", nameof(report));
            }

            var patterns = JObject.Parse(factSheet)["verifiedPatterns"] as JArray;
            if (patterns == null)
            {
                return report;
            }

            var heavyEvening = patterns
                .OfType<JObject>()
                .FirstOrDefault(pattern =>
                    string.Equals(
                        pattern.Value<string>("type"),
                        "penalty_heavy_evening",
                        StringComparison.Ordinal));
            var scoringRuns = patterns
                .OfType<JObject>()
                .Where(pattern =>
                    string.Equals(
                        pattern.Value<string>("type"),
                        "penalty_scoring_run",
                        StringComparison.Ordinal))
                .Select(pattern => new PenaltyScoringRun(
                    pattern.Value<string>("player"),
                    pattern.Value<int?>("penaltyGoals") ?? 0))
                .Where(run =>
                    !string.IsNullOrWhiteSpace(run.Player)
                    && run.PenaltyGoals > 0)
                .ToList();

            if (heavyEvening == null && scoringRuns.Count == 0)
            {
                return report;
            }

            var summary = CreateSummary(
                heavyEvening,
                scoringRuns,
                MatchSummaryPrompt.NormalizeLanguage(language));
            return InsertBeforeRatings(report, summary);
        }

        private static bool HasAggregatePenaltyPattern(JArray patterns)
        {
            return patterns.OfType<JObject>().Any(IsAggregatePenaltyPattern);
        }

        private static bool IsAggregatePenaltyPattern(JObject pattern)
        {
            var type = pattern.Value<string>("type");
            return string.Equals(
                    type,
                    "penalty_heavy_evening",
                    StringComparison.Ordinal)
                || string.Equals(
                    type,
                    "penalty_scoring_run",
                    StringComparison.Ordinal);
        }

        private static string CreateSummary(
            JObject heavyEvening,
            IReadOnlyCollection<PenaltyScoringRun> scoringRuns,
            string language)
        {
            var runDetails = string.Join(
                language == "en" ? ", " : ", ",
                scoringRuns.Select(run =>
                    language == "en"
                        ? $"{run.Player} scored {run.PenaltyGoals} of them"
                        : $"{run.Player} הבקיע {run.PenaltyGoals} מהם"));

            if (heavyEvening != null)
            {
                var penaltyGoals = heavyEvening.Value<int?>("penaltyGoals") ?? 0;
                var matches = heavyEvening.Value<int?>("matchesWithPenalties") ?? 0;

                if (language == "en")
                {
                    var baseSummary =
                        $"The evening featured {penaltyGoals} penalty goals across {matches} matches.";
                    return scoringRuns.Count == 0
                        ? baseSummary
                        : $"{baseSummary.TrimEnd('.')} — {runDetails}.";
                }

                var hebrewSummary =
                    $"הערב כלל {penaltyGoals} שערי פנדל ב-{matches} משחקים.";
                return scoringRuns.Count == 0
                    ? hebrewSummary
                    : $"{hebrewSummary.TrimEnd('.')} — {runDetails}.";
            }

            return language == "en"
                ? $"{runDetails} from the penalty spot."
                : $"{runDetails} מהנקודה הלבנה.";
        }

        private static string InsertBeforeRatings(string report, string summary)
        {
            var match = PlayerRatingsStart.Match(report);
            if (!match.Success)
            {
                return $"{report.TrimEnd()}{Environment.NewLine}{Environment.NewLine}{summary}";
            }

            var narrative = report.Substring(0, match.Index).TrimEnd();
            var ratings = report.Substring(match.Index).TrimStart();
            return $"{narrative}{Environment.NewLine}{Environment.NewLine}"
                + $"{summary}{Environment.NewLine}{Environment.NewLine}{ratings}";
        }

        private sealed class PenaltyScoringRun
        {
            internal PenaltyScoringRun(string player, int penaltyGoals)
            {
                Player = player;
                PenaltyGoals = penaltyGoals;
            }

            internal string Player { get; }

            internal int PenaltyGoals { get; }
        }
    }
}
