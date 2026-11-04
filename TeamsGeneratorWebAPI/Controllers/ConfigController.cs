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
            if (!groups.Success)
                return SaveConfigResponse.Failure("Player groups could not be loaded. Please retry.");
            if (!groups.Groups.Any(group =>
                group.Id == access.GroupId))
            {
                return SaveConfigResponse.Failure(
                    "Player group was not found.");
            }
            var entitlements = await _entitlements.GetAsync(
                userId,
                RequestUserId.ResolveVerifiedEmail(User));
            if (saveGroupSettings
                && !entitlements.CanUseAiAlgorithm
                && userConfig.SelectedAlgoKey == 3)
            {
                return SaveConfigResponse.Failure(
                    "The AI algorithm requires a Premium account.");
            }
            if (saveGlobalSettings
                && userConfig.EnableMatchInvitations
                && !RequestUserId.IsAuthenticatedOrganizer(User, userId))
            {
                return SaveConfigResponse.Failure(
                    "Match invitations require a signed-in account.");
            }
            if (saveGlobalSettings
                && !entitlements.CanUseChemistry
                && userConfig.UseChemistry)
            {
                return SaveConfigResponse.Failure(
                    "Team chemistry requires a Premium account.");
            }
            if (saveGlobalSettings)
            {
                userConfig.ShareImageTemplate =
                    UserConfigScopes.NormalizeShareImageTemplate(
                        userConfig.ShareImageTemplate);
                if (!entitlements.CanUsePremiumShareTemplates
                    && UserConfigScopes.IsPremiumShareImageTemplate(
                        userConfig.ShareImageTemplate))
                {
                    return SaveConfigResponse.Failure(
                        "This sharing image style requires a Premium account.");
                }
            }
            if (saveGroupSettings
                && !TryValidateGroupLogo(
                    userConfig.GroupLogoDataUrl,
                    out var logoError))
            {
                return SaveConfigResponse.Failure(logoError);
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
                if (userConfig.MatchdayRules != null
                    && !userConfig.MatchdayRules.TryNormalize(
                        out var rulesError))
                {
                    return SaveConfigResponse.Failure(rulesError);
                }
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
            var configs = await LoadConfigsAsync(userId, access);
            if (!configs.HasValue)
                return SaveConfigResponse.Failure("Configuration could not be loaded. Please retry.");
            var existingGroupConfig = configs.Value.Group;
            if (saveGroupSettings
                && !entitlements.CanUseCustomGroupLogo
                && !string.IsNullOrWhiteSpace(userConfig.GroupLogoDataUrl)
                && !string.Equals(
                    existingGroupConfig?.GroupLogoDataUrl,
                    userConfig.GroupLogoDataUrl,
                    StringComparison.Ordinal))
            {
                return SaveConfigResponse.Failure(
                    "A custom group logo requires a Premium account.");
            }
            var groupConfig = UserConfigScopes.WithGroupSettings(
                existingGroupConfig,
                userConfig);
            var existingGlobalConfig = configs.Value.Global;
            var globalConfig = UserConfigScopes.WithGlobalSettings(
                existingGlobalConfig,
                userConfig);

            async Task<IResponse> SaveAndObserve(
                string storageUserId, string storageGroupId,
                UserConfigResponse config, bool global, bool group)
            {
                var saved = await SaveConfigAsync(storageUserId, storageGroupId, config);
                if (saved.Success)
                {
                    if (global)
                        _usageTelemetry.TrackGlobalPreferences(
                            ver, userId, config, entitlements, "saved", existingGlobalConfig);
                    if (group)
                        _usageTelemetry.TrackGroupPreferences(
                            ver, userId, access.OwnerId, access.GroupId, config,
                            "saved", "saved", existingGroupConfig);
                }
                return saved;
            }

            IResponse response;
            if (!saveGlobalSettings)
            {
                response = await SaveAndObserve(
                    access.OwnerId,
                    access.GroupId,
                    groupConfig, false, true);
            }
            else if (!saveGroupSettings)
            {
                response = await SaveAndObserve(
                    userId,
                    PlayersStorageBlobConnector.DefaultGroupId,
                    globalConfig, true, false);
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
                response = await SaveAndObserve(
                    access.OwnerId,
                    access.GroupId,
                    combined, true, true);
            }
            else
            {
                var groupResponse = await SaveAndObserve(
                    access.OwnerId,
                    access.GroupId,
                    groupConfig, false, true);
                if (!groupResponse.Success)
                {
                    return groupResponse;
                }
                response = await SaveAndObserve(
                    userId,
                    PlayersStorageBlobConnector.DefaultGroupId,
                    globalConfig, true, false);
            }
            _usageTelemetry.Track(
                "ConfigurationSaved",
                ver,
                userId,
                new Dictionary<string, string?>
                {
                    ["outcome"] = response.Success ? "succeeded" : "failed",
                    ["scope"] = saveGlobalSettings
                        ? saveGroupSettings ? "both" : "global" : "group",
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
            if (!groups.Success)
            {
                _logger.LogWarning("Configuration group storage read failed; group absence is unknown.");
                return StatusCode(StatusCodes.Status503ServiceUnavailable,
                    GetConfigResponse.Failure("Player groups could not be loaded. Please retry."));
            }
            if (!groups.Groups.Any(group =>
                group.Id == access.GroupId))
            {
                return NotFound();
            }
            var configs = await LoadConfigsAsync(userId, access);
            if (!configs.HasValue)
                return StatusCode(StatusCodes.Status503ServiceUnavailable,
                    GetConfigResponse.Failure("Configuration could not be loaded. Please retry."));
            return Ok(new GetConfigResponse(
                UserConfigScopes.Merge(configs.Value.Group, configs.Value.Global)));
        }

        private async Task<(UserConfigResponse? Group, UserConfigResponse? Global)?> LoadConfigsAsync(
            string userId, GroupAccess access)
        {
            var ownerDefault = await LoadConfigAsync(
                access.OwnerId, PlayersStorageBlobConnector.DefaultGroupId);
            var group = access.GroupId == PlayersStorageBlobConnector.DefaultGroupId
                ? ownerDefault : await LoadConfigAsync(access.OwnerId, access.GroupId);
            var global = string.Equals(userId, access.OwnerId, StringComparison.Ordinal)
                ? ownerDefault : await LoadConfigAsync(userId, PlayersStorageBlobConnector.DefaultGroupId);
            if (!GetConfigResponse.IsAvailable(ownerDefault)
                || !GetConfigResponse.IsAvailable(group)
                || !GetConfigResponse.IsAvailable(global))
            {
                _logger.LogWarning("Configuration storage read failed; defaults and writes are suppressed.");
                return null;
            }
            return (group?.Config ?? (access.GroupId == PlayersStorageBlobConnector.DefaultGroupId
                ? null : UserConfigScopes.InheritGroupSettings(ownerDefault?.Config)), global?.Config);
        }

        private async Task<GetConfigResponse?> LoadConfigAsync(
            string userId,
            string groupId)
        {
            var response = await _azureStorage.ListAsync(
                new UserConfigBlobConfig
                {
                    UId = userId,
                    GroupId = groupId
                }) as GetConfigResponse;
            return response;
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

        private static bool TryValidateGroupLogo(
            string dataUrl,
            out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(dataUrl))
            {
                return true;
            }

            const string pngPrefix = "data:image/png;base64,";
            if (!dataUrl.StartsWith(
                pngPrefix,
                StringComparison.OrdinalIgnoreCase))
            {
                error = "The group logo must be a PNG image.";
                return false;
            }
            try
            {
                var bytes = Convert.FromBase64String(
                    dataUrl.Substring(pngPrefix.Length));
                if (bytes.Length > 512 * 1024)
                {
                    error = "The group logo must be smaller than 512 KB.";
                    return false;
                }
                if (bytes.Length == 0)
                {
                    error = "The group logo is invalid.";
                    return false;
                }
                byte[] pngSignature =
                {
                    0x89, 0x50, 0x4E, 0x47,
                    0x0D, 0x0A, 0x1A, 0x0A
                };
                if (bytes.Length < pngSignature.Length
                    || !bytes.Take(pngSignature.Length)
                        .SequenceEqual(pngSignature))
                {
                    error = "The group logo must be a valid PNG image.";
                    return false;
                }
                return true;
            }
            catch (FormatException)
            {
                error = "The group logo is invalid.";
                return false;
            }
        }
    }
 
}