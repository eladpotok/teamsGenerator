using Microsoft.AspNetCore.Mvc;
using TeamsGenerator;
using TeamsGenerator.API;
using TeamsGeneratorWebAPI.ConfigBlob;
using TeamsGeneratorWebAPI.Authentication;
using TeamsGeneratorWebAPI.PlayersBlob;
using TeamsGeneratorWebAPI.Storage;
using TeamsGeneratorWebAPI.Telemetry;
using TeamsGeneratorWebAPI.Premium;
using TeamsGeneratorWebAPI.Collaboration;

namespace TeamsGeneratorWebAPI.Controllers
{

    [ApiController]
    [Route("[controller]")]
    public class ConfigController : ControllerBase
    {
        private readonly ILogger<HomeController> _logger;
        private readonly IUserConfigAzureStorage _azureStorage;
        private readonly IUsageTelemetry _usageTelemetry;
        private readonly IAccountEntitlementService _entitlements;
        private readonly IGroupCollaborationService _collaboration;
        private readonly IPlayersStorageBlobConnector _players;

        public ConfigController(ILogger<HomeController> logger, IUserConfigAzureStorage azureStorage, IUsageTelemetry usageTelemetry, IAccountEntitlementService entitlements, IGroupCollaborationService collaboration, IPlayersStorageBlobConnector players)
        {
            _logger = logger;
            _azureStorage = azureStorage;
            _usageTelemetry = usageTelemetry;
            _entitlements = entitlements;
            _collaboration = collaboration;
            _players = players;
        }

        [HttpPost("Upload")]
        public async Task<IResponse> Post(
            [FromHeader(Name = "client_version")] string ver,
            [FromBody] UserConfigResponse userConfig,
            string uid,
            string? groupId = null,
            string? scope = null)
        {
            var saveGroupSettings = !string.Equals(
                scope,
                "global",
                StringComparison.OrdinalIgnoreCase);
            var saveGlobalSettings = !string.Equals(
                scope,
                "group",
                StringComparison.OrdinalIgnoreCase);
            var userId = RequestUserId.Resolve(User, uid);
            if (
                IsSharedGroupReference(groupId)
                && User.Identity?.IsAuthenticated != true)
            {
                return SaveConfigResponse.Failure(
                    "Authentication is required for shared groups.");
            }
            var access = await _collaboration.ResolveAccessAsync(
                userId,
                groupId);
            if (access == null)
            {
                return SaveConfigResponse.Failure(
                    "Player group access was denied.");
            }
            var groups = await _players.ListGroupsAsync(access.OwnerId);
            if (!groups.Success || !groups.Groups.Any(group =>
                group.Id == access.GroupId))
            {
                return SaveConfigResponse.Failure(
                    "Player group was not found.");
            }
            var entitlements = _entitlements.Get(userId);
            if (saveGroupSettings
                && !entitlements.CanUseAiAlgorithm
                && userConfig.SelectedAlgoKey == 3)
            {
                return SaveConfigResponse.Failure(
                    "The AI algorithm requires a Premium account.");
            }
            if (saveGlobalSettings
                && !entitlements.CanUseChemistry
                && userConfig.UseChemistry)
            {
                return SaveConfigResponse.Failure(
                    "Team chemistry requires a Premium account.");
            }

            if (saveGroupSettings
                && (userConfig.SkillDefinitions == null
                || userConfig.SkillDefinitions.Count == 0)
            )
            {
                userConfig.SkillDefinitions =
                    SkillDefinition.CreateDefaults();
            }

            var suppliedSkills = userConfig.SkillDefinitions?
                .Where(skill =>
                    skill != null
                    && !string.IsNullOrWhiteSpace(skill.Id)
                    && !string.IsNullOrWhiteSpace(skill.Name))
                .ToList() ?? new List<SkillDefinition>();
            if (saveGroupSettings
                && (suppliedSkills.Count < SkillDefinition.MinimumCount
                || suppliedSkills.Count > SkillDefinition.MaximumCount
                || suppliedSkills.Select(skill => skill.Id.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count() != suppliedSkills.Count
                || suppliedSkills.Select(skill => skill.Name.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count() != suppliedSkills.Count
                || suppliedSkills.Any(skill =>
                    !SkillDefinition.IsValidId(skill.Id)
                    || !SkillDefinition.IsValidName(skill.Name))))
            {
                return SaveConfigResponse.Failure(
                    "Choose between 3 and 8 uniquely named skills.");
            }

            if (saveGroupSettings)
            {
                userConfig.SkillDefinitions =
                    SkillDefinition.Normalize(suppliedSkills);
                userConfig.ArchivedSkillDefinitions =
                    SkillDefinition.NormalizeArchived(
                        userConfig.ArchivedSkillDefinitions,
                        userConfig.SkillDefinitions);
                userConfig.MaxMatchdayPlayers =
                    userConfig.HasCustomMatchdayPlayerLimit
                        ? Math.Clamp(userConfig.MaxMatchdayPlayers, 5, 50)
                        : Math.Clamp(userConfig.NumberOfTeams * 5, 5, 50);
            }
            var ownerDefaultConfig = await LoadConfigAsync(
                access.OwnerId,
                PlayersStorageBlobConnector.DefaultGroupId);
            var existingGroupConfig = access.GroupId
                == PlayersStorageBlobConnector.DefaultGroupId
                ? ownerDefaultConfig
                : await LoadConfigAsync(access.OwnerId, access.GroupId)
                    ?? ownerDefaultConfig;
            var groupConfig = UserConfigScopes.WithGroupSettings(
                existingGroupConfig,
                userConfig);
            var existingGlobalConfig = string.Equals(
                    userId,
                    access.OwnerId,
                    StringComparison.Ordinal)
                ? ownerDefaultConfig
                : await LoadConfigAsync(
                    userId,
                    PlayersStorageBlobConnector.DefaultGroupId);
            var globalConfig = UserConfigScopes.WithGlobalSettings(
                existingGlobalConfig,
                userConfig);

            IResponse response;
            if (!saveGlobalSettings)
            {
                response = await SaveConfigAsync(
                    access.OwnerId,
                    access.GroupId,
                    groupConfig);
            }
            else if (!saveGroupSettings)
            {
                response = await SaveConfigAsync(
                    userId,
                    PlayersStorageBlobConnector.DefaultGroupId,
                    globalConfig);
            }
            else if (
                string.Equals(
                    userId,
                    access.OwnerId,
                    StringComparison.Ordinal)
                && access.GroupId
                    == PlayersStorageBlobConnector.DefaultGroupId)
            {
                var combined = UserConfigScopes.WithGlobalSettings(
                    groupConfig,
                    userConfig);
                response = await SaveConfigAsync(
                    access.OwnerId,
                    access.GroupId,
                    combined);
            }
            else
            {
                var groupResponse = await SaveConfigAsync(
                    access.OwnerId,
                    access.GroupId,
                    groupConfig);
                if (!groupResponse.Success)
                {
                    return groupResponse;
                }
                response = await SaveConfigAsync(
                    userId,
                    PlayersStorageBlobConnector.DefaultGroupId,
                    globalConfig);
            }
            _usageTelemetry.Track(
                "ConfigurationSaved",
                ver,
                userId,
                new Dictionary<string, string?>
                {
                    ["outcome"] = response.Success ? "succeeded" : "failed",
                    ["custom_player_limit"] =
                        userConfig.HasCustomMatchdayPlayerLimit
                            .ToString()
                            .ToLowerInvariant()
                },
                new Dictionary<string, double>
                {
                    ["team_count"] = userConfig.NumberOfTeams,
                    ["matchday_player_limit"] =
                        userConfig.MaxMatchdayPlayers,
                    ["skill_count"] =
                        userConfig.SkillDefinitions?.Count ?? 0
                });
            return response;
        }


        [HttpGet(Name = "ConfigController")]

        public async Task<IActionResult> Get([FromHeader(Name = "client_version")] string ver, string uid, string? groupId = null)
        {
            var userId = RequestUserId.Resolve(User, uid);
            if (
                IsSharedGroupReference(groupId)
                && User.Identity?.IsAuthenticated != true)
            {
                return Unauthorized();
            }
            var access = await _collaboration.ResolveAccessAsync(
                userId,
                groupId);
            if (access == null)
            {
                return Forbid();
            }
            var groups = await _players.ListGroupsAsync(access.OwnerId);
            if (!groups.Success || !groups.Groups.Any(group =>
                group.Id == access.GroupId))
            {
                return NotFound();
            }
            var ownerDefault = await LoadConfigAsync(
                access.OwnerId,
                PlayersStorageBlobConnector.DefaultGroupId);
            var groupConfig = access.GroupId
                == PlayersStorageBlobConnector.DefaultGroupId
                ? ownerDefault
                : await LoadConfigAsync(access.OwnerId, access.GroupId)
                    ?? ownerDefault;
            var globalConfig = string.Equals(
                    userId,
                    access.OwnerId,
                    StringComparison.Ordinal)
                ? ownerDefault
                : await LoadConfigAsync(
                    userId,
                    PlayersStorageBlobConnector.DefaultGroupId);
            return Ok(new GetConfigResponse(
                UserConfigScopes.Merge(groupConfig, globalConfig)));
        }

        private async Task<UserConfigResponse> LoadConfigAsync(
            string userId,
            string groupId)
        {
            var response = await _azureStorage.ListAsync(
                new UserConfigBlobConfig
                {
                    UId = userId,
                    GroupId = groupId
                }) as GetConfigResponse;
            return response?.Config;
        }

        private Task<IResponse> SaveConfigAsync(
            string userId,
            string groupId,
            UserConfigResponse config)
        {
            return _azureStorage.UploadAsync(
                config,
                new UserConfigBlobConfig
                {
                    UId = userId,
                    GroupId = groupId
                });
        }

        private static bool IsSharedGroupReference(string groupId) =>
            groupId?.StartsWith(
                "shared.",
                StringComparison.Ordinal) == true;
    }
 
}