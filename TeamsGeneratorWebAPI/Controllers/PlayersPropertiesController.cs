using Microsoft.AspNetCore.Mvc;
using TeamsGenerator.API;
using TeamsGenerator.Orchestration;

namespace TeamsGeneratorWebAPI.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class PlayersPropertiesController : ControllerBase
    {
        private readonly ILogger<PlayersPropertiesController> _logger;

        public PlayersPropertiesController(ILogger<PlayersPropertiesController> logger)
        {
            _logger = logger;
        }

        [HttpGet(Name = "PlayersPropertiesController")]

        public IEnumerable<PlayerProperties> Get([FromHeader(Name = "client_version")] string ver, int algoKey)
        {
            return WebAppAPI.GetPlayersProperties(algoKey);
        }
    }
 
}