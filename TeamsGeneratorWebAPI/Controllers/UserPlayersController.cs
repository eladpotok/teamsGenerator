using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TeamsGenerator.Algos.BackAndForthAlgo;
using TeamsGenerator.Algos.SkillWiseAlgo;
using TeamsGenerator.API;
using TeamsGenerator.Orchestration.Contracts;
using TeamsGeneratorWebAPI.DesignCreator;
using TeamsGeneratorWebAPI.Authentication;
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
            var userId = RequestUserId.Resolve(User, uid);
            var config = new PlayersBlobConfig() { UId = userId, AlgoType = algoKey };
            _telemetryClient.TrackMetric("PlayerAdded", 1);
            return await  _azureStorage.UploadAsync(players, config);
        }

        [HttpPost("SharePlayers")]
        public async Task<IActionResult> PostSharePlayers([FromHeader(Name = "client_version")] string ver, [FromBody] dynamic config, string uid)
        {
            JObject request = config as JObject ?? JObject.FromObject(config);
            var playersList = request["players"]?
                .Children()
                .Select((player, index) =>
                {
                    if (player.Type == JTokenType.String)
                    {
                        return new PlayerShareItem
                        {
                            Name = player.ToString()
                        };
                    }

                    var playerObject = player as JObject ?? new JObject();
                    return new PlayerShareItem
                    {
                        Name = GetString(playerObject, "name"),
                        IsWaiting =
                            playerObject["isWaiting"]?.Value<bool>() == true,
                        WaitingListOrder =
                            playerObject["waitingListOrder"]?.Value<int?>()
                    };
                })
                .Where(player => !string.IsNullOrWhiteSpace(player.Name))
                .Select(player =>
                {
                    player.Name = player.Name.Trim();
                    return player;
                })
                .OrderBy(player => player.IsWaiting)
                .ThenBy(player => player.IsWaiting
                    ? player.WaitingListOrder ?? int.MaxValue
                    : 0)
                .ToList() ?? new List<PlayerShareItem>();
            var teamInfo = request["teamInfo"] as JObject ?? new JObject();

            var teamName = GetString(teamInfo, "teamName", "matchName");
            var location = GetString(teamInfo, "location", "venue");
            var date = GetString(teamInfo, "date", "eventDate");
            var dayInWeek = GetString(teamInfo, "dayInWeek");
            var culture = GetString(teamInfo, "currentCulture", "culture");

            using var ms = ImageCreator.CreatePlayersList(
                playersList,
                teamName,
                location,
                date,
                dayInWeek,
                string.IsNullOrWhiteSpace(culture) ? "en-US" : culture);

            // Convert the image to a byte array and add it to the result list
            byte[] imageBytes = ms.ToArray();

            _telemetryClient.TrackMetric("SharePlayersWithImage", 1);
            return File(imageBytes, "image/png");
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


        [HttpGet(Name = "UserPlayersController")]

        public async Task<IResponse> Get([FromHeader(Name = "client_version")] string ver, string uid, int algoType)
        {
            var config = new PlayersBlobConfig() { UId = uid, AlgoType = algoType };
            return await _azureStorage.ListAsync(config);
        }
    }

}