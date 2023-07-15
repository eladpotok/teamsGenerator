using Microsoft.AspNetCore.Mvc;
using TeamsGenerator;
using TeamsGenerator.API;
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

        public HomeController(ILogger<HomeController> logger, IUserConfigAzureStorage azureStorage)
        {
            _logger = logger;
            _azureStorage = azureStorage;
        }
        
        [HttpGet(Name = "AlgosController")]

        public async Task<GetAppSetupResponse> Get(string uid)
        {
            var config = new UserConfigBlobConfig() { UId = uid };
            var response = await _azureStorage.ListAsync(config) as GetConfigResponse;
            var appSetup = WebAppAPI.GetAppSetup();

            if (response.Config != null)
            {
                appSetup.Config = response.Config;
            }

            return appSetup;
        }
    }
 
}