using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TeamsGenerator.Ai
{
    internal static class MatchSummaryAnalyzer
    {
        internal static string CreateFactSheet(object input)
        {
            var source = input as JToken ?? JToken.FromObject(input);
            var matches = FindMatches(source);
            var standings = FindStandings(source, matches);
            var scorers = FindLeaderboard(source, matches, "topScorers");
            var assisters = FindLeaderboard(source, matches, "topAssists");
            var players = FindPlayers(source, matches);
            var playerTeams = FindPlayerTeams(matches);
            var partnerships = FindAssistPartnerships(matches);

            foreach (var name in scorers.Keys.Concat(assisters.Keys))
            {
                players.Add(name);
            }

            var ratings = CreateRatings(players, scorers, assisters, standings, playerTeams);
            var factSheet = new
            {
                standings = standings.Select((entry, index) => new
                {
                    position = index + 1,
                    team = entry.Team,
                    wins = entry.Wins,
                    draws = entry.Draws,
                    losses = entry.Losses,
                    goalsFor = entry.GoalsFor,
                    goalsAgainst = entry.GoalsAgainst,
                    points = entry.Points
                }),
                awards = new
                {
                    topScorers = FindLeaders(scorers),
                    topAssisters = FindLeaders(assisters)
                },
                players = ratings,
                verifiedPatterns = CreatePatterns(
                    matches,
                    standings,
                    scorers,
                    assisters,
                    playerTeams,
                    partnerships),
                dataLimitations = CreateLimitations(matches, standings, scorers, assisters, players)
            };

            return JsonConvert.SerializeObject(factSheet, Formatting.None);
        }

        private static List<JObject> FindMatches(JToken source)
        {
            var matches = new List<JObject>();

            foreach (var candidate in GetTokens(source).OfType<JObject>())
            {
                var serializedMatch = GetValue(candidate, "SerializedMatch");
                if (serializedMatch != null && serializedMatch.Type == JTokenType.String)
                {
                    TryAddSerializedMatch(matches, serializedMatch.Value<string>());
                }

                if (GetValue(candidate, "teamA") is JObject
                    && GetValue(candidate, "teamB") is JObject)
                {
                    matches.Add(candidate);
                }
            }

            return matches
                .GroupBy(match => match.ToString(Formatting.None))
                .Select(group => group.First())
                .ToList();
        }

        private static void TryAddSerializedMatch(List<JObject> matches, string serializedMatch)
        {
            if (string.IsNullOrWhiteSpace(serializedMatch))
            {
                return;
            }

            try
            {
                var match = JObject.Parse(serializedMatch);
                if (GetValue(match, "teamA") is JObject
                    && GetValue(match, "teamB") is JObject)
                {
                    matches.Add(match);
                }
            }
            catch (JsonReaderException)
            {
                // Invalid serialized matches are excluded and reported as unavailable data.
            }
        }

        private static List<StandingFact> FindStandings(
            JToken source,
            IEnumerable<JObject> matches)
        {
            var candidates = GetDataTokens(source, matches)
                .OfType<JObject>()
                .Select(ParseStandings)
                .Where(standings => standings.Count > 0)
                .OrderByDescending(standings => standings.Count)
                .ToList();

            return candidates.FirstOrDefault() ?? new List<StandingFact>();
        }

        private static List<StandingFact> ParseStandings(JObject candidate)
        {
            var standings = new List<StandingFact>();
            foreach (var property in candidate.Properties())
            {
                var stats = property.Value as JObject;
                if (stats == null || !HasAnyProperty(stats, "w", "d", "l", "gf", "ga", "points"))
                {
                    continue;
                }

                var wins = GetInt(stats, "w");
                var draws = GetInt(stats, "d");
                standings.Add(new StandingFact
                {
                    Team = property.Name,
                    Wins = wins,
                    Draws = draws,
                    Losses = GetInt(stats, "l"),
                    GoalsFor = GetInt(stats, "gf"),
                    GoalsAgainst = GetInt(stats, "ga"),
                    Points = GetValue(stats, "points") == null
                        ? wins * 3 + draws
                        : GetInt(stats, "points")
                });
            }

            return standings
                .OrderByDescending(item => item.Points)
                .ThenByDescending(item => item.GoalsFor - item.GoalsAgainst)
                .ThenByDescending(item => item.GoalsFor)
                .ToList();
        }

        private static Dictionary<string, int> FindLeaderboard(
            JToken source,
            IEnumerable<JObject> matches,
            string propertyName)
        {
            var candidates = GetDataTokens(source, matches)
                .OfType<JProperty>()
                .Where(property => string.Equals(
                    property.Name,
                    propertyName,
                    StringComparison.OrdinalIgnoreCase))
                .Select(property => ParseLeaderboard(property.Value))
                .Where(entries => entries.Count > 0)
                .OrderByDescending(entries => entries.Count)
                .ThenByDescending(entries => entries.Values.Sum())
                .ToList();

            return candidates.FirstOrDefault()
                ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }

        private static Dictionary<string, int> ParseLeaderboard(JToken value)
        {
            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var entries = value as JArray;
            if (entries == null)
            {
                return result;
            }

            foreach (var entry in entries.OfType<JObject>())
            {
                var name = GetName(GetValue(entry, "name"));
                var total = GetInt(entry, "scores", "goals", "assists", "count", "value");
                if (!string.IsNullOrWhiteSpace(name))
                {
                    result[name] = total;
                }
            }

            return result;
        }

        private static HashSet<string> FindPlayers(JToken source, IEnumerable<JObject> matches)
        {
            var players = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in GetDataTokens(source, matches)
                .OfType<JProperty>()
                .Where(property => string.Equals(
                    property.Name,
                    "players",
                    StringComparison.OrdinalIgnoreCase)))
            {
                AddPlayerNames(players, property.Value);
            }

            foreach (var match in matches)
            {
                AddTeamPlayers(players, GetValue(match, "teamA") as JObject);
                AddTeamPlayers(players, GetValue(match, "teamB") as JObject);
            }

            return players;
        }

        private static Dictionary<string, string> FindPlayerTeams(IEnumerable<JObject> matches)
        {
            var teams = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var match in matches)
            {
                AddPlayerTeams(teams, GetValue(match, "teamA") as JObject);
                AddPlayerTeams(teams, GetValue(match, "teamB") as JObject);
            }

            return teams;
        }

        private static void AddTeamPlayers(HashSet<string> players, JObject team)
        {
            if (team != null)
            {
                AddPlayerNames(players, GetValue(team, "players"));
            }
        }

        private static void AddPlayerTeams(Dictionary<string, string> teams, JObject team)
        {
            if (team == null)
            {
                return;
            }

            var teamName = GetName(GetValue(team, "color"))
                ?? GetName(GetValue(team, "name"));
            var teamPlayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddPlayerNames(teamPlayers, GetValue(team, "players"));

            if (string.IsNullOrWhiteSpace(teamName))
            {
                return;
            }

            foreach (var player in teamPlayers)
            {
                teams[player] = teamName;
            }
        }

        private static void AddPlayerNames(ISet<string> players, JToken value)
        {
            var array = value as JArray;
            if (array == null)
            {
                return;
            }

            foreach (var item in array)
            {
                var name = GetName(item);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    players.Add(name);
                }
            }
        }

        private static List<PartnershipFact> FindAssistPartnerships(IEnumerable<JObject> matches)
        {
            var directed = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var match in matches)
            {
                foreach (var candidate in match.DescendantsAndSelf().OfType<JObject>())
                {
                    var scorer = GetName(GetValue(
                        candidate,
                        "scorer",
                        "goalScorer",
                        "scoredBy"));
                    var assister = GetName(GetValue(
                        candidate,
                        "assist",
                        "assister",
                        "assistedBy"));

                    if (string.IsNullOrWhiteSpace(scorer)
                        || string.IsNullOrWhiteSpace(assister)
                        || string.Equals(scorer, assister, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var key = assister + "\u001f" + scorer;
                    directed[key] = directed.ContainsKey(key) ? directed[key] + 1 : 1;
                }
            }

            var partnerships = new List<PartnershipFact>();
            var processed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in directed)
            {
                var names = pair.Key.Split('\u001f');
                var reverseKey = names[1] + "\u001f" + names[0];
                if (!directed.ContainsKey(reverseKey))
                {
                    continue;
                }

                var identity = string.Compare(
                    names[0],
                    names[1],
                    StringComparison.OrdinalIgnoreCase) < 0
                    ? names[0] + "\u001f" + names[1]
                    : names[1] + "\u001f" + names[0];

                if (!processed.Add(identity))
                {
                    continue;
                }

                partnerships.Add(new PartnershipFact
                {
                    PlayerA = names[0],
                    PlayerB = names[1],
                    AToB = pair.Value,
                    BToA = directed[reverseKey]
                });
            }

            return partnerships;
        }

        private static List<PlayerRatingFact> CreateRatings(
            IEnumerable<string> players,
            IDictionary<string, int> scorers,
            IDictionary<string, int> assisters,
            IList<StandingFact> standings,
            IDictionary<string, string> playerTeams)
        {
            var maxGoals = scorers.Count == 0 ? 0 : scorers.Values.Max();
            var maxAssists = assisters.Count == 0 ? 0 : assisters.Values.Max();

            return players.Select(player =>
            {
                var goals = GetValue(scorers, player);
                var assists = GetValue(assisters, player);
                var factors = new List<string>();
                var score = 5.0 + Math.Min(2.4, goals * 0.6) + Math.Min(1.8, assists * 0.45);

                if (goals > 0)
                {
                    factors.Add(FormatCount(goals, "goal", "goals"));
                }

                if (assists > 0)
                {
                    factors.Add(FormatCount(assists, "assist", "assists"));
                }

                if (maxGoals > 0 && goals == maxGoals)
                {
                    score += 0.4;
                    factors.Add("top scorer");
                }

                if (maxAssists > 0 && assists == maxAssists)
                {
                    score += 0.4;
                    factors.Add("top assister");
                }

                string team;
                var teamPosition = 0;
                if (playerTeams.TryGetValue(player, out team))
                {
                    teamPosition = standings
                        .Select((entry, index) => new { entry.Team, Position = index + 1 })
                        .Where(entry => string.Equals(
                            entry.Team,
                            team,
                            StringComparison.OrdinalIgnoreCase))
                        .Select(entry => entry.Position)
                        .FirstOrDefault();

                    if (teamPosition == 1)
                    {
                        score += 0.6;
                        factors.Add("team finished first");
                    }
                    else if (teamPosition == 2 && standings.Count > 2)
                    {
                        score += 0.35;
                        factors.Add("team finished second");
                    }
                    else if (teamPosition == standings.Count && standings.Count > 1)
                    {
                        score -= 0.25;
                        factors.Add("team finished last");
                    }
                }

                if (factors.Count == 0)
                {
                    factors.Add("limited recorded contribution");
                }

                return new PlayerRatingFact
                {
                    Name = player,
                    Team = playerTeams.ContainsKey(player) ? playerTeams[player] : null,
                    Goals = goals,
                    Assists = assists,
                    Rating = Math.Round(Math.Max(1, Math.Min(10, score)), 1),
                    RatingFactors = factors
                };
            })
            .OrderByDescending(player => player.Rating)
            .ThenByDescending(player => player.Goals)
            .ThenByDescending(player => player.Assists)
            .ThenBy(player => player.Name)
            .ToList();
        }

        private static List<object> CreatePatterns(
            IList<JObject> matches,
            IList<StandingFact> standings,
            IDictionary<string, int> scorers,
            IDictionary<string, int> assisters,
            IDictionary<string, string> playerTeams,
            IEnumerable<PartnershipFact> partnerships)
        {
            var patterns = new List<object>();
            var maxGoals = scorers.Count == 0 ? 0 : scorers.Values.Max();

            foreach (var player in assisters.Where(entry => entry.Value >= 2))
            {
                if (GetValue(scorers, player.Key) == 0)
                {
                    patterns.Add(new
                    {
                        type = "creator_without_goal",
                        player = player.Key,
                        assists = player.Value
                    });
                }
            }

            if (standings.Count > 1 && maxGoals > 0)
            {
                var lastTeam = standings.Last().Team;
                foreach (var scorer in scorers.Where(entry => entry.Value == maxGoals))
                {
                    string team;
                    if (playerTeams.TryGetValue(scorer.Key, out team)
                        && string.Equals(team, lastTeam, StringComparison.OrdinalIgnoreCase))
                    {
                        patterns.Add(new
                        {
                            type = "top_scorer_on_last_place_team",
                            player = scorer.Key,
                            goals = scorer.Value,
                            team
                        });
                    }
                }
            }

            foreach (var partnership in partnerships.Where(item => item.AToB + item.BToA >= 2))
            {
                patterns.Add(new
                {
                    type = "mutual_assist_partnership",
                    playerA = partnership.PlayerA,
                    playerB = partnership.PlayerB,
                    playerAToPlayerB = partnership.AToB,
                    playerBToPlayerA = partnership.BToA
                });
            }

            foreach (var streak in FindLossStreaks(matches).Where(item => item.Value >= 3))
            {
                patterns.Add(new
                {
                    type = "loss_streak",
                    team = streak.Key,
                    consecutiveLosses = streak.Value,
                    finalPosition = standings
                        .Select((entry, index) => new { entry.Team, Position = index + 1 })
                        .Where(entry => string.Equals(
                            entry.Team,
                            streak.Key,
                            StringComparison.OrdinalIgnoreCase))
                        .Select(entry => (int?)entry.Position)
                        .FirstOrDefault()
                });
            }

            return patterns;
        }

        private static Dictionary<string, int> FindLossStreaks(IEnumerable<JObject> matches)
        {
            var current = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var longest = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var match in matches)
            {
                var teamA = GetValue(match, "teamA") as JObject;
                var teamB = GetValue(match, "teamB") as JObject;
                UpdateLossStreak(current, longest, teamA, teamB);
                UpdateLossStreak(current, longest, teamB, teamA);
            }

            return longest;
        }

        private static void UpdateLossStreak(
            IDictionary<string, int> current,
            IDictionary<string, int> longest,
            JObject team,
            JObject opponent)
        {
            var name = GetName(GetValue(team, "color")) ?? GetName(GetValue(team, "name"));
            int teamScore;
            int opponentScore;
            if (string.IsNullOrWhiteSpace(name)
                || !TryGetInt(team, out teamScore, "score", "goals")
                || !TryGetInt(opponent, out opponentScore, "score", "goals"))
            {
                return;
            }

            current[name] = teamScore < opponentScore
                ? (current.ContainsKey(name) ? current[name] : 0) + 1
                : 0;
            longest[name] = Math.Max(
                longest.ContainsKey(name) ? longest[name] : 0,
                current[name]);
        }

        private static List<object> FindLeaders(IDictionary<string, int> leaderboard)
        {
            if (leaderboard.Count == 0)
            {
                return new List<object>();
            }

            var maximum = leaderboard.Values.Max();
            return leaderboard
                .Where(entry => entry.Value == maximum)
                .Select(entry => (object)new { player = entry.Key, total = entry.Value })
                .ToList();
        }

        private static List<string> CreateLimitations(
            ICollection<JObject> matches,
            ICollection<StandingFact> standings,
            ICollection<KeyValuePair<string, int>> scorers,
            ICollection<KeyValuePair<string, int>> assisters,
            ICollection<string> players)
        {
            var limitations = new List<string>();
            if (matches.Count == 0)
            {
                limitations.Add("No parseable individual matches were supplied.");
            }

            if (standings.Count == 0)
            {
                limitations.Add("No parseable final standings were supplied.");
            }

            if (scorers.Count == 0)
            {
                limitations.Add("No topScorers aggregate was supplied.");
            }

            if (assisters.Count == 0)
            {
                limitations.Add("No topAssists aggregate was supplied.");
            }

            if (players.Count == 0)
            {
                limitations.Add("No player roster was supplied.");
            }

            return limitations;
        }

        private static JToken GetValue(JObject value, params string[] names)
        {
            if (value == null)
            {
                return null;
            }

            foreach (var name in names)
            {
                var property = value.Properties().FirstOrDefault(item => string.Equals(
                    item.Name,
                    name,
                    StringComparison.OrdinalIgnoreCase));
                if (property != null)
                {
                    return property.Value;
                }
            }

            return null;
        }

        private static IEnumerable<JToken> GetTokens(JToken source)
        {
            yield return source;

            var container = source as JContainer;
            if (container == null)
            {
                yield break;
            }

            foreach (var descendant in container.Descendants())
            {
                yield return descendant;
            }
        }

        private static IEnumerable<JToken> GetDataTokens(
            JToken source,
            IEnumerable<JObject> matches)
        {
            foreach (var token in GetTokens(source))
            {
                yield return token;
            }

            foreach (var match in matches)
            {
                foreach (var token in GetTokens(match))
                {
                    yield return token;
                }
            }
        }

        private static string GetName(JToken value)
        {
            if (value == null || value.Type == JTokenType.Null)
            {
                return null;
            }

            if (value.Type == JTokenType.String)
            {
                return value.Value<string>()?.Trim();
            }

            var item = value as JObject;
            return item == null
                ? null
                : GetName(GetValue(item, "name", "playerName", "displayName"));
        }

        private static int GetInt(JObject value, params string[] names)
        {
            int result;
            return TryGetInt(value, out result, names) ? result : 0;
        }

        private static bool TryGetInt(JObject value, out int result, params string[] names)
        {
            result = 0;
            var token = GetValue(value, names);
            return token != null && int.TryParse(token.ToString(), out result);
        }

        private static int GetValue(IDictionary<string, int> values, string key)
        {
            int value;
            return values.TryGetValue(key, out value) ? value : 0;
        }

        private static string FormatCount(int count, string singular, string plural)
        {
            return count + " " + (count == 1 ? singular : plural);
        }

        private static bool HasAnyProperty(JObject value, params string[] names)
        {
            return names.Any(name => GetValue(value, name) != null);
        }

        private sealed class StandingFact
        {
            public string Team { get; set; }
            public int Wins { get; set; }
            public int Draws { get; set; }
            public int Losses { get; set; }
            public int GoalsFor { get; set; }
            public int GoalsAgainst { get; set; }
            public int Points { get; set; }
        }

        private sealed class PartnershipFact
        {
            public string PlayerA { get; set; }
            public string PlayerB { get; set; }
            public int AToB { get; set; }
            public int BToA { get; set; }
        }

        private sealed class PlayerRatingFact
        {
            [JsonProperty("name")]
            public string Name { get; set; }

            [JsonProperty("team", NullValueHandling = NullValueHandling.Ignore)]
            public string Team { get; set; }

            [JsonProperty("goals")]
            public int Goals { get; set; }

            [JsonProperty("assists")]
            public int Assists { get; set; }

            [JsonProperty("rating")]
            public double Rating { get; set; }

            [JsonProperty("ratingFactors")]
            public IList<string> RatingFactors { get; set; }
        }
    }
}
