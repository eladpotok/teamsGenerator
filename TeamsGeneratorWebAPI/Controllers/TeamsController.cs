using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text.Json;
using TeamsGenerator.Ai;
using TeamsGenerator.Algos.BackAndForthAlgo;
using TeamsGenerator.Algos.SkillWiseAlgo;
using TeamsGenerator.API;
using TeamsGenerator.Utilities;
using TeamsGeneratorWebAPI.Clients;
using TeamsGeneratorWebAPI.Authentication;
using TeamsGeneratorWebAPI.DesignCreator;
using TeamsGeneratorWebAPI.PlayersBlob;

namespace TeamsGeneratorWebAPI.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class TeamsController : ControllerBase
    {
        private readonly ITeamsStorageBlobConnector _azureStorage;

        private readonly ILogger<TeamsController> _logger;
        private readonly TelemetryClient _telemetryClient;
        private readonly AzureTableStorageService _matchService;
        private readonly OpenAiService _aiService;

        public TeamsController(ILogger<TeamsController> logger, TelemetryClient telemetryClient, ITeamsStorageBlobConnector teamsStorageBlobConnector, AzureTableStorageService matchService, OpenAiService aiService)
        {
            _logger = logger;
            _telemetryClient = telemetryClient;
            _azureStorage = teamsStorageBlobConnector;
            _matchService = matchService;
            _aiService = aiService;
        }

        [HttpPost()]
        public async Task<GetTeamsResponse> Post(
            [FromHeader(Name = "client_version")] string ver,
            [FromBody] dynamic dicJson,
            int algoKey,
            string? ownerId = null)
        {
            var effectiveOwnerId =
                RequestUserId.ResolveOptional(User, ownerId);
            _telemetryClient.TrackEvent("GetTeams");
            _telemetryClient.TrackMetric("GetTeams", 1);
            ChemistryHistorySnapshot chemistryHistory =
                string.IsNullOrWhiteSpace(effectiveOwnerId)
                ? new ChemistryHistorySnapshot()
                : await _matchService.GetChemistryHistory(effectiveOwnerId);
            return WebAppAPI.GetTeams(
                dicJson,
                algoKey,
                chemistryHistory.Scores,
                chemistryHistory.MatchdayCount);
        }

        [HttpPost("[action]")]

        public GetTeamsResponse PostResultString([FromHeader(Name = "client_version")] string ver, [FromBody] dynamic dicJson)
        {
            return WebAppAPI.GetResultString(dicJson);
        }

    
        [HttpPost("[action]")]
        public async Task<IActionResult> GetTeamsDesign([FromHeader(Name = "client_version")] string ver, [FromBody] dynamic team)
        {
            var teamsSerializedObject = JsonConvert.SerializeObject(team.playerNames, Newtonsoft.Json.Formatting.Indented);
            IEnumerable<string> players = JsonConvert.DeserializeObject<List<string>>(teamsSerializedObject);

            var ms = ImageCreator.CreateTeams(players.ToList(), team.color.ToString());

            // Convert the image to a byte array and add it to the result list
            byte[] imageBytes = ms.ToArray();

            _telemetryClient.TrackMetric("ShareWithImage", 1);
            return File(imageBytes, "image/png");
        }

        [HttpPost("[action]")]
        public IActionResult GetAllTeamsDesign([FromHeader(Name = "client_version")] string ver, [FromBody] dynamic config)
        {
            JObject request = config as JObject ?? JObject.FromObject(config);
            var teamInfo = request["teamInfo"] as JObject ?? new JObject();
            var teams = request["teams"]?
                .Children<JObject>()
                .Select((team, index) => new TeamShareItem
                {
                    Name = GetString(team, "name", "teamName"),
                    Color = GetString(team, "color"),
                    Players = team["players"]?
                        .Select(player => player.Type == JTokenType.String
                            ? player.ToString()
                            : GetString(player as JObject ?? new JObject(), "name"))
                        .Where(name => !string.IsNullOrWhiteSpace(name))
                        .Select(name => name.Trim())
                        .ToList() ?? new List<string>()
                })
                .ToList() ?? new List<TeamShareItem>();

            var culture = GetString(teamInfo, "currentCulture", "culture");
            using var image = ImageCreator.CreateTeamsOverview(
                teams,
                GetString(teamInfo, "teamName", "matchName"),
                GetString(teamInfo, "location", "venue"),
                GetString(teamInfo, "date", "eventDate"),
                GetString(teamInfo, "dayInWeek"),
                string.IsNullOrWhiteSpace(culture) ? "en-US" : culture);

            _telemetryClient.TrackMetric("ShareAllTeamsWithImage", 1);
            return File(image.ToArray(), "image/png");
        }

        [HttpPost("[action]")]
        public async Task<IActionResult> GetScoresDesign([FromHeader(Name = "client_version")] string ver, [FromBody] dynamic statsJson)
        {
            var statsSerializedObject = JsonConvert.SerializeObject(statsJson.stats, Newtonsoft.Json.Formatting.Indented);
            var topScorersSerializedObject = JsonConvert.SerializeObject(statsJson.topScorers, Newtonsoft.Json.Formatting.Indented);
            //IEnumerable<string> stats = JsonConvert.DeserializeObject<List<string>>(statsSerializedObject);

            var ms = ImageCreator.CreateTable(statsJson.stats, statsJson.topScorers, ver);

            // Convert the image to a byte array and add it to the result list
            byte[] imageBytes = ms.ToArray();

            _telemetryClient.TrackMetric("ShareWithImage", 1);
            return File(imageBytes, "image/png");
        }

        [HttpPost("[action]")]
        public async Task<IActionResult> GetNormalizedScoresDesign([FromHeader(Name = "client_version")] string ver, [FromBody] dynamic statsJson)
        {
            var statsSerializedObject = JsonConvert.SerializeObject(statsJson.stats, Newtonsoft.Json.Formatting.Indented);
            var topScorersSerializedObject = JsonConvert.SerializeObject(statsJson.topScorers, Newtonsoft.Json.Formatting.Indented);
            //IEnumerable<string> stats = JsonConvert.DeserializeObject<List<string>>(statsSerializedObject);

            var ms = ImageCreator.CreateNormalizedTable(statsJson, ver);

            // Convert the image to a byte array and add it to the result list
            byte[] imageBytes = ms.ToArray();

            _telemetryClient.TrackMetric("ShareWithImage", 1);
            return File(imageBytes, "image/png");
        }

        [HttpPost("[action]")]
        public async Task<IResponse> SaveToStorage([FromHeader(Name = "client_version")] string ver, [FromBody] dynamic teams, string uid)
        {
            var userId = RequestUserId.Resolve(User, uid);
            _telemetryClient.TrackEvent("SaveTeamsToStorage");
            _telemetryClient.TrackMetric("SaveTeamsToStorage", 1);
            return await _azureStorage.UploadAsync(teams, new TeamsBlobConfig() { UId = userId });
        }

        [HttpPost("[action]")]
        public async Task<IResponse> GetTeamsFromStorage([FromHeader(Name = "client_version")] string ver, string uid)
        {
            _telemetryClient.TrackEvent("GetTeamsFromStorage");
            _telemetryClient.TrackMetric("GetTeamsFromStorage", 1);
            return await _azureStorage.ListAsync(new TeamsBlobConfig() { UId = uid });
        }

        [HttpPost("[action]")]
        public Dictionary<string, Score> GetScores([FromHeader(Name = "client_version")] string ver, [FromBody] dynamic stats)
        {
            _telemetryClient.TrackEvent("GetScores");
            _telemetryClient.TrackMetric("GetScores", 1);
            return TableCalculator.Create(stats.stats);
        }



        [HttpPost("[action]")]
        public async Task<IActionResult> AddMatch([FromBody] MatchEntity match)
        {
            var result = await _matchService.AddMatchAsync(match);
            if (result == MatchMutationResult.Closed)
            {
                return ClosedMatchdayResponse();
            }
            if (result == MatchMutationResult.Conflict)
            {
                return await ConflictMatchdayResponse(match.PartitionKey);
            }

            var matches = await _matchService.GetAllMatchesAsync(match.PartitionKey);
            return Ok(matches);
        }

        [HttpPost("[action]")]
        public async Task<IActionResult> ReadMatches(string partitionKey)
        {
            var matches = await _matchService.GetAllMatchesAsync(partitionKey);
            return Ok(matches);
        }

        [HttpPost("[action]")]
        public async Task<IActionResult> AddPlayerSwap(
            [FromBody] PlayerSwapEntity playerSwap)
        {
            var result = await _matchService.AddPlayerSwapAsync(playerSwap);
            if (result == MatchMutationResult.Closed)
            {
                return ClosedMatchdayResponse();
            }
            if (result == MatchMutationResult.Conflict)
            {
                var authoritativeSwaps =
                    await _matchService.GetAllPlayerSwapsAsync(
                        playerSwap.PartitionKey);
                return Conflict(new
                {
                    IsConflict = true,
                    Message = "Another lineup update was saved first. The latest player swaps were loaded.",
                    PlayerSwaps = authoritativeSwaps
                });
            }

            return Ok(await _matchService.GetAllPlayerSwapsAsync(
                playerSwap.PartitionKey));
        }

        [HttpPost("[action]")]
        public async Task<IActionResult> ReadPlayerSwaps(string partitionKey)
        {
            return Ok(await _matchService.GetAllPlayerSwapsAsync(
                partitionKey));
        }

        [HttpPost("[action]")]
        public async Task<IActionResult> DoneAndReadMatches(
            string partitionKey,
            string? ownerId = null)
        {
            try
            {
                var effectiveOwnerId =
                    RequestUserId.ResolveOptional(User, ownerId);
                var matches = await _matchService.FinalizeMatchday(
                    partitionKey,
                    effectiveOwnerId);
                return Ok(matches);
            }
            catch (MatchdayConcurrencyException)
            {
                return await ConflictMatchdayResponse(partitionKey);
            }
        }

        [HttpPost("[action]")]
        public async Task<IActionResult> EditMatch(
            [FromBody] MatchEntity match,
            string? ownerId = null)
        {
            var result = await _matchService.EditMatch(match);
            if (result == MatchMutationResult.Closed)
            {
                return ClosedMatchdayResponse();
            }
            if (result == MatchMutationResult.Succeeded)
            {
                var matches = await _matchService.GetAllMatchesAsync(match.PartitionKey);
                return Ok(matches);
            }
            if (result == MatchMutationResult.NotFound)
            {
                return NotFound();
            }
            return await ConflictMatchdayResponse(match.PartitionKey);
        }

        [HttpPost("[action]")]
        public async Task<IActionResult> DeleteMatch(
            [FromBody] MatchEntity match,
            string? ownerId = null)
        {
            var result = await _matchService.DeleteMatch(match);
            if (result == MatchMutationResult.Closed)
            {
                return ClosedMatchdayResponse();
            }
            if (result == MatchMutationResult.Succeeded)
            {
                var matches = await _matchService.GetAllMatchesAsync(match.PartitionKey);
                return Ok(matches);
            }
            if (result == MatchMutationResult.NotFound)
            {
                return NotFound();
            }
            return await ConflictMatchdayResponse(match.PartitionKey);
        }

        [HttpPost("[action]")]
        public async Task<IActionResult> GetAiSummary(
            [FromHeader(Name = "client_version")] string ver,
            [FromBody] dynamic matchesHistory,
            CancellationToken cancellationToken = default)
        {
            string language = "he";
            var reply = await _aiService.GetResponseFromAgent(matchesHistory, language, cancellationToken);
            return Ok(reply);
        }

        [HttpPost("[action]")]
        public async Task<IActionResult> GetMatchday([FromHeader(Name = "client_version")] string ver, string partitionKey)
        {
            var matchday = await _matchService.GetMatchday(partitionKey);
            if(matchday == null)
            {
                return NotFound();
            }

            return Ok(matchday);
        }

        [HttpPost("[action]")]
        public async Task<IActionResult> StartScoreboard([FromHeader(Name = "client_version")] string ver, string partitionKey)
        {
            return await _matchService.StartMatchday(partitionKey)
                ? Ok()
                : BadRequest();

        }

        private IActionResult ClosedMatchdayResponse()
        {
            return Ok(new
            {
                IsClosed = true,
                Message = "Matchday is closed. No further matches can be added."
            });
        }

        private async Task<IActionResult> ConflictMatchdayResponse(
            string partitionKey)
        {
            var matches = await _matchService.GetAllMatchesAsync(partitionKey);
            return Conflict(new
            {
                IsConflict = true,
                Message = "Another user updated the scoreboard. The latest saved matches were loaded.",
                Matches = matches
            });
        }

        private static string GetString(JObject source, params string[] names)
        {
            foreach (var name in names)
            {
                var property = source.Properties()
                    .FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
                var value = property?.Value?.ToString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return string.Empty;
        }

    }
}