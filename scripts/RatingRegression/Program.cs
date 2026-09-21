using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TeamsGenerator.Algos;
using TeamsGenerator.Algos.AiAlgo;
using TeamsGenerator.Algos.BackAndForthAlgo;
using TeamsGenerator.Algos.PositionsAlgo;
using TeamsGenerator.Algos.SkillWiseAlgo;
using TeamsGenerator.API;
using TeamsGenerator.Orchestration;
using TeamsGenerator.Orchestration.Contracts;
using TeamsGenerator.Utilities;
using TeamsGeneratorWebAPI.Clients;
using TeamsGeneratorWebAPI.Collaboration;
using TeamsGeneratorWebAPI.Controllers;
using TeamsGeneratorWebAPI.PlayersBlob;
using TeamsGeneratorWebAPI.Storage;
using TeamsGeneratorWebAPI.Telemetry;

var assertions = 0;
void Check(bool condition, string message)
{
    assertions++;
    if (!condition) throw new InvalidOperationException(message);
}
void Equal(double expected, double actual, string message) =>
    Check(Math.Abs(expected - actual) < 0.00001, $"{message}: expected {expected}, got {actual}");
object Invoke(Type type, string name, params object[] values) =>
    type.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!.Invoke(null, values)!;

WebAppAPI.Init();
const string oldJson = """{"key":"old","name":"Old","rank":7,"attack":8,"defence":4,"passing":6,"leadership":7,"stamina":5,"isGoalKeeper":true}""";
var legacy = JsonConvert.DeserializeObject<SkillWisePlayer>(oldJson)!;
Equal(6, legacy.Rank, "legacy derived base rank");
Equal(6, legacy.EffectiveRank, "legacy effective rank");
Equal(8, legacy.GetSkillValue("attack"), "legacy keeper retains field skills until explicit rating");
Check(legacy.GoalkeeperSkill == null && !legacy.IsRecoveringFromInjury, "old JSON defaults");
legacy.GoalkeeperSkill = 9;
legacy.IsRecoveringFromInjury = true;
Equal(8.5, legacy.EffectiveRank, "keeper injury once");
Equal(8.5, legacy.GetSkillValue("attack"), "keeper scalar overrides field skill");
Equal(6, legacy.Rank, "keeper base rank unchanged");
Equal(8, legacy.Attack, "base attack unchanged");
var serialized = JsonConvert.SerializeObject(legacy);
Check(!serialized.Contains("EffectiveRank"), "effective rank not serialized");
var again = JsonConvert.DeserializeObject<SkillWisePlayer>(serialized)!;
Equal(8.5, again.EffectiveRank, "roundtrip has no stacking");
var ignoredRank = JsonConvert.DeserializeObject<SkillWisePlayer>(
    """{"attack":5,"defence":5,"passing":5,"leadership":5,"stamina":5,"EffectiveRank":99}""")!;
Check(!JsonConvert.SerializeObject(ignoredRank).Contains("EffectiveRank"), "incoming effective rank cannot roundtrip");
again.IsGoalKeeper = false;
Equal(7.5, again.GetSkillValue("attack"), "outfield ignores dormant goalkeeper level");
again.Attack = 1;
Equal(1, again.GetSkillValue("attack"), "injury floor");
again.GoalkeeperSkill = double.NaN;
Check(again.GoalkeeperSkill == null, "nonfinite goalkeeper becomes null");
again.GoalkeeperSkill = 100;
Equal(10, again.GoalkeeperSkill!.Value, "goalkeeper maximum");
again.GoalkeeperSkill = -5;
Equal(1, again.GoalkeeperSkill!.Value, "goalkeeper minimum");
again.Attack = float.PositiveInfinity;
Equal(4.5, again.GetSkillValue("attack"), "nonfinite outfield uses neutral effective value");

var customSkills = new List<SkillDefinition>
{
    new() { Id = "speed", Name = "Speed" },
    new() { Id = "touch", Name = "Touch" },
    new() { Id = "vision", Name = "Vision" }
};
var custom = JsonConvert.DeserializeObject<SkillWisePlayer>(
    """{"speed":8,"touch":7,"vision":3,"isRecoveringFromInjury":true}""")!;
custom.SetActiveSkills(customSkills);
Equal(6, custom.Rank, "custom base average");
Equal(5.5, custom.EffectiveRank, "custom effective average");
Equal(7.5, custom.GetSkillValue("speed"), "custom injury");
custom.IsGoalKeeper = true;
custom.GoalkeeperSkill = 2;
Equal(1.5, custom.GetSkillValue("speed"), "custom skills cannot override GK");
Check(JObject.FromObject(custom)["speed"]!.Value<int>() == 8, "custom base serialized");
foreach (var name in new[] { "goalkeeperSkill", "isRecoveringFromInjury", "EffectiveRank" })
    Check(!SkillDefinition.IsValidId(name), "dedicated field reserved: " + name);

var back = JsonConvert.DeserializeObject<BackAndForthPlayer>(oldJson)!;
back.IsRecoveringFromInjury = true;
Equal(7, back.Rank, "BackAndForth base remains unchanged");
Equal(6.5, back.EffectiveRank, "BackAndForth injury");
back.GoalkeeperSkill = 3;
Equal(2.5, back.EffectiveRank, "BackAndForth goalkeeper");
Equal(7, JObject.FromObject(back)["Rank"]!.Value<double>(), "serialized BackAndForth base");
var positional = JsonConvert.DeserializeObject<PositionsPlayer>(oldJson)!;
positional.GoalkeeperSkill = 9;
positional.Positions = new() { Position.GK };
positional.IsRecoveringFromInjury = true;
Equal(8.5, positional.EffectiveRank, "Positions GK uses positions not flag");
positional.Positions = new() { Position.GK, Position.ST };
Equal(5.5, positional.EffectiveRank, "mixed positions retain field-skill contribution");
Equal(9, positional.GoalkeeperSkill.Value, "mixed positions preserve dormant goalkeeper rating");
Check(PlayerRatings.IsGoalkeeper(positional), "mixed positions still count for keeper distribution");
positional.Positions = new() { Position.ST };
Equal(5.5, positional.EffectiveRank, "Positions outfield fallback");
Check(typeof(PositionsPlayer).GetProperty("IsGoalKeeper") == null, "no incoherent positions flag");

foreach (var algo in new[] { AlgoType.SkillWise, AlgoType.Positions, AlgoType.BackAndForth, AlgoType.Ai })
{
    var info = new WebAppAlgoInfo(algo, "", "");
    info.Init();
    Check(info.PlayerProperties.All(property =>
        !PrivatePlayerFields.IsDedicatedAssessment(property.Name)
        && property.Name != "EffectiveRank"), "no duplicate reflected controls " + algo);
}

List<IPlayer> Roster(AlgoType algo)
{
    return Enumerable.Range(0, 12).Select(index =>
    {
        var value = 2 + index % 8;
        var json = new JObject
        {
            ["key"] = $"p{index}", ["name"] = $"Player {index}",
            ["rank"] = value, ["attack"] = value, ["defence"] = value,
            ["stamina"] = value, ["leadership"] = value, ["passing"] = value,
            ["isGoalKeeper"] = index < 3, ["goalkeeperSkill"] = index < 3 ? 2 + index * 4 : null,
            ["isRecoveringFromInjury"] = index % 2 == 0,
            ["positions"] = new JArray(index < 3 ? 0 : 5), ["isArrived"] = true
        };
        return WebAppAPI.AlgoTypeToPlayerSerializerMapper[algo](new JArray(json).ToString())[0];
    }).Cast<IPlayer>().ToList();
}

foreach (var algo in new[] { AlgoType.SkillWise, AlgoType.Positions, AlgoType.BackAndForth })
{
    for (var repetition = 0; repetition < 5; repetition++)
    {
        var roster = Roster(algo);
        var original = roster.ToDictionary(player => player.Key, player => JsonConvert.SerializeObject(player));
        var config = new AlgoConfig
        {
            TeamsCount = 3, UseChemistry = true,
            ChemistryScores = new Dictionary<string, double>
            {
                [ChemistryPairKey.Create("p0", "p1")] = 1,
                [ChemistryPairKey.Create("p1", "p8")] = -1,
                [ChemistryPairKey.Create("p2", "p10")] = 1
            }
        };
        var teams = AlgoRunner.Run(algo, roster.ToList(), config, null);
        Check(teams.Sum(team => team.Players.Count) == 12, "coverage " + algo);
        Check(teams.SelectMany(team => team.Players).Select(player => player.Key).Distinct().Count() == 12, "no duplicates " + algo);
        Check(teams.All(team => team.Players.Count == 4), "balanced sizes " + algo);
        Check(teams.All(team => team.Players.Count(PlayerRatings.IsGoalkeeper) == 1), "goalkeeper distribution " + algo);
        foreach (var team in teams)
            Equal(team.Players.Sum(PlayerRatings.GetRank), team.TotalRank, "effective totals " + algo);
        foreach (var player in roster)
            Check(original[player.Key] == JsonConvert.SerializeObject(player), "base state preserved " + algo);
        var locked = new Team(0) { Players = new() { roster[0] }, TotalRank = 999 };
        var lockedResult = AlgoRunner.Run(algo, roster.ToList(), config, new() { locked });
        Check(lockedResult.Single(team => team.Index == 0).Players.Contains(roster[0]), "locked placement " + algo);
        Check(lockedResult.All(team => team.Players.Count(PlayerRatings.IsGoalkeeper) == 1), "locked GK distribution " + algo);
        foreach (var team in lockedResult)
            Equal(team.Players.Sum(PlayerRatings.GetRank), team.TotalRank, "locked totals recomputed " + algo);
    }
}
foreach (var algo in new[] { AlgoType.SkillWise, AlgoType.Positions, AlgoType.BackAndForth })
{
    var roster = Roster(algo);
    var stale = WebAppAPI.AlgoTypeToPlayerSerializerMapper[algo](
        """[{"key":"p0","name":"stale","rank":10,"isGoalKeeper":true,"positions":[0],"goalkeeperSkill":10,"isRecoveringFromInjury":false}]""")[0];
    var teams = AlgoRunner.Run(algo, roster, new AlgoConfig { TeamsCount = 3 },
        new() { new Team(0) { Players = new() { stale } } });
    Check(ReferenceEquals(teams.Single(team => team.Index == 0).Players.First(player => player.Key == "p0"), roster[0]),
        "locked snapshot refreshed from current organizer roster " + algo);
    Equal(1.5, PlayerRatings.GetRank(roster[0]), "locked current rating used " + algo);
}
foreach (var algo in new[] { AlgoType.SkillWise, AlgoType.Positions })
{
    var roster = Roster(algo);
    var config = new AlgoConfig { TeamsCount = 3, SkillDefinitions = customSkills };
    var teams = AlgoRunner.Run(algo, roster, config, null);
    foreach (var team in teams)
        Equal(team.Players.Sum(PlayerRatings.GetRank), team.TotalRank, "custom generated total " + algo);
    Equal(1.5, PlayerRatings.GetRank(roster[0]), "explicit GK unaffected by custom definitions " + algo);
}
foreach (var algo in new[] { AlgoType.SkillWise, AlgoType.Positions, AlgoType.BackAndForth })
{
    foreach (var keeperCount in new[] { 0, 1, 2, 5, 12 })
    {
        var roster = Roster(algo);
        for (var index = 0; index < roster.Count; index++)
        {
            if (roster[index] is IGoalKeeperSupport goalkeeper) goalkeeper.IsGoalKeeper = index < keeperCount;
            if (roster[index] is PositionsPlayer position) position.Positions = new() { index < keeperCount ? Position.GK : Position.ST };
            var assessment = (PlayerRatingAssessment)roster[index];
            assessment.GoalkeeperSkill = null;
            assessment.IsRecoveringFromInjury = false;
        }
        var teams = AlgoRunner.Run(algo, roster, new AlgoConfig { TeamsCount = 3 }, null);
        var keeperCounts = teams.Select(team => team.Players.Count(PlayerRatings.IsGoalkeeper)).ToList();
        Check(keeperCounts.Max() - keeperCounts.Min() <= 1, "legacy varied keeper distribution " + algo);
        Check(teams.All(team => team.Players.Count == 4), "legacy varied keeper sizes " + algo);
        foreach (var team in teams)
            Equal(team.Players.Sum(player => player.Rank), team.TotalRank, "legacy base strength unchanged " + algo);
    }
    var lockedRoster = Roster(algo);
    var lockedPlayers = lockedRoster.Skip(3).Take(4).ToList();
    var lockedTeams = AlgoRunner.Run(algo, lockedRoster, new AlgoConfig { TeamsCount = 3 },
        new() { new Team(0) { Players = lockedPlayers } });
    Check(lockedTeams.Single(team => team.Index == 0).Players.All(lockedPlayers.Contains),
        "full locked team not overfilled by keeper distribution " + algo);
    Check(lockedTeams.All(team => team.Players.Count == 4), "locked team capacities " + algo);
}
var sorted = (List<IPlayer>)Invoke(typeof(IPlayer).Assembly.GetType("TeamsGenerator.Utilities.Helper")!,
    "SortPlayersByRank", new List<IPlayer>
{
    new BackAndForthPlayer { Name = "Strong field, weak GK", Rank = 10, IsGoalKeeper = true, GoalkeeperSkill = 2 },
    new BackAndForthPlayer { Name = "Weak field, strong GK", Rank = 1, IsGoalKeeper = true, GoalkeeperSkill = 9 }
});
Check(sorted[0].Name == "Weak field, strong GK", "GK level actually controls order");

var inputRoster = Roster(AlgoType.SkillWise);
var request = JObject.FromObject(new
{
    players = inputRoster,
    config = new UserConfigResponse
    {
        NumberOfTeams = 3, ShowWhoBegins = false,
        ShirtsColors = new()
        {
            new() { ColorName = "Red", IsMarked = true },
            new() { ColorName = "Green", IsMarked = true },
            new() { ColorName = "Blue", IsMarked = true }
        }
    }
});
var response = WebAppAPI.GetTeams(request, (int)AlgoType.SkillWise);
foreach (var team in response.Teams)
{
    var outfield = team.Players.Cast<SkillWisePlayer>().Where(player => !PlayerRatings.HasGoalkeeperRating(player)).ToList();
    Equal(outfield.Average(player => player.GetSkillValue("attack")), team.SkillAverages["attack"], "response excludes explicit goalkeeper contributions from field charts");
    Equal(team.Players.Sum(PlayerRatings.GetRank), team.Rank, "response strength includes goalkeeper");
}
var aiInput = JObject.FromObject(Invoke(typeof(AiManager), "CreateBalancingInput", legacy));
Equal(8.5, aiInput["attack"]!.Value<double>(), "AI sees effective scalar");
Check(aiInput["IsRecoveringFromInjury"] == null, "AI cannot apply second recovery deduction");

var privateJson = """{"Teams":[{"Players":[{"Key":"p","Name":"Player","GoalkeeperSkill":9,"isRecoveringFromInjury":true,"Attack":4}]}],"swaps":{"out":{"GOALKEEPERSKILL":2,"IsRecoveringFromInjury":true}}}""";
var clean = PrivatePlayerFields.SanitizeJson(privateJson);
Check(!clean.Contains("goalkeeperSkill", StringComparison.OrdinalIgnoreCase)
    && !clean.Contains("isRecoveringFromInjury", StringComparison.OrdinalIgnoreCase), "recursive snapshot privacy");
Check(clean.Contains("Player") && clean.Contains("Attack"), "snapshot preserves other data");
Check(new MatchEntity { SerializedMatch = privateJson }.SerializedMatch == clean, "public match sanitization");
Check(new PlayerSwapEntity { SerializedSwap = privateJson }.SerializedSwap == clean, "public swap sanitization");
Check(new PlayerEntity { JsonData = privateJson }.JsonData == clean, "public player sanitization");
var shared = (JObject)Invoke(typeof(TeamsController), "CreateSharedTeams", JObject.Parse(privateJson));
Check(shared.SelectToken("Teams[0].Players[0].Attack") == null, "PascalCase team privacy allowlist");
var sharedRoster = (JArray)Invoke(typeof(UserPlayersController), "CreateSharedRoster", new List<IPlayer> { legacy });
Check(sharedRoster[0]["goalkeeperSkill"] == null && sharedRoster[0]["isRecoveringFromInjury"] == null, "shared roster privacy");
var emptyAssessments = new Dictionary<string, JObject>();
var assessments = (Dictionary<string, JObject>)Invoke(typeof(PlayerAssessmentService), "ExtractAssessments",
    JArray.Parse("""[{"key":"p","name":"Player","attack":4,"goalkeeperSkill":9,"isRecoveringFromInjury":true}]"""),
    emptyAssessments, true);
Check(assessments["p"].Properties().Count() == 2, "owner stores only new private assessments");
var oldClient = (Dictionary<string, JObject>)Invoke(typeof(PlayerAssessmentService), "ExtractAssessments",
    JArray.Parse("""[{"key":"p","name":"Player"}]"""), assessments, true);
Equal(9, oldClient["p"]["goalkeeperSkill"]!.Value<double>(), "old client save preserves private value");
var reset = (Dictionary<string, JObject>)Invoke(typeof(PlayerAssessmentService), "ExtractAssessments",
    JArray.Parse("""[{"key":"p","GoalkeeperSkill":null,"IsRecoveringFromInjury":false}]"""), assessments, true);
var applied = (JArray)Invoke(typeof(PlayerAssessmentService), "ApplyAssessments",
    JArray.Parse("""[{"key":"p","goalkeeperSkill":8,"isRecoveringFromInjury":true}]"""), reset);
Check(applied[0]["goalkeeperSkill"]!.Type == JTokenType.Null && !applied[0]["isRecoveringFromInjury"]!.Value<bool>(), "explicit clear and case-insensitive overlay");
Check(applied[0].Children<JProperty>().Count() == 3, "no duplicate-case fields");
Equal(10, PrivatePlayerFields.NormalizeAssessment("GoalkeeperSkill", new JValue(100)).Value<double>(),
    "private overlay clamps goalkeeper rating like generation");
Check(PrivatePlayerFields.NormalizeAssessment("GoalkeeperSkill", new JValue(double.PositiveInfinity)).Type == JTokenType.Null,
    "private overlay rejects nonfinite goalkeeper values");

var assessmentReads = new List<string>();
var assessmentProxy = CallbackProxy.Create<IPlayerAssessmentService>((method, args) =>
{
    if (method.Name != "ApplyAsync") throw new InvalidOperationException("Unexpected assessment operation");
    assessmentReads.Add((string)args[0]!);
    var players = (JArray)args[3]!;
    players[0]["goalkeeperSkill"] = 3;
    players[0]["isRecoveringFromInjury"] = false;
    return Task.FromResult(players);
});
var collaborationProxy = CallbackProxy.Create<IGroupCollaborationService>((method, args) =>
    Task.FromResult<GroupAccess?>(new GroupAccess
    {
        OwnerId = (string?)args[1] == "shared.group" ? "owner" : (string)args[0]!,
        GroupId = "default",
        IsOwner = (string?)args[1] != "shared.group"
    }));
var storageProxy = CallbackProxy.Create<IPlayersStorageBlobConnector>((method, args) =>
    Task.FromResult<IResponse>(new GetPlayersResponse(new List<IPlayer> { legacy })));
var telemetryProxy = CallbackProxy.Create<IUsageTelemetry>((method, args) => null);
var controller = new UserPlayersController(
    NullLogger<UserPlayersController>.Instance, storageProxy, telemetryProxy, null!,
    collaborationProxy, assessmentProxy, null!)
{
    ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
};
JObject ReadResult(IActionResult result) => JObject.FromObject(((OkObjectResult)result).Value!);
var publicRoster = ReadResult(await controller.Get("test", "owner", 0));
Check(assessmentReads.Count == 0, "anonymous caller never loads an assessment overlay");
Check(publicRoster["players"]![0]!["name"]!.Value<string>() == "Old", "public roster preserves names");
Check(publicRoster["players"]![0]!["isArrived"] != null, "public roster preserves attendance");
Check(publicRoster["players"]![0]!["goalkeeperSkill"] == null
    && publicRoster["players"]![0]!["isRecoveringFromInjury"] == null, "public default-group roster omits health and GK data");
controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(Array.Empty<Claim>(), "test"));
var missingClaim = ReadResult(await controller.Get("test", "owner", 0));
Check(assessmentReads.Count == 0 && missingClaim["players"]![0]!["isRecoveringFromInjury"] == null,
    "authenticated principal without identity cannot use supplied UID to read private assessments");
controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("user_id", "caller") }, "test"));
var ownerRoster = ReadResult(await controller.Get("test", "some-other-user", 0));
Check(assessmentReads.Last() == "caller", "authenticated identity controls overlay, not supplied UID");
Equal(3, ownerRoster["players"]![0]!["goalkeeperSkill"]!.Value<double>(), "authenticated owner receives own overlay");
var coOrganizerRoster = ReadResult(await controller.Get("test", "owner", 0, "shared.group"));
Check(assessmentReads.Last() == "caller", "co-organizer receives caller overlay only");
Equal(3, coOrganizerRoster["players"]![0]!["goalkeeperSkill"]!.Value<double>(), "owner assessment not exposed to co-organizer");
Check(!coOrganizerRoster["players"]![0]!["isRecoveringFromInjury"]!.Value<bool>(), "co-organizer recovery remains their own");
controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity());
var beforeRejectedSave = assessmentReads.Count;
var rejectedSave = await controller.Post("test",
    JArray.Parse("""[{"key":"old","isRecoveringFromInjury":true}]"""), "owner", 0);
Check(rejectedSave is UnauthorizedObjectResult && assessmentReads.Count == beforeRejectedSave,
    "anonymous callers cannot write recovery to an organizer's private overlay");

var teamsStorageProxy = CallbackProxy.Create<ITeamsStorageBlobConnector>((method, args) =>
    Task.FromResult<IResponse>(new GetTeamsFromStorageResponse(JObject.Parse(privateJson))));
var teamsController = new TeamsController(
    NullLogger<TeamsController>.Instance, telemetryProxy, teamsStorageProxy, null!, null!, null!, collaborationProxy)
{
    ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
};
var publicTeams = ReadResult(await teamsController.GetTeamsFromStorage("test", "owner")).ToString();
Check(!publicTeams.Contains("goalkeeperSkill", StringComparison.OrdinalIgnoreCase)
    && !publicTeams.Contains("isRecoveringFromInjury", StringComparison.OrdinalIgnoreCase),
    "anonymous teams endpoint strips private fields even from older snapshots");
await ChemistryResetRegression.Run(Check);
await InvitationRegression.Run(Check);
await PremiumInvitationRegression.Run(Check);
Console.WriteLine($"Passed {assertions} rating, algorithm, locking, AI-input, privacy, chemistry-reset, invitation and notification assertions.");

public class CallbackProxy : DispatchProxy
{
    public Func<MethodInfo, object?[], object?> Callback { get; set; } = null!;
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
        Callback(targetMethod!, args ?? Array.Empty<object?>());

    public static T Create<T>(Func<MethodInfo, object?[], object?> callback) where T : class
    {
        var instance = DispatchProxy.Create<T, CallbackProxy>();
        ((CallbackProxy)(object)instance).Callback = callback;
        return instance;
    }
}
