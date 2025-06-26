using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Mvc;
using TeamsGenerator;
using TeamsGenerator.API;
using TeamsGeneratorWebAPI.Clients;
using TeamsGeneratorWebAPI.ConfigBlob;
using TeamsGeneratorWebAPI.PlayersBlob;

namespace TeamsGeneratorWebAPI.Controllers
{

    [ApiController]
    [Route("[controller]")]
    public class HomeController : ControllerBase
    {
        private readonly ILogger<HomeController> _logger;
        private readonly IUserConfigAzureStorage _azureStorage;
        private readonly TelemetryClient _telemetryClient;
        private readonly AzureTableStorageService _azureTablesStorage;


        public HomeController(ILogger<HomeController> logger, IUserConfigAzureStorage azureStorage, TelemetryClient telemetryClient, AzureTableStorageService azureTablesStorage)
        {
            _logger = logger;
            _azureStorage = azureStorage;
            _telemetryClient = telemetryClient;
            _azureTablesStorage = azureTablesStorage;
        }
        
        [HttpGet(Name = "AlgosController")]

        public async Task<GetAppSetupResponse> Get([FromHeader(Name = "client_version")] string ver, string uid)
        {
            var config = new UserConfigBlobConfig() { UId = uid };
            var response = await _azureStorage.ListAsync(config) as GetConfigResponse;
            var appSetup = WebAppAPI.GetAppSetup(ver);

            if (response.Config != null)
            {
                appSetup.Config = response.Config;
            }

            var lastUpdate = await _azureTablesStorage.GetAllEntities<UpdateEntity>("2c607f3d-d645-41a5-ad4f-c96ab9737780");
            var lastReleaseVersion = lastUpdate.FirstOrDefault();
            appSetup.Config.CurrentVersion = lastReleaseVersion.VersionNumber;
            
            _telemetryClient.TrackMetric("UserEntered", 1);
            return appSetup;
        }


    }
 
}