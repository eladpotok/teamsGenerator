using Azure.Storage.Blobs;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text;
using TeamsGenerator.Algos.SkillWiseAlgo;
using TeamsGenerator.API;
using TeamsGenerator.Orchestration;
using TeamsGenerator.Orchestration.Contracts;
using TeamsGeneratorWebAPI.Storage;

namespace TeamsGeneratorWebAPI.PlayersBlob
{
    public interface ITeamsStorageBlobConnector : IAzureStorage { }

    public class TeamsStorageBlobConnector : ITeamsStorageBlobConnector
    {
        private readonly string _storageConnectionString;
        private readonly string _storageContainerName;
        private readonly ILogger<TeamsStorageBlobConnector> _logger;

        public TeamsStorageBlobConnector(IConfiguration configuration, ILogger<TeamsStorageBlobConnector> logger)
        {
            _storageConnectionString = configuration.GetValue<string>("BlobConnectionString");
            _storageContainerName = configuration.GetValue<string>("TeamsBlobContainerName");
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

        public async Task<IResponse> ListAsync(IConfig config)
        {
            try
            {
                var teamsConfig = config as TeamsBlobConfig;
                BlobContainerClient container = new BlobContainerClient(_storageConnectionString, _storageContainerName);

                BlobClient client = container.GetBlobClient($"{teamsConfig.UId}_teams");
                if (client == null) return GetTeamsFromStorageResponse.Failure("teams were not found");

                if (await client.ExistsAsync())
                {
                    var data = await client.OpenReadAsync();
                    Stream blobContent = data;

                    var content = await client.DownloadContentAsync();
                    var value = content.Value;
                    var json = value.Content;

                    return new GetTeamsFromStorageResponse(JObject.Parse(json.ToString()));
                }
            }
            catch (Exception e)
            {
                return GetPlayersResponse.Failure(e.Message);
            }

            return GetPlayersResponse.Failure("Players were not found");
        }

        public async Task<IResponse> UploadAsync(dynamic teams, IConfig config)
        {
            var playersConfig = config as TeamsBlobConfig;
            BlobContainerClient container = new BlobContainerClient(_storageConnectionString, _storageContainerName);
            BlobClient client = container.GetBlobClient($"{playersConfig.UId}_teams");
            using (MemoryStream ms = new MemoryStream(Encoding.UTF8.GetBytes(teams.ToString())))
            {
                await client.UploadAsync(ms, overwrite: true);
            }

            return new SavePlayersResponse();
        }
    }
}
