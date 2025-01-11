using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Mvc;
using TeamsGenerator.Algos.BackAndForthAlgo;
using TeamsGenerator.Algos.SkillWiseAlgo;
using TeamsGenerator.API;
using TeamsGenerator.Orchestration.Contracts;
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
        public async Task<SavePlayersResponse> Post([FromHeader(Name = "client_version")] string ver, [FromBody] dynamic players, string uid, int algoType)
        {
            var config = new PlayersBlobConfig() { UId = uid, AlgoType = algoType };
            _telemetryClient.TrackMetric("PlayerAdded", 1);
            return await  _azureStorage.UploadAsync(players, config);
        }


        [HttpGet(Name = "UserPlayersController")]

        public async Task<IResponse> Get([FromHeader(Name = "client_version")] string ver, string uid, int algoType)
        {
            var config = new PlayersBlobConfig() { UId = uid, AlgoType = algoType };
            return await _azureStorage.ListAsync(config);
        }
    }

}