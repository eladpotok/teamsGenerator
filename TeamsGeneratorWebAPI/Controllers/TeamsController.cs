using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using System.Text.Json;
using TeamsGenerator.Ai;
using TeamsGenerator.Algos.BackAndForthAlgo;
using TeamsGenerator.Algos.SkillWiseAlgo;
using TeamsGenerator.API;
using TeamsGenerator.Utilities;
using TeamsGeneratorWebAPI.Clients;
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
            _telemetryClient.TrackEvent("GetTeams");
            _telemetryClient.TrackMetric("GetTeams", 1);
            IReadOnlyDictionary<string, double>? chemistryScores =
                string.IsNullOrWhiteSpace(ownerId)
                ? null
                : await _matchService.GetChemistryScores(ownerId);
            return WebAppAPI.GetTeams(dicJson, algoKey, chemistryScores);
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
            _telemetryClient.TrackEvent("SaveTeamsToStorage");
            _telemetryClient.TrackMetric("SaveTeamsToStorage", 1);
            return await _azureStorage.UploadAsync(teams, new TeamsBlobConfig() { UId = uid });
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
            var isClosed = await _matchService.IsClosed(match.PartitionKey);
            if (isClosed)
            {
                return Ok(new
                {
                    IsClosed = true,
                    Message = "Matchday is closed. No further matches can be added."
                });
            }

            await _matchService.AddMatchAsync(match);
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
        public async Task<IActionResult> DoneAndReadMatches(
            string partitionKey,
            string? ownerId = null)
        {
            var matches = await _matchService.GetAllMatchesAsync(partitionKey);
            if (!string.IsNullOrWhiteSpace(ownerId) && matches?.Count > 0)
            {
                await _matchService.StoreChemistryMatchday(
                    ownerId,
                    partitionKey,
                    matches);
            }
            await _matchService.DoneMatch(new MatchdayMetadataEntity() { PartitionKey = partitionKey, RowKey = AzureTableStorageService.RowKeyForCloseStatus, IsClosed = true  });
            return Ok(matches);
        }

        [HttpPost("[action]")]
        public async Task<IActionResult> EditMatch(
            [FromBody] MatchEntity match,
            string? ownerId = null)
        {
            var succeeded = await _matchService.EditMatch(match);
            if(succeeded)
            {
                var matches = await _matchService.GetAllMatchesAsync(match.PartitionKey);
                await RefreshChemistryIfClosed(
                    ownerId,
                    match.PartitionKey,
                    matches);
                return Ok(matches);
            }
            return NotFound();
        }

        [HttpPost("[action]")]
        public async Task<IActionResult> DeleteMatch(
            [FromBody] MatchEntity match,
            string? ownerId = null)
        {
            var succeeded = await _matchService.DeleteEntity(match);
            if (succeeded)
            {
                var matches = await _matchService.GetAllMatchesAsync(match.PartitionKey);
                await RefreshChemistryIfClosed(
                    ownerId,
                    match.PartitionKey,
                    matches);
                return Ok(matches);
            }
            return NotFound();
        }

        private async Task RefreshChemistryIfClosed(
            string? ownerId,
            string matchdayId,
            List<MatchEntity> matches)
        {
            if (string.IsNullOrWhiteSpace(ownerId)
                || !await _matchService.IsClosed(matchdayId))
            {
                return;
            }

            await _matchService.StoreChemistryMatchday(
                ownerId,
                matchdayId,
                matches);
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
            try
            {
                var matchdayMetadata = new MatchdayMetadataEntity() { PartitionKey = partitionKey, RowKey = AzureTableStorageService.RowKeyForStartStatus, IsClosed = false };
                await _matchService.AddEntity<MatchdayMetadataEntity>(matchdayMetadata);
                return Ok();
            }
            catch (Exception)
            {
                return BadRequest();
            }

        }
    }
}