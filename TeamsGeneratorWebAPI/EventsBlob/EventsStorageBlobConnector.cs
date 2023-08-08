using Azure.Storage.Blobs;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text;
using TeamsGenerator.Algos.SkillWiseAlgo;
using TeamsGenerator.API;
using TeamsGenerator.Orchestration;
using TeamsGenerator.Orchestration.Contracts;
using TeamsGeneratorWebAPI.EventsBlob;
using TeamsGeneratorWebAPI.Storage;

namespace TeamsGeneratorWebAPI.PlayersBlob
{
    public interface IEventsStorageBlobConnector : IAzureStorage 
    {
        Task<bool> SubscribeToUser(string subscribeRequesterUid, string subscribeToUid);

        Task<GetEventsResposne> GetEvents(string uid);

        Task<bool> CreateEvent(string uid, dynamic eventConfig);
    }

    public class EventsStorageBlobConnector : IEventsStorageBlobConnector
    {
        private readonly string _storageConnectionString;
        private readonly string _storageContainerName;
        private readonly ILogger<EventsStorageBlobConnector> _logger;

        public EventsStorageBlobConnector(IConfiguration configuration, ILogger<EventsStorageBlobConnector> logger)
        {
            _storageConnectionString = configuration.GetValue<string>("BlobConnectionString");
            _storageContainerName = configuration.GetValue<string>("EventsBlobContainerName");
            _logger = logger;
        }

        public Task<IResponse> DeleteAsync(string blobFilename)
        {
            throw new NotImplementedException();
        }

        public Task<IResponse> DownloadAsync(string blobFilename)
        {
            throw new NotImplementedException();
        }

        public async Task<GetEventsResposne> GetEvents(string uid)
        {
            try
            {
                BlobContainerClient container = new BlobContainerClient(_storageConnectionString, _storageContainerName);

                BlobClient client = container.GetBlobClient($"{uid}_subscribes");
                if (client == null) return GetEventsResposne.Failure("events were not found");

                if (await client.ExistsAsync())
                {
                    var data = await client.OpenReadAsync();
                    Stream blobContent = data;

                    var content = await client.DownloadContentAsync();
                    var value = content.Value;
                    var json = value.Content;

                    var subscribes = JsonConvert.DeserializeObject<List<string>>(json.ToString());

                    var results = new List<EventData>();
                    foreach (var subscribeToUid in subscribes)
                    {
                        var eventData = await GetEvent(subscribeToUid);
                        if(eventData != null)
                        {
                            results.Add(eventData);
                        }
                    }

                    return new GetEventsResposne(results);
                }
            }
            catch (Exception e)
            {
                return GetEventsResposne.Failure(e.Message);
            }

            return GetEventsResposne.Failure("events were not found");
        }
        

        public async Task<EventData> GetEvent(string eventKey)
        {
            try
            {
                BlobContainerClient container = new BlobContainerClient(_storageConnectionString, _storageContainerName);

                BlobClient client = container.GetBlobClient($"{eventKey}");
                if (client == null) return null;

                if (await client.ExistsAsync())
                {
                    var data = await client.OpenReadAsync();
                    Stream blobContent = data;

                    var content = await client.DownloadContentAsync();
                    var value = content.Value;
                    var json = value.Content;

                    var eventData = JsonConvert.DeserializeObject<EventData>(json.ToString());
                    return eventData;
                }
            }
            catch (Exception e)
            {
                return null;
            }

            return null;
        }

        public async Task<IResponse> ListAsync(IConfig config)
        {
            try
            {
                var getEventConfig = config as GetEventsConfig;
                BlobContainerClient container = new BlobContainerClient(_storageConnectionString, _storageContainerName);

                BlobClient client = container.GetBlobClient($"{getEventConfig.Uid}_events");
                if (client == null) return GetEventsResposne.Failure("events were not found");

                if (await client.ExistsAsync())
                {
                    var data = await client.OpenReadAsync();
                    Stream blobContent = data;

                    var content = await client.DownloadContentAsync();
                    var value = content.Value;
                    var json = value.Content;

                    var eventData = JsonConvert.DeserializeObject<IEnumerable<EventData>>(json.ToString());

                    return new GetEventsResposne(eventData);
                }
            }
            catch (Exception e)
            {
                return GetEventsResposne.Failure(e.Message);
            }

            return GetEventsResposne.Failure("events were not found");
        }

        public async Task<bool> SubscribeToUser(string subscribeRequesterUid, string subscribeToUid)
        {
            try
            {
                BlobContainerClient container = new BlobContainerClient(_storageConnectionString, _storageContainerName);
                BlobClient client = container.GetBlobClient($"{subscribeRequesterUid}_subscribes");
                if (client == null) return false;

                if (await client.ExistsAsync())
                {
                    var data = await client.OpenReadAsync();
                    Stream blobContent = data;

                    var content = await client.DownloadContentAsync();
                    var value = content.Value;
                    var json = value.Content;

                    var subscribes = JsonConvert.DeserializeObject<List<string>>(json.ToString());
                    subscribes.Add(subscribeToUid);

                    var subscribeToSave = JsonConvert.SerializeObject(subscribes);

                    using (MemoryStream ms = new MemoryStream(Encoding.UTF8.GetBytes(subscribeToSave.ToString())))
                    {
                        await client.UploadAsync(ms, overwrite: true);
                    }

                    return true;
                }
                else
                {
                    var subscribes = new List<string>() { subscribeToUid };

                    var subscribeToSave = JsonConvert.SerializeObject(subscribes);

                    using (MemoryStream ms = new MemoryStream(Encoding.UTF8.GetBytes(subscribeToSave.ToString())))
                    {
                        await client.UploadAsync(ms, overwrite: true);
                    }

                    return true;
                }
            }
            catch (Exception e)
            {
                return false;
            }

            return false;
        }

        public async Task<IResponse> UploadAsync(dynamic players, IConfig config)
        {
            var playersConfig = config as PlayersBlobConfig;
            BlobContainerClient container = new BlobContainerClient(_storageConnectionString, _storageContainerName);
            BlobClient client = container.GetBlobClient($"{playersConfig.UId}_players");
            using (MemoryStream ms = new MemoryStream(Encoding.UTF8.GetBytes(players.ToString())))
            {
                await client.UploadAsync(ms, overwrite: true);
            }

            return new SavePlayersResponse();
        }

        public Task<bool> CreateEvent(string uid)
        {
            throw new NotImplementedException();
        }

        public async Task<bool> CreateEvent(string uid, dynamic eventConfig)
        {
            try
            {
                var eventGuid = Guid.NewGuid();
                BlobContainerClient container = new BlobContainerClient(_storageConnectionString, _storageContainerName);
                BlobClient client = container.GetBlobClient($"{eventGuid}_event");
                if (client == null) return false;

                while (await client.ExistsAsync())
                {
                    eventGuid = Guid.NewGuid();
                    client = container.GetBlobClient($"{eventGuid}_event");
                }

                var subscribeToSave = JsonConvert.DeserializeObject()

            }
            catch (Exception e)
            {
                return false;
            }

            return false;
        }
    }
}
