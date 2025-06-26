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
    public class ControlPanelController : ControllerBase
    {
        private readonly ILogger<HomeController> _logger;
        private readonly AzureTableStorageService _azuretableService;
        private readonly TelemetryClient _telemetryClient;


        public ControlPanelController(ILogger<HomeController> logger, AzureTableStorageService azuretableService, TelemetryClient telemetryClient)
        {
            _logger = logger;
            _azuretableService = azuretableService;
            _telemetryClient = telemetryClient;
        }
        
        [HttpPost("[action]")]
        public async Task<IActionResult> AddUpdate([FromBody] UpdateEntity update)
        {
            await _azuretableService.AddUpdate(update);
            return Ok();
        }


        [HttpPost("[action]")]
        public async Task<IActionResult> GetAllUpdates(string partitionKey)
        {
            var updates = await _azuretableService.GetAllEntities<UpdateEntity>(partitionKey);
            return Ok(updates);
        }

        [HttpPost("[action]")]
        public async Task<IActionResult> GetAllFeedbacks(string partitionKey)
        {
            var feedback = await _azuretableService.GetAllEntities<FeedbackEntity>(partitionKey);
            return Ok(feedback);
        }

        [HttpPost("[action]")]
        public async Task<IActionResult> AddFeedback([FromBody] FeedbackEntity feedback)
        {
            await _azuretableService.AddFeedback(feedback);
            return Ok();
        }

        [HttpPost("[action]")]
        public async Task<IActionResult> DeleteUpdate([FromBody] UpdateEntity update)
        {
            await _azuretableService.DeleteEntity(update);
            return Ok();
        }

        [HttpPost("[action]")]
        public async Task<IActionResult> DeleteFeedback([FromBody] FeedbackEntity feedback)
        {
            await _azuretableService.DeleteEntity(feedback);
            return Ok();
        }
    }
 
}