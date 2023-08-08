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
    public class EventsController : ControllerBase
    {
        private readonly ILogger<EventsController> _logger;
        private readonly IEventsStorageBlobConnector _azureStorage;

        public EventsController(ILogger<EventsController> logger, IEventsStorageBlobConnector azureStorage)
        {
            _logger = logger;
            _azureStorage = azureStorage;
        }

        [HttpPost("Subscribe")]
        public async Task<bool> Subscribe(string subscribeRequesterUid, string subscribeToUid, string version)
        {
            return await _azureStorage.SubscribeToUser(subscribeRequesterUid, subscribeToUid);
        }

        [HttpPost("GetEvents")]
        public async Task<GetEventsResposne> GetEvents(string uid, string version)
        {
            return await _azureStorage.GetEvents(uid);
        }

    }

}