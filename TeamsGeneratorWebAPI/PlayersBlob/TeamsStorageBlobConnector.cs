using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text;
using TeamsGenerator.Algos.SkillWiseAlgo;
using TeamsGenerator.API;
using TeamsGenerator.Orchestration;
using TeamsGenerator.Orchestration.Contracts;
using TeamsGeneratorWebAPI.Storage;
using TeamsGeneratorWebAPI.Clients;

namespace TeamsGeneratorWebAPI.PlayersBlob
{
    public interface ITeamsStorageBlobConnector : IAzureStorage
    {
        Task<TeamsStorageWriteResponse> UploadVersionedAsync(
            JObject snapshot, TeamsBlobConfig config, string expectedRevision);
    }

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
            var teamsConfig = config as TeamsBlobConfig;
            BlobContainerClient container = new BlobContainerClient(_storageConnectionString, _storageContainerName);
            BlobClient client = container.GetBlobClient(GetTeamsPath(teamsConfig));
            try
            {
                var content = await client.DownloadContentAsync();
                return new GetTeamsFromStorageResponse(
                    (JObject)PrivatePlayerFields.RemoveFromSnapshot(JObject.Parse(content.Value.Content.ToString())))
                {
                    Revision = content.Value.Details.ETag.ToString(),
                    Exists = true
                };
            }
            catch (RequestFailedException exception)
                when (exception.Status == 404 && exception.ErrorCode == "BlobNotFound")
            {
                return GetTeamsFromStorageResponse.Failure("No teams have been saved yet.");
            }
        }

        public Task<IResponse> UploadAsync(dynamic teams, IConfig config) =>
            Task.FromResult<IResponse>(TeamsStorageWriteResponse.UpgradeRequired());

        public async Task<TeamsStorageWriteResponse> UploadVersionedAsync(
            JObject snapshot, TeamsBlobConfig config, string expectedRevision)
        {
            if (expectedRevision != SafeWriteContract.AbsentRevision
                && !AzureTableStorageService.HasExpectedRevision(expectedRevision))
                return TeamsStorageWriteResponse.UpgradeRequired();
            BlobContainerClient container = new BlobContainerClient(_storageConnectionString, _storageContainerName);
            BlobClient client = container.GetBlobClient(
                GetTeamsPath(config));
            using (MemoryStream ms = new MemoryStream(Encoding.UTF8.GetBytes(
                PrivatePlayerFields.RemoveFromSnapshot(snapshot).ToString(Formatting.None))))
            {
                try
                {
                    var saved = await client.UploadAsync(ms, new BlobUploadOptions
                    {
                        Conditions = expectedRevision == SafeWriteContract.AbsentRevision
                            ? new BlobRequestConditions { IfNoneMatch = ETag.All }
                            : new BlobRequestConditions { IfMatch = new ETag(expectedRevision) }
                    });
                    return new TeamsStorageWriteResponse
                    {
                        Success = true,
                        Revision = saved.Value.ETag.ToString()
                    };
                }
                catch (RequestFailedException exception)
                    when ((exception.Status == 412 && exception.ErrorCode == "ConditionNotMet")
                        || (exception.Status == 409 && exception.ErrorCode == "BlobAlreadyExists")
                        || (exception.Status == 404 && exception.ErrorCode == "BlobNotFound"))
                {
                    return new TeamsStorageWriteResponse
                    {
                        IsConflict = true,
                        ErrorCode = "stale_revision",
                        Error = "The saved teams changed. Reload them and review your changes before saving again."
                    };
                }
            }
        }

        private static string GetTeamsPath(TeamsBlobConfig config)
        {
            return string.IsNullOrWhiteSpace(config.GroupId)
                || string.Equals(
                    config.GroupId,
                    PlayersStorageBlobConnector.DefaultGroupId,
                    StringComparison.OrdinalIgnoreCase)
                ? $"{config.UId}_teams"
                : $"{config.UId}_player_groups/{config.GroupId}/teams";
        }
    }
}
