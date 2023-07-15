using Microsoft.AspNetCore.Mvc;
using TeamsGenerator;
using TeamsGenerator.API;
using TeamsGeneratorWebAPI.ConfigBlob;
using TeamsGeneratorWebAPI.PlayersBlob;
using TeamsGeneratorWebAPI.Storage;

namespace TeamsGeneratorWebAPI.Controllers
{

    [ApiController]
    [Route("[controller]")]
    public class ConfigController : ControllerBase
    {
        private readonly ILogger<HomeController> _logger;
        private readonly IUserConfigAzureStorage _azureStorage;

        public ConfigController(ILogger<HomeController> logger, IUserConfigAzureStorage azureStorage)
        {
            _logger = logger;
            _azureStorage = azureStorage;
        }

        [HttpPost("Upload")]
        public async Task<SaveConfigResponse> Post([FromBody] dynamic players, string uid)
        {
            var config = new UserConfigBlobConfig() { UId = uid };
            return await _azureStorage.UploadAsync(players, config);
        }


        [HttpGet(Name = "ConfigController")]

        public async Task<IResponse> Get(string uid)
        {
            var config = new UserConfigBlobConfig() { UId = uid };
            return await _azureStorage.ListAsync(config);
        }
    }
 
}