using Azure.Storage.Blobs;
using Newtonsoft.Json;
using System.Text;
using TeamsGenerator.Algos.SkillWiseAlgo;
using TeamsGenerator.API;
using TeamsGenerator.Orchestration;
using TeamsGenerator.Orchestration.Contracts;
using TeamsGeneratorWebAPI.Storage;

namespace TeamsGeneratorWebAPI.PlayersBlob
{
    public interface IPlayersStorageBlobConnector : IAzureStorage { }

    public class PlayersStorageBlobConnector : IPlayersStorageBlobConnector
    {
        private readonly string _storageConnectionString;
        private readonly string _storageContainerName;
        private readonly ILogger<PlayersStorageBlobConnector> _logger;

        public PlayersStorageBlobConnector(IConfiguration configuration, ILogger<PlayersStorageBlobConnector> logger)
        {
            _storageConnectionString = configuration.GetValue<string>("BlobConnectionString");
            _storageContainerName = configuration.GetValue<string>("PlayersBlobContainerName");
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
                var playersConfig = config as PlayersBlobConfig;
                BlobContainerClient container = new BlobContainerClient(_storageConnectionString, _storageContainerName);

                BlobClient client = container.GetBlobClient($"{playersConfig.UId}_players");
                if (client == null) return GetPlayersResponse.Failure("Players were not found");

                if (await client.ExistsAsync())
                {
                    var data = await client.OpenReadAsync();
                    Stream blobContent = data;

                    var content = await client.DownloadContentAsync();
                    var value = content.Value;
                    var json = value.Content;

                    var algoKeyEnum = (AlgoType)playersConfig.AlgoType;
                    var players = WebAppAPI.AlgoTypeToPlayerSerializerMapper[algoKeyEnum](json.ToString());

                    return new GetPlayersResponse(players);
                }
            }
            catch (Exception e)
            {
                return GetPlayersResponse.Failure(e.Message);
            }

            return GetPlayersResponse.Failure("Players were not found");
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
    }
}
