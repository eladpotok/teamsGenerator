using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TeamsGenerator.API;
using TeamsGenerator.Orchestration;
using TeamsGenerator.Orchestration.Contracts;
using TeamsGeneratorWebAPI.Storage;
using TeamsGeneratorWebAPI.Collaboration;

namespace TeamsGeneratorWebAPI.PlayersBlob
{
    public interface IPlayersStorageBlobConnector : IAzureStorage
    {
        Task<PlayerGroupsResponse> ListGroupsAsync(string userId);

        Task<PlayerGroupMutationResponse> InitializeDefaultGroupNameAsync(
            string userId,
            string name);

        Task<PlayerGroupMutationResponse> CreateGroupAsync(
            string userId,
            string name,
            int maximumOwnedGroups);

        Task<PlayerGroupMutationResponse> RenameGroupAsync(
            string userId,
            string groupId,
            string name);

        Task<SavePlayersResponse> DeleteGroupAsync(
            string userId,
            string groupId);

        Task<SavePlayersResponse> MovePlayerAsync(
            string userId,
            int algoType,
            MovePlayerRequest request);

        Task<SavePlayersResponse> CopyPlayerAsync(
            string userId,
            int algoType,
            MovePlayerRequest request);
    }

    public class PlayersStorageBlobConnector : IPlayersStorageBlobConnector
    {
        internal const string DefaultGroupId = "default";
        private const string DefaultGroupName = "Default group";
        private const int MaximumGroups = 20;
        private const int MaximumGroupUpdateAttempts = 3;
        private readonly BlobContainerClient _container;
        private readonly ILogger<PlayersStorageBlobConnector> _logger;
        private readonly IPlayerAssessmentService _assessments;

        public PlayersStorageBlobConnector(
            IConfiguration configuration,
            ILogger<PlayersStorageBlobConnector> logger,
            IPlayerAssessmentService assessments)
            : this(new BlobContainerClient(configuration.GetValue<string>("BlobConnectionString"),
                configuration.GetValue<string>("PlayersBlobContainerName")), logger, assessments)
        {
        }

        public PlayersStorageBlobConnector(
            BlobContainerClient container,
            ILogger<PlayersStorageBlobConnector> logger,
            IPlayerAssessmentService assessments)
        {
            _container = container;
            _logger = logger;
            _assessments = assessments;
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
                if (playersConfig == null
                    || !await GroupExistsAsync(
                        playersConfig.UId,
                        playersConfig.GroupId))
                {
                    return GetPlayersResponse.Failure(
                        "Player group was not found");
                }

                var client = GetContainer().GetBlobClient(
                    GetPlayersPath(playersConfig));
                if (await client.ExistsAsync())
                {
                    var content = await client.DownloadContentAsync();
                    var algoKey = (AlgoType)playersConfig.AlgoType;
                    var players =
                        WebAppAPI.AlgoTypeToPlayerSerializerMapper[algoKey](
                            RosterDocument.Players(JToken.Parse(content.Value.Content.ToString())).ToString(Formatting.None));
                    return new GetPlayersResponse(players) { Revision = content.Value.Details.ETag.ToString() };
                }
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Failed to load a player roster.");
                return GetPlayersResponse.Failure(
                    "Players could not be loaded");
            }

            var missing = GetPlayersResponse.Failure("Players were not found");
            missing.Revision = RosterDocument.MissingRevision;
            return missing;
        }

        public async Task<IResponse> UploadAsync(
            dynamic players,
            IConfig config)
        {
            var playersConfig = config as PlayersBlobConfig;
            if (playersConfig == null
                || !await GroupExistsAsync(
                    playersConfig.UId,
                    playersConfig.GroupId))
            {
                return SavePlayersResponse.Failure(
                    "Player group was not found");
            }

            JToken playerToken = players is JToken token
                ? token : JToken.FromObject((object)players);
            var sanitized = PrivatePlayerFields.RemoveFromSnapshot(playerToken) as JArray
                ?? throw new InvalidDataException("A player array is required.");
            var client = GetContainer().GetBlobClient(GetPlayersPath(playersConfig));
            JToken? existing = null;
            string? etag = null;
            try
            {
                var content = await client.DownloadContentAsync();
                existing = JToken.Parse(content.Value.Content.ToString());
                RosterDocument.Players(existing);
                etag = content.Value.Details.ETag.ToString();
            }
            catch (RequestFailedException exception) when (exception.Status == 404 && exception.ErrorCode == "BlobNotFound") { }
            if (!RosterDocument.CanSave(existing, etag, playersConfig.ExpectedRevision, sanitized))
                return RosterConflict(playersConfig.ExpectedRevision == null);
            if (playersConfig.MergeRevision != null
                && playersConfig.MergeRevision != (etag ?? RosterDocument.MissingRevision))
                return RosterConflict();
            try
            {
                var result = await client.UploadAsync(
                    BinaryData.FromString(RosterDocument.WithPlayers(existing, sanitized).ToString(Formatting.None)),
                    new BlobUploadOptions
                    {
                        Conditions = etag == null
                            ? new BlobRequestConditions { IfNoneMatch = ETag.All }
                            : new BlobRequestConditions { IfMatch = new ETag(etag) }
                    });
                return new SavePlayersResponse { Revision = result.Value.ETag.ToString() };
            }
            catch (RequestFailedException exception) when (exception.Status is 409 or 412)
            {
                return RosterConflict();
            }
        }

        private static SavePlayersResponse RosterConflict(bool legacyMembershipChange = false) => new()
        {
            Success = false, Conflict = true,
            Error = legacyMembershipChange
                ? "This save would remove, restore or duplicate an approved member. Reload the online roster before editing. Use the updated app to remove approved members."
                : "The online roster changed. Reload it before saving to avoid losing newly approved players."
        };

        public async Task<PlayerGroupsResponse> ListGroupsAsync(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return PlayerGroupsResponse.Failure(
                    "A user identifier is required");
            }

            try
            {
                var state = await DownloadGroupStateAsync(userId);
                var storedDefault = state.Groups.FirstOrDefault(group =>
                    NormalizeGroupId(group.Id) == DefaultGroupId);
                var groups = state.Groups.Where(group =>
                    IsCustomGroupId(group.Id));
                return PlayerGroupsResponse.Succeeded(
                    new[]
                    {
                        CreateDefaultGroup(
                            storedDefault?.Name,
                            storedDefault != null)
                    }
                        .Concat(groups.OrderBy(group => group.CreatedAt))
                        .ToList());
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Failed to load player groups.");
                return PlayerGroupsResponse.Failure(
                    "Player groups could not be loaded");
            }
        }

        public async Task<PlayerGroupMutationResponse>
            InitializeDefaultGroupNameAsync(string userId, string name)
        {
            var normalizedName = NormalizeGroupName(name);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return PlayerGroupMutationResponse.Failure(
                    "A user identifier is required");
            }
            if (normalizedName == null)
            {
                return PlayerGroupMutationResponse.Failure(
                    "Group names must contain between 1 and 60 characters");
            }

            for (var attempt = 0;
                attempt < MaximumGroupUpdateAttempts;
                attempt++)
            {
                var state = await DownloadGroupStateAsync(userId);
                var existing = state.Groups.FirstOrDefault(group =>
                    NormalizeGroupId(group.Id) == DefaultGroupId);
                if (existing != null)
                {
                    existing.IsPersisted = true;
                    return PlayerGroupMutationResponse.Succeeded(existing);
                }

                var group = CreateDefaultGroup(normalizedName);
                state.Groups.Insert(0, group);
                if (await TryUploadGroupsAsync(userId, state))
                {
                    group.IsPersisted = true;
                    return PlayerGroupMutationResponse.Succeeded(group);
                }
            }

            return PlayerGroupMutationResponse.Failure(
                "The group changed at the same time. Please try again.");
        }

        public async Task<PlayerGroupMutationResponse> CreateGroupAsync(
            string userId,
            string name,
            int maximumOwnedGroups)
        {
            var normalizedName = NormalizeGroupName(name);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return PlayerGroupMutationResponse.Failure(
                    "A user identifier is required");
            }
            if (normalizedName == null)
            {
                return PlayerGroupMutationResponse.Failure(
                    "Group names must contain between 1 and 60 characters");
            }

            for (var attempt = 0;
                attempt < MaximumGroupUpdateAttempts;
                attempt++)
            {
                var state = await DownloadGroupStateAsync(userId);
                var effectiveMaximum = maximumOwnedGroups > 0
                    ? Math.Min(maximumOwnedGroups, MaximumGroups)
                    : MaximumGroups;
                if (state.Groups.Count(group =>
                    IsCustomGroupId(group.Id)) >= effectiveMaximum - 1)
                {
                    return PlayerGroupMutationResponse.Failure(
                        $"An account can own at most {effectiveMaximum} groups");
                }
                if (GroupNameExists(state.Groups, normalizedName))
                {
                    return PlayerGroupMutationResponse.Failure(
                        "A group with this name already exists");
                }

                var group = new PlayerGroup
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = normalizedName,
                    CreatedAt = DateTimeOffset.UtcNow
                };
                state.Groups.Add(group);
                if (await TryUploadGroupsAsync(userId, state))
                {
                    group.IsPersisted = true;
                    return PlayerGroupMutationResponse.Succeeded(group);
                }
            }

            return PlayerGroupMutationResponse.Failure(
                "The group changed at the same time. Please try again.");
        }

        public async Task<PlayerGroupMutationResponse> RenameGroupAsync(
            string userId,
            string groupId,
            string name)
        {
            var normalizedGroupId = NormalizeGroupId(groupId);
            if (!IsValidGroupId(normalizedGroupId))
            {
                return PlayerGroupMutationResponse.Failure(
                    "Player group was not found");
            }

            var normalizedName = NormalizeGroupName(name);
            if (normalizedName == null)
            {
                return PlayerGroupMutationResponse.Failure(
                    "Group names must contain between 1 and 60 characters");
            }

            for (var attempt = 0;
                attempt < MaximumGroupUpdateAttempts;
                attempt++)
            {
                var state = await DownloadGroupStateAsync(userId);
                var group = state.Groups.FirstOrDefault(item =>
                    string.Equals(
                        NormalizeGroupId(item.Id),
                        normalizedGroupId,
                        StringComparison.OrdinalIgnoreCase));
                if (group == null && normalizedGroupId == DefaultGroupId)
                {
                    group = CreateDefaultGroup();
                    state.Groups.Insert(0, group);
                }
                if (group == null)
                {
                    return PlayerGroupMutationResponse.Failure(
                        "Player group was not found");
                }
                if (state.Groups.Any(item =>
                    !string.Equals(
                        NormalizeGroupId(item.Id),
                        normalizedGroupId,
                        StringComparison.OrdinalIgnoreCase)
                    && string.Equals(
                        item.Name,
                        normalizedName,
                        StringComparison.OrdinalIgnoreCase)))
                {
                    return PlayerGroupMutationResponse.Failure(
                        "A group with this name already exists");
                }

                group.Name = normalizedName;
                group.IsDefault = normalizedGroupId == DefaultGroupId;
                if (await TryUploadGroupsAsync(userId, state))
                {
                    group.IsPersisted = true;
                    return PlayerGroupMutationResponse.Succeeded(group);
                }
            }

            return PlayerGroupMutationResponse.Failure(
                "The group changed at the same time. Please try again.");
        }

        public async Task<SavePlayersResponse> DeleteGroupAsync(
            string userId,
            string groupId)
        {
            if (!IsCustomGroupId(groupId))
            {
                return SavePlayersResponse.Failure(
                    "The Default group cannot be deleted");
            }

            foreach (var algoType in Enum.GetValues<AlgoType>())
            {
                var players = await DownloadPlayersJsonAsync(
                    new PlayersBlobConfig
                    {
                        UId = userId,
                        GroupId = groupId,
                        AlgoType = (int)algoType
                    });
                if (players.Count > 0)
                {
                    return SavePlayersResponse.Failure(
                        "Move or remove every player before deleting this group");
                }
            }

            for (var attempt = 0;
                attempt < MaximumGroupUpdateAttempts;
                attempt++)
            {
                var state = await DownloadGroupStateAsync(userId);
                var removed = state.Groups.RemoveAll(item =>
                    string.Equals(
                        item.Id,
                        groupId,
                        StringComparison.OrdinalIgnoreCase));
                if (removed == 0)
                {
                    return SavePlayersResponse.Failure(
                        "Player group was not found");
                }
                if (await TryUploadGroupsAsync(userId, state))
                {
                    return new SavePlayersResponse();
                }
            }

            return SavePlayersResponse.Failure(
                "The group changed at the same time. Please try again.");
        }

        public async Task<SavePlayersResponse> MovePlayerAsync(
            string userId,
            int algoType,
            MovePlayerRequest request)
        {
            return await TransferPlayerAsync(
                userId,
                algoType,
                request,
                removeFromSource: true);
        }

        public async Task<SavePlayersResponse> CopyPlayerAsync(
            string userId,
            int algoType,
            MovePlayerRequest request)
        {
            return await TransferPlayerAsync(
                userId,
                algoType,
                request,
                removeFromSource: false);
        }

        private async Task<SavePlayersResponse> TransferPlayerAsync(
            string userId,
            int algoType,
            MovePlayerRequest request,
            bool removeFromSource)
        {
            if (request?.Player == null
                || !IsValidGroupId(request.FromGroupId)
                || !IsValidGroupId(request.ToGroupId)
                || string.Equals(
                    NormalizeGroupId(request.FromGroupId),
                    NormalizeGroupId(request.ToGroupId),
                    StringComparison.OrdinalIgnoreCase))
            {
                return SavePlayersResponse.Failure(
                    "A player and two different valid groups are required");
            }
            if (!Enum.IsDefined(typeof(AlgoType), algoType))
            {
                return SavePlayersResponse.Failure(
                    "The selected algorithm is not supported");
            }
            if (!await GroupExistsAsync(userId, request.FromGroupId)
                || !await GroupExistsAsync(userId, request.ToGroupId))
            {
                return SavePlayersResponse.Failure(
                    "Player group was not found");
            }

            var playerKey = request.Player["key"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(playerKey))
            {
                return SavePlayersResponse.Failure(
                    "The player identifier is required");
            }

            var rostersToMove = new List<RosterMove>();
            foreach (var currentAlgoType in Enum.GetValues<AlgoType>())
            {
                var sourceConfig = CreateConfig(
                    userId,
                    (int)currentAlgoType,
                    request.FromGroupId);
                var sourcePlayers =
                    await DownloadPlayersJsonAsync(sourceConfig);
                var sourcePlayer = FindPlayer(sourcePlayers, playerKey);
                if (sourcePlayer == null)
                {
                    continue;
                }

                var destinationConfig = CreateConfig(
                    userId,
                    (int)currentAlgoType,
                    request.ToGroupId);
                var destinationPlayers =
                    await DownloadPlayersJsonAsync(destinationConfig);
                rostersToMove.Add(new RosterMove
                {
                    SourceConfig = sourceConfig,
                    SourcePlayers = sourcePlayers,
                    SourcePlayer = sourcePlayer,
                    DestinationConfig = destinationConfig,
                    DestinationPlayers = destinationPlayers
                });
            }
            if (rostersToMove.Count == 0)
            {
                return SavePlayersResponse.Failure(
                    "Player was not found in the source group");
            }

            foreach (var roster in rostersToMove)
            {
                var existingDestination = FindPlayer(
                    roster.DestinationPlayers,
                    playerKey);
                if (existingDestination == null)
                {
                    roster.DestinationPlayers.Add(
                        roster.SourcePlayer.DeepClone());
                }
                else
                {
                    existingDestination.Replace(
                        roster.SourcePlayer.DeepClone());
                }

                // Copy first so a partial failure can duplicate but never lose a player.
                await UploadPlayersJsonAsync(
                    roster.DestinationConfig,
                    roster.DestinationPlayers);
            }
            if (removeFromSource)
            {
                foreach (var roster in rostersToMove)
                {
                    roster.SourcePlayer.Remove();
                    await UploadPlayersJsonAsync(
                        roster.SourceConfig,
                        roster.SourcePlayers);
                }
            }
            return new SavePlayersResponse();
        }

        internal static string GetPlayersPath(PlayersBlobConfig config)
        {
            var groupId = NormalizeGroupId(config.GroupId);
            var prefix = groupId == DefaultGroupId
                ? $"{config.UId}_players"
                : $"{config.UId}_player_groups/{groupId}/players";
            return config.AlgoType == (int)AlgoType.SkillWise
                ? prefix
                : $"{prefix}_{config.AlgoType}";
        }

        private async Task<bool> GroupExistsAsync(
            string userId,
            string groupId)
        {
            var normalizedGroupId = NormalizeGroupId(groupId);
            if (normalizedGroupId == DefaultGroupId)
            {
                return true;
            }
            if (!IsCustomGroupId(normalizedGroupId))
            {
                return false;
            }

            var groups = await DownloadCustomGroupsAsync(userId);
            return groups.Any(group => string.Equals(
                group.Id,
                normalizedGroupId,
                StringComparison.OrdinalIgnoreCase));
        }

        private async Task<List<PlayerGroup>> DownloadCustomGroupsAsync(
            string userId)
        {
            return (await DownloadGroupStateAsync(userId))
                .Groups
                .Where(group => IsCustomGroupId(group.Id))
                .ToList();
        }

        private async Task<GroupState> DownloadGroupStateAsync(string userId)
        {
            var client = GetContainer().GetBlobClient(GetGroupsPath(userId));
            if (!await client.ExistsAsync())
            {
                return new GroupState();
            }

            var content = await client.DownloadContentAsync();
            return new GroupState
            {
                Groups = JsonConvert.DeserializeObject<List<PlayerGroup>>(
                    content.Value.Content.ToString())
                    ?? new List<PlayerGroup>(),
                ETag = content.Value.Details.ETag,
                Exists = true
            };
        }

        private async Task<bool> TryUploadGroupsAsync(
            string userId,
            GroupState state)
        {
            var conditions = state.Exists
                ? new BlobRequestConditions { IfMatch = state.ETag }
                : new BlobRequestConditions { IfNoneMatch = ETag.All };
            try
            {
                await GetContainer()
                    .GetBlobClient(GetGroupsPath(userId))
                    .UploadAsync(
                        BinaryData.FromString(
                            JsonConvert.SerializeObject(state.Groups)),
                        new BlobUploadOptions
                        {
                            Conditions = conditions,
                            HttpHeaders = new BlobHttpHeaders
                            {
                                ContentType = "application/json"
                            }
                        });
                return true;
            }
            catch (RequestFailedException exception)
                when (exception.Status == 409 || exception.Status == 412)
            {
                return false;
            }
        }

        private async Task<JArray> DownloadPlayersJsonAsync(
            PlayersBlobConfig config)
        {
            var client = GetContainer().GetBlobClient(GetPlayersPath(config));
            if (!await client.ExistsAsync())
            {
                config.ExpectedRevision = RosterDocument.MissingRevision;
                return new JArray();
            }

            var content = await client.DownloadContentAsync();
            config.ExpectedRevision = content.Value.Details.ETag.ToString();
            return await _assessments.ApplyAsync(
                config.UId, NormalizeGroupId(config.GroupId), config.AlgoType,
                RosterDocument.Players(JToken.Parse(content.Value.Content.ToString())));
        }

        private async Task UploadPlayersJsonAsync(
            PlayersBlobConfig config,
            JArray players)
        {
            var response = await UploadAsync(players, config) as SavePlayersResponse;
            if (response?.Success != true)
                throw new InvalidOperationException(response?.Error ?? "The player roster could not be saved.");
            config.ExpectedRevision = response.Revision;
            await _assessments.SaveAsync(
                config.UId, NormalizeGroupId(config.GroupId), config.AlgoType,
                players, dedicatedOnly: true);
        }

        private BlobContainerClient GetContainer()
        {
            return _container;
        }

        private static PlayersBlobConfig CreateConfig(
            string userId,
            int algoType,
            string groupId)
        {
            return new PlayersBlobConfig
            {
                UId = userId,
                AlgoType = algoType,
                GroupId = groupId
            };
        }

        private static JObject FindPlayer(
            JArray players,
            string playerKey)
        {
            return players
                .OfType<JObject>()
                .FirstOrDefault(player => string.Equals(
                    player["key"]?.Value<string>(),
                    playerKey,
                    StringComparison.OrdinalIgnoreCase));
        }

        private static string GetGroupsPath(string userId)
        {
            return $"{userId}_player_groups.json";
        }

        private static PlayerGroup CreateDefaultGroup(
            string name = null,
            bool isPersisted = false)
        {
            return new PlayerGroup
            {
                Id = DefaultGroupId,
                Name = NormalizeGroupName(name) ?? DefaultGroupName,
                IsDefault = true,
                IsPersisted = isPersisted,
                CreatedAt = DateTimeOffset.UnixEpoch
            };
        }

        private static bool GroupNameExists(
            IEnumerable<PlayerGroup> groups,
            string name)
        {
            var groupList = groups.ToList();
            var hasStoredDefault = groupList.Any(group =>
                NormalizeGroupId(group.Id) == DefaultGroupId);
            return (!hasStoredDefault && string.Equals(
                        DefaultGroupName,
                        name,
                        StringComparison.OrdinalIgnoreCase))
                || groupList.Any(group => string.Equals(
                    group.Name,
                    name,
                    StringComparison.OrdinalIgnoreCase));
        }

        private static string NormalizeGroupName(string name)
        {
            var normalized = name?.Trim();
            return string.IsNullOrWhiteSpace(normalized)
                || normalized.Length > 60
                ? null
                : normalized;
        }

        internal static string NormalizeGroupId(string groupId)
        {
            return string.IsNullOrWhiteSpace(groupId)
                || string.Equals(
                    groupId,
                    DefaultGroupId,
                    StringComparison.OrdinalIgnoreCase)
                ? DefaultGroupId
                : groupId.Trim().ToLowerInvariant();
        }

        private static bool IsValidGroupId(string groupId)
        {
            return NormalizeGroupId(groupId) == DefaultGroupId
                || IsCustomGroupId(groupId);
        }

        private static bool IsCustomGroupId(string groupId)
        {
            return Guid.TryParseExact(
                groupId?.Trim(),
                "N",
                out _);
        }

        private sealed class GroupState
        {
            internal List<PlayerGroup> Groups { get; set; } =
                new List<PlayerGroup>();

            internal ETag ETag { get; set; }

            internal bool Exists { get; set; }
        }

        private sealed class RosterMove
        {
            internal PlayersBlobConfig SourceConfig { get; set; }

            internal JArray SourcePlayers { get; set; }

            internal JObject SourcePlayer { get; set; }

            internal PlayersBlobConfig DestinationConfig { get; set; }

            internal JArray DestinationPlayers { get; set; }
        }
    }
}
