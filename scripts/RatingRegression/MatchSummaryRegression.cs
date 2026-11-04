using System.Reflection;
using Newtonsoft.Json.Linq;
using TeamsGenerator.Ai;

internal static class MatchSummaryRegression
{
    private static readonly Assembly Assembly = typeof(OpenAiService).Assembly;

    internal static void Run(Action<bool, string> check)
    {
        foreach (var language in new[] { "en", "he" })
        {
            var input = Input(Match("Green", "Blue", 0, 0));
            var facts = Facts(input, language);
            var standings = (JArray)facts["standings"]!;
            var players = (JArray)facts["players"]!;
            check(standings.All(team => team.Value<int>("position") == 1 && team.Value<bool>("jointPosition")),
                $"{language}: 0-0 standings share first place on every tiebreaker");
            check(players.Count == 10 && players.All(player => player.Value<double>("rating") == 5.0),
                $"{language}: all ten players receive the same neutral baseline, not 5.6 versus 4.8");
            check(players.All(player => player.Value<int>("goals") == 0 && player.Value<int>("assists") == 0
                && !player["ratingFactors"]!.ToString().Contains("team finished")),
                $"{language}: no unearned first-place bonus or last-place penalty");
            check(!facts["awards"]!["topScorers"]!.Any() && !facts["awards"]!["topAssisters"]!.Any(),
                $"{language}: a scoreless draw has no scoring or assisting award");
            check(facts.Value<int>("recordedMatchCount") == 1 && facts["matchResults"]!.Count() == 1
                && facts["matchResults"]![0]!.Value<int>("goals") == 0 && facts["matchResults"]![0]!.Value<int>("opponentGoals") == 0,
                $"{language}: narrative receives the actual match count and 0-0 result");
            var lead = facts["verifiedPatterns"]!.Single(pattern => pattern.Value<string>("type") == "shared_table_lead");
            check(lead["leaders"]!.Count() == 2 && lead.Value<int>("points") == 1,
                $"{language}: both joint leaders replace the false first-versus-second story");
            if (language == "he")
                check(lead["leaders"]!.All(team => team.Value<string>("team")!.StartsWith("הקבוצה")),
                    "joint leader names and match result teams retain Hebrew localization");

            var stats = (JObject)input["tableStats"]!["stats"]!;
            input["tableStats"]!["stats"] = new JObject(stats.Properties().Reverse().Select(property => new JProperty(property.Name, property.Value.DeepClone())));
            var reversed = Facts(input, language);
            check(JToken.DeepEquals(players, reversed["players"]), $"{language}: player ratings are invariant to table insertion order");
            check(((JArray)reversed["standings"]!).All(team => team.Value<int>("position") == 1),
                $"{language}: changing row order cannot create a winner");
        }

        var tiedSecond = Facts(WithStandings(
            ("Green", 7, 3, 1), ("Blue", 3, 2, 2), ("Red", 3, 2, 2)), "en");
        CheckPositions(check, tiedSecond, 1, 2, 2);
        var secondPlayers = tiedSecond["players"]!.Where(player => player.Value<string>("team") != "Green").ToArray();
        check(secondPlayers.Select(player => player.Value<double>("rating")).Distinct().Count() == 1
            && secondPlayers.All(player => player["ratingFactors"]!.ToString().Contains("joint second")),
            "shared second gives both teams identical adjustments");

        var tiedLast = Facts(WithStandings(
            ("Green", 9, 5, 0), ("Blue", 6, 3, 1), ("Red", 1, 0, 2), ("Yellow", 1, 0, 2)), "en");
        CheckPositions(check, tiedLast, 1, 2, 3, 3);
        var lastPlayers = tiedLast["players"]!.Where(player => player.Value<string>("team") is "Red" or "Yellow");
        check(lastPlayers.All(player => player.Value<double>("rating") == 4.8
            && player["ratingFactors"]!.ToString().Contains("joint last")), "shared last is consistent despite competition-rank gaps");

        var tiedFirst = Facts(WithStandings(
            ("Green", 6, 4, 1), ("Blue", 6, 4, 1), ("Red", 0, 0, 6)), "en");
        CheckPositions(check, tiedFirst, 1, 1, 3);
        check(tiedFirst["players"]!.Where(player => player.Value<string>("team") is "Green" or "Blue")
            .All(player => player.Value<double>("rating") == 5.6), "joint leaders above a lower-ranked team share the same bonus");

        CheckPositions(check, Facts(WithStandings(("Green", 3, 3, 1), ("Blue", 3, 1, 1)), "en"), 1, 2);
        CheckPositions(check, Facts(WithStandings(("Green", 3, 3, 2), ("Blue", 3, 2, 1)), "en"), 1, 2);
        check(Facts(WithStandings(("Green", 3, 3, 1), ("Blue", 3, 1, 1)), "en")["players"]!
            .Where(player => player.Value<string>("team") == "Green")
            .All(player => player.Value<double>("rating") == 5.6), "genuine tiebreaker winner keeps existing placement bonus");

        var scoringDraw = Facts(Input(Match("Green", "Blue", 1, 1)), "en");
        var scorers = scoringDraw["players"]!.Where(player => player.Value<int>("goals") > 0).ToArray();
        check(scorers.Length == 2 && scorers.Select(player => player.Value<double>("rating")).Distinct().Count() == 1,
            "scoring draw gives equal scorers equal ratings while retaining goal credit");
        check(scorers.All(player => player.Value<double>("rating") > 5), "scoring contributions remain rewarded");

        var finalTie = Facts(Input(Match("Green", "Blue", 1, 0), Match("Green", "Blue", 0, 1)), "en");
        CheckPositions(check, finalTie, 1, 1);
        check(!finalTie["verifiedPatterns"]!.Any(pattern => pattern.Value<string>("type") == "final_match_changed_leader"),
            "final win into a shared lead is not a sole-leader change");
        var repeatedDraws = Facts(Input(Match("Green", "Blue", 0, 0), Match("Green", "Blue", 0, 0)), "en");
        check(repeatedDraws.Value<int>("recordedMatchCount") == 2
            && repeatedDraws["standings"]!.All(team => team.Value<int>("position") == 1 && team.Value<int>("points") == 2)
            && repeatedDraws["players"]!.All(player => player.Value<double>("rating") == 5),
            "multiple scoreless matches retain shared ranks, equal neutral ratings, and correct totals");
        var previousTie = Facts(Input(Match("Green", "Blue", 0, 0), Match("Green", "Blue", 0, 1)), "en");
        check(!previousTie["verifiedPatterns"]!.Any(pattern => pattern.Value<string>("type") == "final_match_changed_leader"),
            "a previous tied table cannot supply an arbitrary displaced leader");
        var trueLeadChange = Facts(Input(Match("Green", "Blue", 1, 0), Match("Green", "Blue", 0, 2)), "en");
        check(trueLeadChange["verifiedPatterns"]!.Any(pattern => pattern.Value<string>("type") == "final_match_changed_leader"),
            "a verified final-match lead change is still reported: " + trueLeadChange);

        var prompt = (string)Invoke("MatchSummaryPrompt", "Create", "he");
        check(prompt.Contains("few chances") && prompt.Contains("Identical recorded factors")
            && prompt.Contains("shared sporting rank") && prompt.Contains("one 0-0 draw"),
            "prompt forbids scoreline-based tactical inventions and arbitrary tie/rating distinctions");

        CheckContributionRatings(check);
        CheckGoalkeeperRatings(check);
        CheckOwnGoalAttribution(check);
    }

    private static void CheckOwnGoalAttribution(Action<bool, string> check)
    {
        JObject OwnGoal(string? name) => new()
        {
            ["scorer"] = new JObject { ["key"] = name, ["name"] = name ?? "Own Goal", ["ownGoal"] = true },
            ["assist"] = "Green2"
        };
        var first = Match("Green", "Blue", 2, 1);
        var second = Match("Red", "Blue", 1, 0);
        first.Remove("goalEvents");
        second.Remove("goalEvents");
        first["teamA"]!["goals"] = new JArray(OwnGoal("Blue1"), OwnGoal(null));
        second["teamA"]!["goals"] = new JArray(OwnGoal("Blue1"));
        // Model both the nested marker and modern event-level marker, plus redundant saved scorers.
        second["teamA"]!["goals"]![0]!["isOwnGoal"] = true;
        foreach (var match in new[] { first, second })
            match["teamA"]!["scorers"] = new JArray(match["teamA"]!["goals"]!
                .Select(goal => goal["scorer"]!.DeepClone()));
        var input = Input(first, second);
        foreach (var language in new[] { "en", "he" })
        {
            var facts = Facts(input, language);
            var named = facts["ownGoalScorers"]!.Single();
            check(named.Value<string>("player") == "Blue1" && named.Value<int>("ownGoals") == 2,
                $"{language}: named nested own-goal markers count once per event across matches");
            var patterns = facts["verifiedPatterns"]!;
            check(patterns.Single(p => p.Value<string>("type") == "own_goal_festival").Value<int>("totalOwnGoals") == 3,
                $"{language}: anonymous own goals count toward the evening but scorer copies do not");
            check(patterns.Single(p => p.Value<string>("type") == "repeat_own_goal").Value<int>("ownGoals") == 2,
                $"{language}: repeated own goals are verified without requiring a chronological event array");
            var team = patterns.Single(p => p.Value<string>("type") == "team_own_goal_total");
            check(team.Value<string>("team") == (language == "he" ? "הקבוצה הכחולה" : "Blue")
                && team.Value<int>("ownGoals") == 3, $"{language}: totals identify the conceding team, not the beneficiary");
            var player = facts["players"]!.Single(p => p.Value<string>("name") == "Blue1");
            check(player.Value<int>("goals") == 1 && player.Value<int>("assists") == 0,
                $"{language}: a player can score both regular and own goals without mixing their totals");
            check(!facts["awards"]!["topAssisters"]!.Any()
                && facts["players"]!.All(p => p.Value<string>("name") != "Own Goal"),
                $"{language}: legacy assists on own goals earn no credit and anonymous markers are not players");
            var serialized = new JObject { ["matchesHistory"] = new JArray(
                input["matchesHistory"]!.Select(match => new JObject { ["SerializedMatch"] = match.ToString() })) };
            check(JToken.DeepEquals(facts["ownGoalScorers"], Facts(serialized, language)["ownGoalScorers"]),
                $"{language}: persisted match deserialization retains named own-goal counts");
            var anonymous = (JObject)input.DeepClone();
            foreach (var match in anonymous["matchesHistory"]!)
            foreach (var goal in match["teamA"]!["goals"]!)
                goal["scorer"]!["name"] = "Own Goal";
            check(JToken.DeepEquals(facts["players"], Facts(anonymous, language)["players"]),
                $"{language}: naming own-goal scorers does not alter regular goals, assists, awards or ratings");
            var prompt = (string)Invoke("MatchSummaryPrompt", "Create", language);
            check(prompt.Contains("ownGoalScorers") && prompt.Contains("explicitly noting repeats")
                && prompt.Contains("never regular goals or assists"),
                $"{language}: narrative includes verified names/counts separately from regular scoring");
        }

        var single = Match("Green", "Blue", 1, 0);
        single.Remove("goalEvents");
        single["teamA"]!["goals"] = new JArray(OwnGoal("Blue1"));
        check(Facts(Input(single), "en")["ownGoalScorers"]!.Single().Value<int>("ownGoals") == 1,
            "one named own goal is available to the narrative even without a repeated pattern");
        foreach (var invalid in new[] { "score", "missing", "non-object" })
        {
            var variant = (JObject)input.DeepClone();
            var team = (JObject)variant["matchesHistory"]![1]!["teamA"]!;
            if (invalid == "score") team["score"] = 2;
            if (invalid == "missing") team.Remove("goals");
            if (invalid == "non-object") team["goals"] = new JArray("not an event");
            var facts = Facts(variant, "en");
            check(!facts["ownGoalScorers"]!.Any()
                && !facts["verifiedPatterns"]!.Any(p => p.Value<string>("type")!.Contains("own_goal")),
                $"{invalid}: incomplete or invalid event data cannot produce partial evening totals");
        }

        var legacy = Match("Green", "Blue", 2, 0);
        ((JObject)legacy["teamA"]!).Remove("goals");
        ((JObject)legacy["teamB"]!).Remove("goals");
        legacy["goalEvents"] = new JArray(
            new JObject { ["team"] = "Green", ["scorer"] = "Blue1", ["isOwnGoal"] = true },
            new JObject { ["team"] = "Green", ["ownGoal"] = "Blue1" });
        check(Facts(Input(legacy), "en")["ownGoalScorers"]!.Single().Value<int>("ownGoals") == 2,
            "legacy flat events retain explicit beneficiaries, named markers and repeated attribution");

        var transferred = (JObject)input.DeepClone();
        transferred["matchesHistory"]![1]!["teamA"]!["players"]![0]!["name"] = "Blue1";
        check(Facts(transferred, "en")["verifiedPatterns"]!
            .Single(p => p.Value<string>("type") == "team_own_goal_total").Value<string>("team") == "Blue",
            "own-goal team attribution follows the saved benefited side, not a later player lineup");
    }

    private static void CheckGoalkeeperRatings(Action<bool, string> check)
    {
        JObject Player(JObject facts, string name) =>
            (JObject)facts["players"]!.Single(player => player.Value<string>("name") == name);
        JObject Record(JObject facts, string name) => (JObject)Player(facts, name)["goalkeeper"]!;
        var input = Input(
            KeeperMatch("Green", "Blue", 2, 0, 0),
            KeeperMatch("Red", "Blue", 1, 1, 1),
            KeeperMatch("Green", "Red", 1, 0, 2),
            KeeperMatch("Green", "Blue", 0, 2, 3),
            KeeperMatch("Green", "Red", 0, 0, 4));

        foreach (var language in new[] { "en", "he" })
        {
            var facts = Facts(input, language);
            var record = Record(facts, "Green5");
            check(record.Value<int>("matches") == 4 && record.Value<int>("recordedAppearances") == 4
                && record.Value<int>("cleanSheets") == 3 && record.Value<int>("goalsConceded") == 2,
                $"{language}: defensive facts use only the designated goalkeeper's four appearances");
            check(record.Value<int>("longestCleanSheetRun") == 2 && record.Value<bool>("streakOrderAvailable"),
                $"{language}: opponent-only rest games do not interrupt a goalkeeper clean-sheet streak");
            check(record.Value<double>("goalsConcededPerMatch") == 0.5
                && record.Value<bool>("hasLowestConcededRate"),
                $"{language}: equal conceded totals over unequal appearances are compared by rate");
            check(!Record(facts, "Red5").Value<bool>("hasLowestConcededRate"),
                $"{language}: two conceded in three matches does not tie two conceded in four");
            check(Player(facts, "Green5").Value<double>("rating") > Player(facts, "Green4").Value<double>("rating")
                && Player(facts, "Green5").Value<int>("goals") == 0 && Player(facts, "Green5").Value<int>("assists") == 0,
                $"{language}: goalkeepers can earn defensive credit without attacking contributions");
            check(Player(facts, "Green4")["goalkeeper"] == null
                && Player(facts, "Green4").Value<double>("rating") == 5.6,
                $"{language}: zero-scoring outfield players are not inferred to be goalkeepers");

            var shuffled = (JObject)input.DeepClone();
            shuffled["matchesHistory"] = new JArray(((JArray)shuffled["matchesHistory"]!).Reverse().Select(match => match.DeepClone()));
            check(JToken.DeepEquals(record, Record(Facts(shuffled, language), "Green5")),
                $"{language}: recorded timestamps make goalkeeper facts invariant to input ordering");
        }

        var ambiguous = Input(
            KeeperMatch("Green", "Blue", 1, 0, 0),
            KeeperMatch("Green", "Blue", 1, 0, 1),
            KeeperMatch("Green", "Blue", 1, 0, 2));
        ambiguous["matchesHistory"]![1]!["teamA"]!["players"]![3]!["isGoalKeeper"] = true;
        var ambiguityFacts = Facts(ambiguous, "en");
        check(Record(ambiguityFacts, "Green5").Value<int>("matches") == 2
            && Record(ambiguityFacts, "Green5").Value<int>("longestCleanSheetRun") == 1,
            "multiple marked goalkeepers exclude that match and break the previous keeper's streak");
        check(Record(ambiguityFacts, "Green4").Value<int>("matches") == 0
            && Player(ambiguityFacts, "Green4").Value<double>("rating") == 5.6
            && Player(ambiguityFacts, "Green4")["ratingFactors"]!.ToString().Contains("excludes")
            && Record(ambiguityFacts, "Green4")["goalsConcededPerMatch"]!.Type == JTokenType.Null,
            "an ambiguous-only keeper gets no defensive bonus or fabricated assignment");

        foreach (var change in new[] { "missing", "outfield", "absent", "invalid", "negative", "duplicate" })
        {
            var variant = (JObject)ambiguous.DeepClone();
            var middle = variant["matchesHistory"]![1]!;
            middle["teamA"]!["players"]![3]!["isGoalKeeper"] = false;
            var keeper = (JObject)middle["teamA"]!["players"]![4]!;
            if (change == "missing") keeper.Remove("isGoalKeeper");
            if (change == "outfield") keeper["isGoalKeeper"] = false;
            if (change == "absent") keeper["name"] = "Replacement";
            if (change == "invalid") ((JObject)middle["teamB"]!).Remove("score");
            if (change == "negative") middle["teamB"]!["score"] = -1;
            if (change == "duplicate") middle["teamB"]!["players"]![4]!["name"] = "Green5";
            var record = Record(Facts(variant, "en"), "Green5");
            check(record.Value<int>("matches") == 2 && record.Value<int>("longestCleanSheetRun") == 1,
                $"{change}: an unconfirmed appearance cannot bridge a clean-sheet run");
            check(record.Value<int>("recordedAppearances") == (change == "absent" ? 2 : 3),
                $"{change}: recorded appearances count matches once even across duplicate lineup names");
        }

        var legacy = (JObject)input.DeepClone();
        foreach (var match in legacy["matchesHistory"]!.OfType<JObject>())
        foreach (var side in new[] { "teamA", "teamB" })
        foreach (var player in match[side]!["players"]!.OfType<JObject>())
            player.Remove("isGoalKeeper");
        var legacyPlayers = Facts(legacy, "en")["players"]!;
        foreach (var role in new JToken[] { new JValue(false), new JValue("true"), new JValue(1), JValue.CreateNull() })
        {
            var variant = (JObject)legacy.DeepClone();
            foreach (var match in variant["matchesHistory"]!.OfType<JObject>())
            foreach (var side in new[] { "teamA", "teamB" })
                match[side]!["players"]![4]!["isGoalKeeper"] = role.DeepClone();
            check(JToken.DeepEquals(legacyPlayers, Facts(variant, "en")["players"]),
                $"non-true-boolean role {role.Type}/{role} preserves the complete ordinary player facts");
        }
        var duplicateWithin = Input(KeeperMatch("Green", "Blue", 1, 0, 0));
        duplicateWithin["matchesHistory"]![0]!["teamA"]!["players"]![3]!["name"] = "Green5";
        check(Record(Facts(duplicateWithin, "en"), "Green5").Value<int>("matches") == 0,
            "one flagged player with a duplicated lineup name does not receive defensive credit");

        var swapped = Input(
            KeeperMatch("Green", "Blue", 1, 0, 0),
            KeeperMatch("Green", "Blue", 2, 1, 1));
        swapped["matchesHistory"]![1]!["teamA"]!["players"]![4]!["name"] = "Blue5";
        swapped["matchesHistory"]![1]!["teamB"]!["players"]![4]!["name"] = "Green5";
        var swappedRecord = Record(Facts(swapped, "en"), "Green5");
        check(swappedRecord.Value<int>("matches") == 2 && swappedRecord.Value<int>("goalsConceded") == 2
            && swappedRecord.Value<int>("cleanSheets") == 1,
            "a transferred goalkeeper gets each actual lineup's conceded score, not their final team's aggregate");

        var withoutTimes = (JObject)input.DeepClone();
        foreach (var match in withoutTimes["matchesHistory"]!.OfType<JObject>())
            match.Remove("createdAt");
        var noOrder = Record(Facts(withoutTimes, "en"), "Green5");
        check(noOrder.Value<int>("cleanSheets") == 3 && noOrder.Value<int>("longestCleanSheetRun") == 0
            && !noOrder.Value<bool>("streakOrderAvailable"),
            "missing timestamps preserve clean-sheet counts but never fabricate a consecutive run");
        var duplicateTimes = (JObject)input.DeepClone();
        duplicateTimes["matchesHistory"]![1]!["createdAt"] = duplicateTimes["matchesHistory"]![0]!["createdAt"]!.DeepClone();
        check(!Record(Facts(duplicateTimes, "en"), "Green5").Value<bool>("streakOrderAvailable"),
            "ambiguous timestamp ties disable streak credit");

        var single = Facts(Input(KeeperMatch("Green", "Blue", 0, 0, 0)), "en");
        check(Player(single, "Green5").Value<double>("rating") == Player(single, "Blue5").Value<double>("rating")
            && Player(single, "Green5").Value<double>("rating") < 6
            && !Record(single, "Green5").Value<bool>("hasLowestConcededRate"),
            "one scoreless draw earns equal, modest goalkeeper credit and no smallest-sample leader label");
        var repeated = Facts(Input(Enumerable.Range(0, 4).Select(i => KeeperMatch("Green", "Blue", 0, 0, i)).ToArray()), "en");
        check(Player(repeated, "Green5").Value<double>("rating") > Player(single, "Green5").Value<double>("rating")
            && Record(repeated, "Green5").Value<bool>("hasLowestConcededRate")
            && Record(repeated, "Blue5").Value<bool>("hasLowestConcededRate"),
            "sustained clean sheets earn more credit and equal rates share the qualifying lead");
        var uneven = Facts(Input(Enumerable.Range(0, 7)
            .Select(i => KeeperMatch("Green", i < 3 ? "Blue" : "Red", 0, 0, i)).ToArray()), "en");
        check(!Record(uneven, "Blue5").Value<bool>("hasLowestConcededRate")
            && Record(uneven, "Red5").Value<bool>("hasLowestConcededRate")
            && Record(uneven, "Green5").Value<bool>("hasLowestConcededRate"),
            "three clean appearances below half the largest sample cannot claim the qualifying lowest rate");

        var keeperAttack = Input(KeeperMatch("Green", "Blue", 2, 0, 0));
        ((JObject)keeperAttack["matchesHistory"]![0]!).Remove("goalEvents");
        keeperAttack["matchesHistory"]![0]!["teamA"]!["goals"]![0]!["scorer"] = "Green5";
        keeperAttack["matchesHistory"]![0]!["teamA"]!["goals"]![1]!["assist"] = "Green5";
        var attackingKeeper = Player(Facts(keeperAttack, "en"), "Green5");
        check(attackingKeeper.Value<int>("goals") == 1 && attackingKeeper.Value<int>("assists") == 1
            && attackingKeeper.Value<double>("rating") > Player(single, "Green5").Value<double>("rating"),
            "goalkeeper goals and assists are still credited");

        var keeperType = Assembly.GetType("TeamsGenerator.Ai.MatchSummaryAnalyzer")!
            .GetNestedType("GoalkeeperFact", BindingFlags.NonPublic)!;
        double Credit(int goals, int assists, JObject record) =>
            (double)Invoke("MatchSummaryAnalyzer", "GoalkeeperContribution", goals, assists, record.ToObject(keeperType)!);
        var fullTime = Record(repeated, "Green5");
        var mixed = (JObject)fullTime.DeepClone();
        mixed["recordedAppearances"] = 8;
        check(Credit(2, 1, mixed) > Credit(2, 1, fullTime)
            && Credit(0, 0, mixed) == Credit(0, 0, fullTime),
            "mixed-role players keep more attacking weight without receiving credit for unconfirmed defensive appearances");
        check(Credit(1, 0, fullTime) - Credit(0, 0, fullTime)
            < (double)Invoke("MatchSummaryAnalyzer", "CalculateContributionRating", 1, 0),
            "full-time goalkeeper attack credit is positive but reduced relative to outfield credit");
        var higherConcessions = (JObject)fullTime.DeepClone();
        higherConcessions["cleanSheets"] = 0;
        higherConcessions["longestCleanSheetRun"] = 0;
        higherConcessions["goalsConceded"] = 8;
        fullTime["matchdayConcededPerTeamMatch"] = 1;
        higherConcessions["matchdayConcededPerTeamMatch"] = 1;
        check(Credit(0, 0, fullTime) > Credit(0, 0, higherConcessions),
            "at an equal sample and baseline, clean sheets and lower concessions increase defensive credit");
        for (var goals = 0; goals <= 30; goals++)
        for (var assists = 0; assists <= 30; assists++)
        {
            var credit = Credit(goals, assists, fullTime);
            check(credit >= 0 && credit < 3.6 && Credit(goals + 1, assists, fullTime) > credit
                && Credit(goals, assists + 1, fullTime) > credit,
                $"goalkeeper attacking credit stays bounded and strictly increasing at {goals}/{assists}");
        }

        var prompt = (string)Invoke("MatchSummaryPrompt", "Create", "en");
        check(prompt.Contains("sole marked goalkeeper") && prompt.Contains("shots faced")
            && prompt.Contains("goalkeeper.matches is zero") && prompt.Contains("streakOrderAvailable")
            && prompt.Contains("never by raw conceded totals"),
            "goalkeeper narrative is restricted to valid role-specific team outcomes, not invented saves or awards");
    }

    private static JObject KeeperMatch(string a, string b, int goalsA, int goalsB, int index)
    {
        var match = Match(a, b, goalsA, goalsB);
        match["createdAt"] = new DateTimeOffset(2020, 1, 1, 12, 0, 0, TimeSpan.Zero).AddMinutes(index).ToString("O");
        foreach (var side in new[] { "teamA", "teamB" })
            match[side]!["players"]![4]!["isGoalKeeper"] = true;
        return match;
    }

    private static void CheckContributionRatings(Action<bool, string> check)
    {
        double Contribution(int goals, int assists) =>
            (double)Invoke("MatchSummaryAnalyzer", "CalculateContributionRating", goals, assists);

        check(Contribution(0, 0) == 0, "zero contributions leave the neutral baseline unchanged");
        check(Contribution(8, 0) > Contribution(4, 0) && Contribution(0, 8) > Contribution(0, 4),
            "goals and assists beyond the fourth continue to earn credit");
        check(Math.Abs(Contribution(3, 0) - Contribution(0, 4)) < 0.000001,
            "combined contribution credit preserves the 0.6-to-0.45 goal/assist weights");

        for (var goals = 0; goals <= 30; goals++)
        for (var assists = 0; assists <= 30; assists++)
        {
            var current = Contribution(goals, assists);
            var nextGoal = Contribution(goals + 1, assists);
            var nextAssist = Contribution(goals, assists + 1);
            check(current >= 0 && current < 3.6,
                $"contribution credit reserves space for all bonuses at {goals} goals/{assists} assists");
            check(nextGoal > nextAssist && nextAssist > current,
                $"every contribution counts, with a goal worth more than an assist at {goals}/{assists}");
            check(Contribution(goals + 2, assists) - nextGoal < nextGoal - current
                && Contribution(goals, assists + 2) - nextAssist < nextAssist - current,
                $"additional goals and assists have diminishing, not capped, credit at {goals}/{assists}");
        }

        foreach (var language in new[] { "en", "he" })
        {
            var input = ContributionInput();
            var facts = Facts(input, language);
            var players = facts["players"]!.ToArray();
            var winner = players.Single(player => player.Value<string>("name") == "Blue1");
            var runnerUp = players.Single(player => player.Value<string>("name") == "Green1");
            check(winner.Value<int>("goals") == 8 && winner.Value<int>("assists") == 2
                && runnerUp.Value<int>("goals") == 4 && runnerUp.Value<int>("assists") == 4,
                $"{language}: synthetic match events establish the reported contribution pattern");
            check(winner.Value<double>("rating") == 8.9 && runnerUp.Value<double>("rating") == 7.8,
                $"{language}: eight-goal top scorer on the winning team outranks four-goal/four-assist runner-up");
            check(Array.IndexOf(players, winner) < Array.IndexOf(players, runnerUp),
                $"{language}: the AI receives the improved player ordering");
            check(winner["ratingFactors"]!.ToString().Contains("top scorer")
                && winner["ratingFactors"]!.ToString().Contains("team finished first")
                && runnerUp["ratingFactors"]!.ToString().Contains("team finished second")
                && !runnerUp["ratingFactors"]!.ToString().Contains("top assister"),
                $"{language}: actual awards and placement bonuses remain separate from contribution credit");
            check(players.All(player => player.Value<double>("rating") >= 1 && player.Value<double>("rating") <= 10),
                $"{language}: ratings stay within the existing display contract");

            var reordered = (JObject)input.DeepClone();
            var matches = (JArray)reordered["matchesHistory"]!;
            foreach (var match in matches.OfType<JObject>())
            foreach (var side in new[] { "teamA", "teamB" })
                match[side]!["players"] = new JArray(((JArray)match[side]!["players"]!).Reverse().Select(player => player.DeepClone()));
            check(JToken.DeepEquals(facts["players"], Facts(reordered, language)["players"]),
                $"{language}: rating values and order are independent of roster insertion order");
        }

        var prolific = Input(Match("Green", "Blue", 20, 0));
        foreach (var goal in prolific["matchesHistory"]![0]!["teamA"]!["goals"]!)
            goal["assist"] = "Green2";
        var prolificPlayers = Facts(prolific, "en")["players"]!;
        check(prolificPlayers.Single(player => player.Value<string>("name") == "Green1").Value<double>("rating") == 9.5
            && prolificPlayers.Single(player => player.Value<string>("name") == "Green2").Value<double>("rating") == 9.3,
            "high contribution totals do not collapse scorer and assister to automatic 10/10 ratings");
    }

    private static JObject ContributionInput()
    {
        var match = Match("Green", "Blue", 8, 10);
        match.Remove("goalEvents");
        foreach (var (side, goals, assists) in new[] { ("teamA", 4, 4), ("teamB", 8, 2) })
        {
            var color = match[side]!.Value<string>("color");
            match[side]!["goals"] = new JArray(
                Enumerable.Range(0, goals).Select(_ => new JObject { ["scorer"] = color + "1" })
                .Concat(Enumerable.Range(0, assists).Select(_ => new JObject { ["scorer"] = color + "2", ["assist"] = color + "1" })));
        }
        // Another player leads assists so the comparison only includes the applicable scorer award.
        var greenGoals = (JArray)match["teamA"]!["goals"]!;
        foreach (var goal in greenGoals.Take(4))
            goal["assist"] = "Green3";
        var extra = Match("Orange", "Green", 0, 3);
        extra.Remove("goalEvents");
        foreach (var goal in extra["teamB"]!["goals"]!)
        {
            goal["scorer"] = "Green4";
            goal["assist"] = "Green3";
        }
        return Input(match, Match("Blue", "Orange", 0, 0), extra);
    }

    private static JObject Facts(JObject input, string language) =>
        JObject.Parse((string)Invoke("MatchSummaryAnalyzer", "CreateFactSheet", input, language));

    private static object Invoke(string type, string method, params object[] args) =>
        Assembly.GetType("TeamsGenerator.Ai." + type)!.GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args)!;

    private static void CheckPositions(Action<bool, string> check, JObject facts, params int[] expected)
    {
        var standings = facts["standings"]!.ToArray();
        check(standings.Select(team => team.Value<int>("position")).SequenceEqual(expected),
            "competition ranks respect all tiebreakers: " + string.Join(",", expected));
        check(standings.All(team => team.Value<bool>("jointPosition") ==
            (standings.Count(other => other.Value<int>("position") == team.Value<int>("position")) > 1)),
            "jointPosition agrees with shared ranks");
    }

    private static JObject Team(string color, int score) => new()
    {
        ["color"] = color, ["score"] = score,
        ["goals"] = new JArray(Enumerable.Range(0, score).Select(_ => new JObject { ["scorer"] = color + "1" })),
        ["players"] = new JArray(Enumerable.Range(1, 5).Select(index => new JObject { ["name"] = color + index }))
    };

    private static JObject Match(string a, string b, int goalsA, int goalsB) => new()
    {
        ["teamA"] = Team(a, goalsA), ["teamB"] = Team(b, goalsB),
        ["goalEvents"] = new JArray(Enumerable.Repeat(a, goalsA).Concat(Enumerable.Repeat(b, goalsB))
            .Select(color => new JObject { ["team"] = color, ["scorer"] = color + "1" }))
    };

    private static JObject Input(params JObject[] matches)
    {
        var stats = new JObject();
        var index = 0;
        foreach (var match in matches)
        {
            match["matchKey"] = "match-" + index++;
            foreach (var (side, opponent) in new[] { ("teamA", "teamB"), ("teamB", "teamA") })
            {
                var team = match[side]!.Value<string>("color")!;
                var goals = match[side]!.Value<int>("score");
                var conceded = match[opponent]!.Value<int>("score");
                if (stats[team] is not JObject entry)
                {
                    entry = new JObject { ["w"] = 0, ["d"] = 0, ["l"] = 0, ["gf"] = 0, ["ga"] = 0, ["points"] = 0 };
                    stats[team] = entry;
                }
                var outcome = goals > conceded ? "w" : goals < conceded ? "l" : "d";
                entry[outcome] = entry.Value<int>(outcome) + 1;
                entry["gf"] = entry.Value<int>("gf") + goals;
                entry["ga"] = entry.Value<int>("ga") + conceded;
                entry["points"] = entry.Value<int>("points") + (goals > conceded ? 3 : goals == conceded ? 1 : 0);
            }
        }
        return new JObject { ["matchesHistory"] = new JArray(matches), ["tableStats"] = new JObject { ["stats"] = stats } };
    }

    private static JObject WithStandings(params (string Team, int Points, int Gf, int Ga)[] rows) => new()
    {
        ["players"] = new JArray(rows.Select(row => new JObject { ["name"] = row.Team + "1" })),
        ["matchesHistory"] = new JArray(Enumerable.Range(1, rows.Length - 1).Select(index => Match(rows[0].Team, rows[index].Team, 0, 0))),
        ["tableStats"] = new JObject { ["stats"] = new JObject(rows.Select(row => new JProperty(row.Team,
            new JObject { ["points"] = row.Points, ["gf"] = row.Gf, ["ga"] = row.Ga }))) }
    };
}
