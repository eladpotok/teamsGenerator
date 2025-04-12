using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using TeamsGenerator.Algos.BackAndForthAlgo;
using TeamsGenerator.Algos.SkillWiseAlgo;
using TeamsGenerator.API;
using TeamsGenerator.Orchestration.Contracts;
using TeamsGeneratorWebAPI.DesignCreator;
using TeamsGeneratorWebAPI.PlayersBlob;
using TeamsGeneratorWebAPI.Storage;

namespace TeamsGeneratorWebAPI.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class UserPlayersController : ControllerBase
    {
        private readonly ILogger<UserPlayersController> _logger;
        private readonly IPlayersStorageBlobConnector _azureStorage;
        private readonly TelemetryClient _telemetryClient;

        public UserPlayersController(ILogger<UserPlayersController> logger, IPlayersStorageBlobConnector azureStorage, TelemetryClient telemetryClient)
        {
            _logger = logger;
            _azureStorage = azureStorage;
            _telemetryClient = telemetryClient;
        }

        [HttpPost("Upload")]
        public async Task<SavePlayersResponse> Post([FromHeader(Name = "client_version")] string ver, [FromBody] dynamic players, string uid, int algoKey)
        {
            var config = new PlayersBlobConfig() { UId = uid, AlgoType = algoKey };
            _telemetryClient.TrackMetric("PlayerAdded", 1);
            return await  _azureStorage.UploadAsync(players, config);
        }

        [HttpPost("SharePlayers")]
        public async Task<IActionResult> PostSharePlayers([FromHeader(Name = "client_version")] string ver, [FromBody] dynamic config, string uid)
        {
            var teamsSerializedObject = JsonConvert.SerializeObject(config.players, Newtonsoft.Json.Formatting.Indented);
            IEnumerable<string> playersList = JsonConvert.DeserializeObject<List<string>>(teamsSerializedObject);

            var teamInfo = config.teamInfo;
            var culture = teamInfo.currentCulture ?? "en-us";
            var ms = ImageCreator.CreatePlayersList(playersList.ToList(), teamInfo.teamName.ToString(), teamInfo.location.ToString(), teamInfo.date.ToString(), teamInfo.dayInWeek.ToString(), culture.ToString());

            // Convert the image to a byte array and add it to the result list
            byte[] imageBytes = ms.ToArray();

            _telemetryClient.TrackMetric("SharePlayersWithImage", 1);
            return File(imageBytes, "image/png");
        }


        [HttpGet(Name = "UserPlayersController")]

        public async Task<IResponse> Get([FromHeader(Name = "client_version")] string ver, string uid, int algoType)
        {
            var config = new PlayersBlobConfig() { UId = uid, AlgoType = algoType };
            return await _azureStorage.ListAsync(config);
        }
    }

}