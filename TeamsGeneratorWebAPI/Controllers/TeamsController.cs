using Microsoft.AspNetCore.Mvc;
using TeamsGenerator.Algos.BackAndForthAlgo;
using TeamsGenerator.Algos.SkillWiseAlgo;
using TeamsGenerator.API;

namespace TeamsGeneratorWebAPI.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class TeamsController : ControllerBase
    {
        private readonly ILogger<TeamsController> _logger;

        public TeamsController(ILogger<TeamsController> logger)
        {
            _logger = logger;
        }

        [HttpPost()]


        public GetTeamsResponse Post([FromBody] dynamic dicJson, int algoKey)
        {
            return WebAppAPI.GetTeams(dicJson, algoKey);
        }

        [HttpPost("[action]")]

        public GetTeamsResponse PostResultString([FromBody] dynamic dicJson)
        {
            return WebAppAPI.GetResultString(dicJson);
        }
    }
 
}