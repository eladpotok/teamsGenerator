using Microsoft.AspNetCore.Mvc;
using TeamsGenerator;
using TeamsGenerator.API;
using TeamsGeneratorWebAPI.ConfigBlob;
using TeamsGeneratorWebAPI.PlayersBlob;
using TeamsGeneratorWebAPI.Storage;
using TeamsGeneratorWebAPI.UsersBlob;

namespace TeamsGeneratorWebAPI.Controllers
{

    [ApiController]
    [Route("[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly ILogger<HomeController> _logger;
        private readonly IUserAzureStorage _azureStorage;

        public AuthController(ILogger<HomeController> logger, IUserAzureStorage azureStorage)
        {
            _logger = logger;
            _azureStorage = azureStorage;
        }

        [HttpPost("Sign")]
        public async Task<IResponse> Sign([FromHeader(Name = "client_version")] string ver, [FromBody] dynamic user)
        {
            var config = new UserBlobConfig() { User = user };
            return await _azureStorage.UploadAsync(user, config);
        }


        [HttpPost("Login")]

        public async Task<IResponse> Login([FromHeader(Name = "client_version")] string ver, [FromBody] dynamic user)
        {
            var config = new UserBlobConfig() { User = user };
            return await _azureStorage.ListAsync(config);
        }
    }
 
}