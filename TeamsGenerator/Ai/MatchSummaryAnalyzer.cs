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
            var ownGoals = FindOwnGoals(matches, playerTeams);
            var unexpectedContributors = FindUnexpectedContributors(
                source,
                matches,
                scorers,
                assisters);

            foreach (var name in scorers.Keys.Concat(assisters.Keys))
            {
                players.Add(name);
            }

            var ratings = CreateRatings(
                players,
                scorers,
                assisters,
                standings,
                playerTeams);
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
                    partnerships,
                    ownGoals,
                    unexpectedContributors),
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
                    BToA = directed.ContainsKey(reverseKey) ? directed[reverseKey] : 0
                });
            }

            return partnerships;
        }

        private static List<OwnGoalFact> FindOwnGoals(
            IEnumerable<JObject> matches,
            IDictionary<string, string> playerTeams)
        {
            var ownGoals = new List<OwnGoalFact>();

            foreach (var match in matches)
            {
                foreach (var candidate in match.DescendantsAndSelf().OfType<JObject>())
                {
                    var ownGoalValue = GetValue(candidate, "ownGoal");
                    var marker = GetValue(candidate, "isOwnGoal", "ownGoal", "isOwn");
                    var eventType = GetValue(candidate, "type", "eventType", "goalType");
                    var ownGoalPlayerName = ownGoalValue != null
                        && ownGoalValue.Type == JTokenType.String
                        && !IsTruthyText(ownGoalValue.Value<string>())
                        && !IsExplicitFalseText(ownGoalValue.Value<string>());
                    if (!IsTruthy(marker)
                        && !IsOwnGoalType(eventType)
                        && !ownGoalPlayerName)
                    {
                        continue;
                    }

                    var player = GetName(GetValue(
                        candidate,
                        "scorer",
                        "goalScorer",
                        "scoredBy"));
                    if (string.IsNullOrWhiteSpace(player)
                        && ownGoalPlayerName)
                    {
                        player = ownGoalValue.Value<string>()?.Trim();
                    }

                    string team = null;
                    if (!string.IsNullOrWhiteSpace(player))
                    {
                        playerTeams.TryGetValue(player, out team);
                    }

                    team = team
                        ?? GetName(GetValue(
                            candidate,
                            "ownGoalTeam",
                            "concedingTeam",
                            "scorerTeam",
                            "team"));

                    ownGoals.Add(new OwnGoalFact
                    {
                        Player = player,
                        Team = team
                    });
                }
            }

            return ownGoals;
        }

        private static HashSet<string> FindUnexpectedContributors(
            JToken source,
            IEnumerable<JObject> matches,
            IDictionary<string, int> scorers,
            IDictionary<string, int> assisters)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var skillNames = new[] { "attack", "defence", "stamina", "leadership", "passing" };

            foreach (var player in GetDataTokens(source, matches).OfType<JObject>())
            {
                var name = GetName(player);
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var skills = skillNames
                    .Select(skill => GetValue(player, skill))
                    .Where(value => value != null)
                    .Select(value =>
                    {
                        double parsed;
                        return double.TryParse(value.ToString(), out parsed)
                            ? (double?)parsed
                            : null;
                    })
                    .Where(value => value.HasValue)
                    .Select(value => value.Value)
                    .ToList();

                var contribution = GetValue(scorers, name) + GetValue(assisters, name);
                var hasMostlyBelowAverageSkills = skills.Count >= 3
                    && skills.Count(skill => skill < 5) > skills.Count / 2;

                if (hasMostlyBelowAverageSkills && contribution >= 2)
                {
                    result.Add(name);
                }
            }

            return result;
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
            IEnumerable<PartnershipFact> partnerships,
            IEnumerable<OwnGoalFact> ownGoals,
            IEnumerable<string> unexpectedContributors)
        {
            var patterns = new List<object>();
            var maxGoals = scorers.Count == 0 ? 0 : scorers.Values.Max();
            var maxAssists = assisters.Count == 0 ? 0 : assisters.Values.Max();
            var ownGoalList = ownGoals.ToList();

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

            foreach (var partnership in partnerships.Where(item =>
                item.AToB > 0
                && item.BToA > 0
                && item.AToB + item.BToA >= 2))
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

            foreach (var player in unexpectedContributors)
            {
                patterns.Add(new
                {
                    type = "unexpected_contributor",
                    player,
                    goals = GetValue(scorers, player),
                    assists = GetValue(assisters, player)
                });
            }

            if (ownGoalList.Count >= 3)
            {
                patterns.Add(new
                {
                    type = "own_goal_festival",
                    totalOwnGoals = ownGoalList.Count
                });
            }

            foreach (var team in ownGoalList
                .Where(item => !string.IsNullOrWhiteSpace(item.Team))
                .GroupBy(item => item.Team, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() >= 2))
            {
                patterns.Add(new
                {
                    type = "team_own_goal_total",
                    team = team.Key,
                    ownGoals = team.Count()
                });
            }

            foreach (var player in ownGoalList
                .Where(item => !string.IsNullOrWhiteSpace(item.Player))
                .GroupBy(item => item.Player, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() >= 2))
            {
                patterns.Add(new
                {
                    type = "repeat_own_goal",
                    player = player.Key,
                    ownGoals = player.Count()
                });
            }

            if (maxGoals > 0 && maxAssists > 0)
            {
                foreach (var player in scorers
                    .Where(entry => entry.Value == maxGoals)
                    .Select(entry => entry.Key)
                    .Intersect(
                        assisters
                            .Where(entry => entry.Value == maxAssists)
                            .Select(entry => entry.Key),
                        StringComparer.OrdinalIgnoreCase))
                {
                    patterns.Add(new
                    {
                        type = "double_crown",
                        player,
                        goals = maxGoals,
                        assists = maxAssists
                    });
                }
            }

            foreach (var player in scorers.Where(entry => entry.Value >= 2))
            {
                var assists = GetValue(assisters, player.Key);
                if (assists >= 2)
                {
                    patterns.Add(new
                    {
                        type = "all_round_attacker",
                        player = player.Key,
                        goals = player.Value,
                        assists
                    });
                }
            }

            if (standings.Count > 1)
            {
                var highestGoalsFor = standings.Max(team => team.GoalsFor);
                var highestGoalsAgainst = standings.Max(team => team.GoalsAgainst);
                foreach (var team in standings.Where(team =>
                    team.GoalsFor == highestGoalsFor
                    && team.GoalsAgainst == highestGoalsAgainst
                    && highestGoalsFor > 0
                    && highestGoalsAgainst > 0))
                {
                    patterns.Add(new
                    {
                        type = "all_action_team",
                        team = team.Team,
                        goalsFor = team.GoalsFor,
                        goalsAgainst = team.GoalsAgainst
                    });
                }

                var lastTeam = standings.Last();
                if (lastTeam.GoalsFor == highestGoalsFor && highestGoalsFor > 0)
                {
                    patterns.Add(new
                    {
                        type = "highest_scoring_last_place_team",
                        team = lastTeam.Team,
                        goalsFor = lastTeam.GoalsFor
                    });
                }
            }

            AddPlayerDependencyPatterns(patterns, standings, scorers, assisters, playerTeams);
            AddTeamEffortPatterns(patterns, scorers, playerTeams);
            AddPowerDuoPatterns(patterns, partnerships, scorers, assisters);
            AddMatchStoryPatterns(patterns, matches, standings, playerTeams);
            AddTablePatterns(patterns, standings);
            AddDefensiveEveningPattern(patterns, matches);
            AddResiliencePatterns(patterns, matches, standings);
            AddRunPatterns(patterns, standings);

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

        private static void AddPlayerDependencyPatterns(
            ICollection<object> patterns,
            IEnumerable<StandingFact> standings,
            IDictionary<string, int> scorers,
            IDictionary<string, int> assisters,
            IDictionary<string, string> playerTeams)
        {
            var goalsByTeam = standings.ToDictionary(
                team => team.Team,
                team => team.GoalsFor,
                StringComparer.OrdinalIgnoreCase);

            foreach (var player in scorers.Keys
                .Concat(assisters.Keys)
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string team;
                int teamGoals;
                if (!playerTeams.TryGetValue(player, out team)
                    || !goalsByTeam.TryGetValue(team, out teamGoals)
                    || teamGoals < 2)
                {
                    continue;
                }

                var goals = GetValue(scorers, player);
                var assists = GetValue(assisters, player);
                var contributions = goals + assists;
                if (contributions > teamGoals * 0.5)
                {
                    patterns.Add(new
                    {
                        type = "one_player_dependency",
                        player,
                        team,
                        goals,
                        assists,
                        teamGoals,
                        contributionShare = Math.Round((double)contributions / teamGoals, 2)
                    });
                }
            }
        }

        private static void AddTeamEffortPatterns(
            ICollection<object> patterns,
            IDictionary<string, int> scorers,
            IDictionary<string, string> playerTeams)
        {
            foreach (var team in scorers
                .Where(player => player.Value > 0 && playerTeams.ContainsKey(player.Key))
                .GroupBy(
                    player => playerTeams[player.Key],
                    StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() >= 4))
            {
                patterns.Add(new
                {
                    type = "team_scoring_effort",
                    team = team.Key,
                    differentScorers = team.Count()
                });
            }
        }

        private static void AddPowerDuoPatterns(
            ICollection<object> patterns,
            IEnumerable<PartnershipFact> partnerships,
            IDictionary<string, int> scorers,
            IDictionary<string, int> assisters)
        {
            foreach (var partnership in partnerships)
            {
                var directCombinations = partnership.AToB + partnership.BToA;
                var combinedContributions =
                    GetValue(scorers, partnership.PlayerA)
                    + GetValue(assisters, partnership.PlayerA)
                    + GetValue(scorers, partnership.PlayerB)
                    + GetValue(assisters, partnership.PlayerB);

                if (directCombinations >= 3
                    || (directCombinations >= 2 && combinedContributions >= 6))
                {
                    patterns.Add(new
                    {
                        type = "power_duo",
                        playerA = partnership.PlayerA,
                        playerB = partnership.PlayerB,
                        directGoalCombinations = directCombinations,
                        combinedGoalsAndAssists = combinedContributions
                    });
                }
            }
        }

        private static void AddMatchStoryPatterns(
            ICollection<object> patterns,
            IList<JObject> matches,
            IList<StandingFact> standings,
            IDictionary<string, string> playerTeams)
        {
            var winningGoals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var match in matches)
            {
                MatchScoreFact score;
                if (!TryGetMatchScore(match, out score) || score.TeamAScore == score.TeamBScore)
                {
                    continue;
                }

                var events = FindOrderedGoalEvents(match, score, playerTeams);
                if (!GoalEventsMatchFinalScore(events, score))
                {
                    continue;
                }

                var winner = score.TeamAScore > score.TeamBScore
                    ? score.TeamA
                    : score.TeamB;
                var loserScore = score.TeamAScore > score.TeamBScore
                    ? score.TeamBScore
                    : score.TeamAScore;
                var winnerGoals = 0;
                var runningA = 0;
                var runningB = 0;
                var winnerWasBehind = false;

                foreach (var goalEvent in events)
                {
                    if (string.Equals(goalEvent.Team, score.TeamA, StringComparison.OrdinalIgnoreCase))
                    {
                        runningA++;
                    }
                    else
                    {
                        runningB++;
                    }

                    winnerWasBehind = winnerWasBehind
                        || (string.Equals(winner, score.TeamA, StringComparison.OrdinalIgnoreCase)
                            ? runningA < runningB
                            : runningB < runningA);

                    if (string.Equals(goalEvent.Team, winner, StringComparison.OrdinalIgnoreCase))
                    {
                        winnerGoals++;
                        if (winnerGoals == loserScore + 1
                            && !string.IsNullOrWhiteSpace(goalEvent.Scorer))
                        {
                            winningGoals[goalEvent.Scorer] =
                                winningGoals.ContainsKey(goalEvent.Scorer)
                                    ? winningGoals[goalEvent.Scorer] + 1
                                    : 1;
                        }
                    }
                }

                if (winnerWasBehind)
                {
                    patterns.Add(new
                    {
                        type = "comeback_win",
                        team = winner,
                        opponent = string.Equals(
                            winner,
                            score.TeamA,
                            StringComparison.OrdinalIgnoreCase)
                            ? score.TeamB
                            : score.TeamA,
                        finalScore = score.TeamAScore + "-" + score.TeamBScore
                    });
                }

                var finalGoal = events.Last();
                var beforeFinalA = score.TeamAScore
                    - (string.Equals(finalGoal.Team, score.TeamA, StringComparison.OrdinalIgnoreCase) ? 1 : 0);
                var beforeFinalB = score.TeamBScore
                    - (string.Equals(finalGoal.Team, score.TeamB, StringComparison.OrdinalIgnoreCase) ? 1 : 0);
                if (beforeFinalA == beforeFinalB
                    && string.Equals(finalGoal.Team, winner, StringComparison.OrdinalIgnoreCase))
                {
                    patterns.Add(new
                    {
                        type = "late_winner",
                        team = winner,
                        scorer = finalGoal.Scorer,
                        finalScore = score.TeamAScore + "-" + score.TeamBScore
                    });
                }
            }

            if (winningGoals.Count > 0)
            {
                var maximum = winningGoals.Values.Max();
                foreach (var player in winningGoals.Where(entry => entry.Value == maximum))
                {
                    patterns.Add(new
                    {
                        type = "clutch_player",
                        player = player.Key,
                        matchWinningGoals = player.Value
                    });
                }
            }

            AddLastMatchLeadChangePattern(patterns, matches, standings);
        }

        private static void AddLastMatchLeadChangePattern(
            ICollection<object> patterns,
            IList<JObject> matches,
            IList<StandingFact> standings)
        {
            if (matches.Count == 0 || standings.Count < 2)
            {
                return;
            }

            MatchScoreFact score;
            if (!TryGetMatchScore(matches.Last(), out score)
                || score.TeamAScore == score.TeamBScore)
            {
                return;
            }

            var winner = score.TeamAScore > score.TeamBScore ? score.TeamA : score.TeamB;
            if (!string.Equals(
                standings.First().Team,
                winner,
                StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var previousStandings = standings.Select(team => new StandingFact
            {
                Team = team.Team,
                Wins = team.Wins,
                Draws = team.Draws,
                Losses = team.Losses,
                GoalsFor = team.GoalsFor,
                GoalsAgainst = team.GoalsAgainst,
                Points = team.Points
            }).ToList();

            var teamA = previousStandings.FirstOrDefault(team => string.Equals(
                team.Team,
                score.TeamA,
                StringComparison.OrdinalIgnoreCase));
            var teamB = previousStandings.FirstOrDefault(team => string.Equals(
                team.Team,
                score.TeamB,
                StringComparison.OrdinalIgnoreCase));
            if (teamA == null || teamB == null)
            {
                return;
            }

            teamA.GoalsFor -= score.TeamAScore;
            teamA.GoalsAgainst -= score.TeamBScore;
            teamB.GoalsFor -= score.TeamBScore;
            teamB.GoalsAgainst -= score.TeamAScore;

            if (score.TeamAScore > score.TeamBScore)
            {
                teamA.Wins--;
                teamA.Points -= 3;
                teamB.Losses--;
            }
            else
            {
                teamB.Wins--;
                teamB.Points -= 3;
                teamA.Losses--;
            }

            var previousLeader = previousStandings
                .OrderByDescending(team => team.Points)
                .ThenByDescending(team => team.GoalsFor - team.GoalsAgainst)
                .ThenByDescending(team => team.GoalsFor)
                .First()
                .Team;

            if (!string.Equals(previousLeader, winner, StringComparison.OrdinalIgnoreCase))
            {
                patterns.Add(new
                {
                    type = "final_match_changed_leader",
                    team = winner,
                    previousLeader
                });
            }
        }

        private static void AddTablePatterns(
            ICollection<object> patterns,
            IList<StandingFact> standings)
        {
            if (standings.Count < 2)
            {
                return;
            }

            var gap = standings[0].Points - standings[1].Points;
            if (gap <= 1)
            {
                patterns.Add(new
                {
                    type = "tight_table",
                    first = standings[0].Team,
                    second = standings[1].Team,
                    pointsGap = gap,
                    tiedOnPoints = gap == 0
                });
            }

            var highestGoalsFor = standings.Max(team => team.GoalsFor);
            var lowPositionStart = (standings.Count + 1) / 2 + 1;
            foreach (var entry in standings
                .Select((team, index) => new { Team = team, Position = index + 1 })
                .Where(entry =>
                    entry.Position >= lowPositionStart
                    && entry.Team.GoalsFor == highestGoalsFor
                    && highestGoalsFor > 0))
            {
                patterns.Add(new
                {
                    type = "attack_without_reward",
                    team = entry.Team.Team,
                    goalsFor = entry.Team.GoalsFor,
                    finalPosition = entry.Position
                });
            }
        }

        private static void AddDefensiveEveningPattern(
            ICollection<object> patterns,
            IEnumerable<JObject> matches)
        {
            var parsedMatches = new List<MatchScoreFact>();
            foreach (var match in matches)
            {
                MatchScoreFact score;
                if (TryGetMatchScore(match, out score))
                {
                    parsedMatches.Add(score);
                }
            }

            if (parsedMatches.Count < 3)
            {
                return;
            }

            var totalGoals = parsedMatches.Sum(match => match.TeamAScore + match.TeamBScore);
            var cleanSheets = parsedMatches.Sum(match =>
                (match.TeamAScore == 0 ? 1 : 0) + (match.TeamBScore == 0 ? 1 : 0));
            var average = (double)totalGoals / parsedMatches.Count;
            if (average <= 2 || cleanSheets >= 3)
            {
                patterns.Add(new
                {
                    type = "defensive_evening",
                    matches = parsedMatches.Count,
                    totalGoals,
                    goalsPerMatch = Math.Round(average, 2),
                    cleanSheets
                });
            }
        }

        private static void AddResiliencePatterns(
            ICollection<object> patterns,
            IEnumerable<JObject> matches,
            IList<StandingFact> standings)
        {
            var outcomes = new Dictionary<string, List<char>>(StringComparer.OrdinalIgnoreCase);
            foreach (var match in matches)
            {
                MatchScoreFact score;
                if (!TryGetMatchScore(match, out score))
                {
                    continue;
                }

                AddOutcome(outcomes, score.TeamA, score.TeamAScore, score.TeamBScore);
                AddOutcome(outcomes, score.TeamB, score.TeamBScore, score.TeamAScore);
            }

            foreach (var entry in standings
                .Select((team, index) => new { Team = team.Team, Position = index + 1 })
                .Where(entry => entry.Position <= 2))
            {
                List<char> teamOutcomes;
                if (!outcomes.TryGetValue(entry.Team, out teamOutcomes))
                {
                    continue;
                }

                var streak = 0;
                var recovered = false;
                foreach (var outcome in teamOutcomes)
                {
                    if (outcome == 'L')
                    {
                        streak++;
                    }
                    else
                    {
                        recovered = recovered || (streak >= 2 && outcome == 'W');
                        streak = 0;
                    }
                }

                if (recovered)
                {
                    patterns.Add(new
                    {
                        type = "resilient_finish",
                        team = entry.Team,
                        finalPosition = entry.Position
                    });
                }
            }
        }

        private static void AddRunPatterns(
            ICollection<object> patterns,
            IEnumerable<StandingFact> standings)
        {
            foreach (var team in standings.Where(team =>
                team.Wins + team.Draws + team.Losses >= 2))
            {
                if (team.Losses == 0 && team.Draws == 0)
                {
                    patterns.Add(new
                    {
                        type = "perfect_run",
                        team = team.Team,
                        wins = team.Wins
                    });
                }
                else if (team.Losses == 0)
                {
                    patterns.Add(new
                    {
                        type = "undefeated_run",
                        team = team.Team,
                        wins = team.Wins,
                        draws = team.Draws
                    });
                }

                if (team.Wins == 0)
                {
                    patterns.Add(new
                    {
                        type = "winless_run",
                        team = team.Team,
                        draws = team.Draws,
                        losses = team.Losses
                    });
                }
            }
        }

        private static bool TryGetMatchScore(JObject match, out MatchScoreFact score)
        {
            score = null;
            var teamA = GetValue(match, "teamA") as JObject;
            var teamB = GetValue(match, "teamB") as JObject;
            int teamAScore;
            int teamBScore;
            var teamAName = GetTeamName(teamA);
            var teamBName = GetTeamName(teamB);
            if (string.IsNullOrWhiteSpace(teamAName)
                || string.IsNullOrWhiteSpace(teamBName)
                || !TryGetInt(teamA, out teamAScore, "score", "goals")
                || !TryGetInt(teamB, out teamBScore, "score", "goals"))
            {
                return false;
            }

            score = new MatchScoreFact
            {
                TeamA = teamAName,
                TeamB = teamBName,
                TeamAScore = teamAScore,
                TeamBScore = teamBScore
            };
            return true;
        }

        private static List<GoalEventFact> FindOrderedGoalEvents(
            JObject match,
            MatchScoreFact score,
            IDictionary<string, string> playerTeams)
        {
            var eventArray = GetTokens(match)
                .OfType<JProperty>()
                .Where(property =>
                    string.Equals(property.Name, "goals", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(property.Name, "goalEvents", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(property.Name, "scoringEvents", StringComparison.OrdinalIgnoreCase))
                .Select(property => property.Value as JArray)
                .Where(array => array != null)
                .OrderByDescending(array => array.Count)
                .FirstOrDefault();

            if (eventArray == null)
            {
                return new List<GoalEventFact>();
            }

            var events = new List<GoalEventFact>();
            foreach (var item in eventArray.OfType<JObject>())
            {
                var scorer = GetName(GetValue(item, "scorer", "goalScorer", "scoredBy"));
                var explicitTeam = GetName(GetValue(
                    item,
                    "scoringTeam",
                    "teamColor",
                    "team"));
                string playerTeam = null;
                if (!string.IsNullOrWhiteSpace(scorer))
                {
                    playerTeams.TryGetValue(scorer, out playerTeam);
                }

                var team = explicitTeam ?? playerTeam;
                var ownGoal = IsGoalEventOwnGoal(item);
                if (ownGoal && !string.IsNullOrWhiteSpace(playerTeam))
                {
                    team = string.Equals(playerTeam, score.TeamA, StringComparison.OrdinalIgnoreCase)
                        ? score.TeamB
                        : score.TeamA;
                }

                if (!string.Equals(team, score.TeamA, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(team, score.TeamB, StringComparison.OrdinalIgnoreCase))
                {
                    return new List<GoalEventFact>();
                }

                events.Add(new GoalEventFact
                {
                    Team = team,
                    Scorer = scorer
                });
            }

            return events;
        }

        private static bool GoalEventsMatchFinalScore(
            IEnumerable<GoalEventFact> events,
            MatchScoreFact score)
        {
            var eventList = events.ToList();
            return eventList.Count == score.TeamAScore + score.TeamBScore
                && eventList.Count(goal => string.Equals(
                    goal.Team,
                    score.TeamA,
                    StringComparison.OrdinalIgnoreCase)) == score.TeamAScore
                && eventList.Count(goal => string.Equals(
                    goal.Team,
                    score.TeamB,
                    StringComparison.OrdinalIgnoreCase)) == score.TeamBScore;
        }

        private static bool IsGoalEventOwnGoal(JObject goalEvent)
        {
            var marker = GetValue(goalEvent, "isOwnGoal", "ownGoal", "isOwn");
            return IsTruthy(marker)
                || IsOwnGoalType(GetValue(goalEvent, "type", "eventType", "goalType"))
                || (marker != null
                    && marker.Type == JTokenType.String
                    && !IsTruthyText(marker.Value<string>())
                    && !IsExplicitFalseText(marker.Value<string>()));
        }

        private static string GetTeamName(JObject team)
        {
            return GetName(GetValue(team, "color")) ?? GetName(GetValue(team, "name"));
        }

        private static void AddOutcome(
            IDictionary<string, List<char>> outcomes,
            string team,
            int teamScore,
            int opponentScore)
        {
            List<char> teamOutcomes;
            if (!outcomes.TryGetValue(team, out teamOutcomes))
            {
                teamOutcomes = new List<char>();
                outcomes[team] = teamOutcomes;
            }

            teamOutcomes.Add(teamScore > opponentScore
                ? 'W'
                : teamScore < opponentScore ? 'L' : 'D');
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

        private static bool IsTruthy(JToken value)
        {
            if (value == null || value.Type == JTokenType.Null)
            {
                return false;
            }

            if (value.Type == JTokenType.Boolean)
            {
                return value.Value<bool>();
            }

            if (value.Type == JTokenType.Integer)
            {
                return value.Value<int>() == 1;
            }

            return value.Type == JTokenType.String
                && IsTruthyText(value.Value<string>());
        }

        private static bool IsTruthyText(string value)
        {
            var normalized = (value ?? string.Empty)
                .Replace("_", string.Empty)
                .Replace("-", string.Empty)
                .Replace(" ", string.Empty);
            return string.Equals(normalized, "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "yes", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "owngoal", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsExplicitFalseText(string value)
        {
            var normalized = (value ?? string.Empty).Trim();
            return string.Equals(normalized, "false", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "no", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "0", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsOwnGoalType(JToken value)
        {
            return value != null
                && value.Type == JTokenType.String
                && string.Equals(
                    value.Value<string>()
                        ?.Replace("_", string.Empty)
                        .Replace("-", string.Empty)
                        .Replace(" ", string.Empty),
                    "owngoal",
                    StringComparison.OrdinalIgnoreCase);
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

        private sealed class OwnGoalFact
        {
            public string Player { get; set; }
            public string Team { get; set; }
        }

        private sealed class MatchScoreFact
        {
            public string TeamA { get; set; }
            public string TeamB { get; set; }
            public int TeamAScore { get; set; }
            public int TeamBScore { get; set; }
        }

        private sealed class GoalEventFact
        {
            public string Team { get; set; }
            public string Scorer { get; set; }
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
