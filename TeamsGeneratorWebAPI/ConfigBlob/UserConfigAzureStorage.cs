using Azure.Storage.Blobs;
using Newtonsoft.Json;
using System.Text;
using TeamsGenerator.Algos.SkillWiseAlgo;
using TeamsGenerator.API;
using TeamsGenerator.Orchestration;
using TeamsGenerator.Orchestration.Contracts;
using TeamsGeneratorWebAPI.ConfigBlob;
using TeamsGeneratorWebAPI.Storage;

namespace TeamsGeneratorWebAPI.PlayersBlob
{
    public interface IUserConfigAzureStorage : IAzureStorage { }

    public class UserConfigAzureStorage : IUserConfigAzureStorage
    {
        private readonly string _storageConnectionString;
        private readonly string _storageContainerName;
        private readonly ILogger<UserConfigAzureStorage> _logger;

        public UserConfigAzureStorage(IConfiguration configuration, ILogger<UserConfigAzureStorage> logger)
        {
            _storageConnectionString = configuration.GetValue<string>("BlobConnectionString");
            _storageContainerName = configuration.GetValue<string>("UserConfigBlobContainerName");
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
                var userConfig = config as UserConfigBlobConfig;
                BlobContainerClient container = new BlobContainerClient(_storageConnectionString, _storageContainerName);

                BlobClient client = container.GetBlobClient($"{userConfig.UId}_config");
                if (client == null) return GetPlayersResponse.Failure("Players were not found");

                if (await client.ExistsAsync())
                {
                    var data = await client.OpenReadAsync();
                    Stream blobContent = data;

                    var content = await client.DownloadContentAsync();
                    var value = content.Value;
                    var json = value.Content;
                    var serializedConfig = JsonConvert.DeserializeObject<UserConfigResponse>(json.ToString());

                    return new GetConfigResponse(serializedConfig);
                }
            }
            catch (Exception e)
            {
                return GetConfigResponse.Failure(e.Message);
            }

            return GetConfigResponse.Failure("config isn't found");
        }

        public async Task<IResponse> UploadAsync(dynamic configs, IConfig config)
        {
            var userConfig = config as UserConfigBlobConfig;
            BlobContainerClient container = new BlobContainerClient(_storageConnectionString, _storageContainerName);
            BlobClient client = container.GetBlobClient($"{userConfig.UId}_config");
            using (MemoryStream ms = new MemoryStream(Encoding.UTF8.GetBytes(configs.ToString())))
            {
                await client.UploadAsync(ms, overwrite: true);
            }

            return new SaveConfigResponse();
        }
    }
}
