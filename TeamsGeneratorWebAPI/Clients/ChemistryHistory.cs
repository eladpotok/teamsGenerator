using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Security.Cryptography;
using System.Text;
using TeamsGenerator.Orchestration;

namespace TeamsGeneratorWebAPI.Clients
{
    internal static class ChemistryHistory
    {
        private const double HalfLifeDays = 180;
        private const double ConfidenceSamples = 5;
        private const int MaximumSamplesPerMatchday = 3;

        internal static string GetPartitionKey(string ownerId)
        {
            var ownerHash = SHA256.HashData(Encoding.UTF8.GetBytes(ownerId));
            return $"Chemistry_{Convert.ToHexString(ownerHash)}";
        }

        internal static ChemistryMatchdayEntity CreateMatchdayEntity(
            string ownerId,
            string matchdayId,
            IEnumerable<MatchEntity> matches,
            DateTimeOffset completedAt)
        {
            var evidenceByPair = new Dictionary<string, PairAccumulator>(
                StringComparer.OrdinalIgnoreCase);
            var matchList = matches?.ToList() ?? new List<MatchEntity>();

            foreach (var match in matchList)
            {
                AddMatchEvidence(match.SerializedMatch, evidenceByPair);
            }

            var pairEvidence = evidenceByPair
                .Select(item =>
                {
                    var samples = Math.Min(
                        item.Value.Samples,
                        MaximumSamplesPerMatchday);
                    return new PairEvidence
                    {
                        PairKey = item.Key,
                        Evidence = item.Value.Evidence
                            / item.Value.Samples
                            * samples,
                        Samples = samples
                    };
                })
                .ToList();

            return new ChemistryMatchdayEntity
            {
                PartitionKey = GetPartitionKey(ownerId),
                RowKey = matchdayId,
                PairEvidenceJson = JsonConvert.SerializeObject(pairEvidence),
                CompletedAt = completedAt.ToUniversalTime()
            };
        }

        internal static IReadOnlyDictionary<string, double> CalculateScores(
            IEnumerable<ChemistryMatchdayEntity> matchdays,
            DateTimeOffset now)
        {
            var totals = new Dictionary<string, WeightedEvidence>(
                StringComparer.OrdinalIgnoreCase);

            foreach (var matchday in matchdays)
            {
                var ageDays = Math.Max(
                    0,
                    (now - matchday.CompletedAt).TotalDays);
                var recencyWeight = Math.Pow(0.5, ageDays / HalfLifeDays);
                var evidence = JsonConvert.DeserializeObject<List<PairEvidence>>(
                    matchday.PairEvidenceJson) ?? new List<PairEvidence>();

                foreach (var pair in evidence)
                {
                    if (string.IsNullOrWhiteSpace(pair.PairKey)
                        || pair.Samples <= 0)
                    {
                        continue;
                    }

                    if (!totals.TryGetValue(pair.PairKey, out var total))
                    {
                        total = new WeightedEvidence();
                        totals[pair.PairKey] = total;
                    }

                    total.Evidence += pair.Evidence * recencyWeight;
                    total.Samples += pair.Samples * recencyWeight;
                }
            }

            return totals.ToDictionary(
                item => item.Key,
                item =>
                {
                    var averageResidual =
                        item.Value.Evidence / item.Value.Samples;
                    var confidence =
                        item.Value.Samples
                        / (item.Value.Samples + ConfidenceSamples);
                    return Math.Clamp(
                        averageResidual * 2 * confidence,
                        -1,
                        1);
                },
                StringComparer.OrdinalIgnoreCase);
        }

        private static void AddMatchEvidence(
            string serializedMatch,
            IDictionary<string, PairAccumulator> evidenceByPair)
        {
            if (string.IsNullOrWhiteSpace(serializedMatch))
            {
                return;
            }

            var match = JObject.Parse(serializedMatch);
            var teamA = GetObject(match, "teamA");
            var teamB = GetObject(match, "teamB");
            if (teamA == null || teamB == null)
            {
                return;
            }

            var teamAPlayers = GetPlayerKeys(teamA);
            var teamBPlayers = GetPlayerKeys(teamB);
            if (teamAPlayers.Count < 2 && teamBPlayers.Count < 2)
            {
                return;
            }

            var teamAScore = GetDouble(teamA, "score") ?? 0;
            var teamBScore = GetDouble(teamB, "score") ?? 0;
            var actualA = teamAScore == teamBScore
                ? 0.5
                : teamAScore > teamBScore ? 1 : 0;
            var expectedA = GetExpectedResult(
                GetTeamRating(teamA),
                GetTeamRating(teamB));
            var residualA = actualA - expectedA;

            AddTeamPairs(teamAPlayers, residualA, evidenceByPair);
            AddTeamPairs(teamBPlayers, -residualA, evidenceByPair);
        }

        private static void AddTeamPairs(
            IReadOnlyList<string> playerKeys,
            double evidence,
            IDictionary<string, PairAccumulator> evidenceByPair)
        {
            for (var first = 0; first < playerKeys.Count; first++)
            {
                for (var second = first + 1; second < playerKeys.Count; second++)
                {
                    var pairKey = ChemistryPairKey.Create(
                        playerKeys[first],
                        playerKeys[second]);
                    if (pairKey == null)
                    {
                        continue;
                    }

                    if (!evidenceByPair.TryGetValue(pairKey, out var total))
                    {
                        total = new PairAccumulator();
                        evidenceByPair[pairKey] = total;
                    }

                    total.Evidence += evidence;
                    total.Samples++;
                }
            }
        }

        private static List<string> GetPlayerKeys(JObject team)
        {
            var players = GetValue(team, "players") as JArray;
            if (players == null)
            {
                return new List<string>();
            }

            return players
                .OfType<JObject>()
                .Select(player =>
                    GetString(player, "key")
                    ?? GetString(player, "id"))
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static double GetTeamRating(JObject team)
        {
            var players = GetValue(team, "players") as JArray;
            if (players == null || players.Count == 0)
            {
                return 5;
            }

            var ratings = players
                .OfType<JObject>()
                .Select(GetPlayerRating)
                .ToList();
            return ratings.Count == 0 ? 5 : ratings.Average();
        }

        private static double GetPlayerRating(JObject player)
        {
            var rank = GetDouble(player, "rank");
            if (rank.HasValue && rank.Value > 0)
            {
                return rank.Value;
            }

            var skills = new[]
            {
                GetDouble(player, "attack"),
                GetDouble(player, "defence"),
                GetDouble(player, "stamina"),
                GetDouble(player, "leadership"),
                GetDouble(player, "passing")
            }.Where(value => value.HasValue)
             .Select(value => value.Value)
             .ToList();

            return skills.Count == 0 ? 5 : skills.Average();
        }

        private static double GetExpectedResult(
            double teamRating,
            double opponentRating)
        {
            return 1
                / (1 + Math.Pow(10, (opponentRating - teamRating) / 2));
        }

        private static JObject GetObject(JObject source, string propertyName)
        {
            return GetValue(source, propertyName) as JObject;
        }

        private static JToken GetValue(JObject source, string propertyName)
        {
            return source.GetValue(
                propertyName,
                StringComparison.OrdinalIgnoreCase);
        }

        private static string GetString(JObject source, string propertyName)
        {
            return GetValue(source, propertyName)?.Value<string>();
        }

        private static double? GetDouble(JObject source, string propertyName)
        {
            return GetValue(source, propertyName)?.Value<double?>();
        }

        private sealed class PairAccumulator
        {
            public double Evidence { get; set; }
            public int Samples { get; set; }
        }

        private sealed class WeightedEvidence
        {
            public double Evidence { get; set; }
            public double Samples { get; set; }
        }

        private sealed class PairEvidence
        {
            public string PairKey { get; set; }
            public double Evidence { get; set; }
            public int Samples { get; set; }
        }
    }
}
