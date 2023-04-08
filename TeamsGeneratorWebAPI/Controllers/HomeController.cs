using Microsoft.AspNetCore.Mvc;
using TeamsGenerator;
using TeamsGenerator.API;

namespace TeamsGeneratorWebAPI.Controllers
{

    [ApiController]
    [Route("[controller]")]
    public class HomeController : ControllerBase
    {
        private readonly ILogger<HomeController> _logger;

        public HomeController(ILogger<HomeController> logger)
        {
            _logger = logger;
        }
        
        [HttpGet(Name = "AlgosController")]

        public InitialAppConfig Get()
        {
            return WebAppAPI.GetInitialAlgoConfig();
        }
    }
 
}