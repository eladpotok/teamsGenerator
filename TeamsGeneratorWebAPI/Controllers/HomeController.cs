using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using TeamsGenerator;
using TeamsGenerator.API;
using TeamsGeneratorWebAPI.Clients;
using TeamsGeneratorWebAPI.ConfigBlob;
using TeamsGeneratorWebAPI.PlayersBlob;
using TeamsGeneratorWebAPI.Telemetry;
using TeamsGeneratorWebAPI.Premium;
using TeamsGeneratorWebAPI.Authentication;
using TeamsGeneratorWebAPI.Collaboration;

namespace TeamsGeneratorWebAPI.Controllers
{

    [ApiController]
    [Route("[controller]")]
    public class HomeController : ControllerBase
    {
        private readonly ILogger<HomeController> _logger;
        private readonly IUserConfigAzureStorage _azureStorage;
        private readonly IUsageTelemetry _usageTelemetry;
        private readonly AzureTableStorageService _azureTablesStorage;
        private readonly IAccountEntitlementService _entitlements;
        private readonly IGroupCollaborationService _collaboration;
        private readonly IPlayersStorageBlobConnector _players;
        private readonly IMemoryCache _cache;


        public HomeController(ILogger<HomeController> logger, IUserConfigAzureStorage azureStorage, IUsageTelemetry usageTelemetry, AzureTableStorageService azureTablesStorage, IAccountEntitlementService entitlements, IGroupCollaborationService collaboration, IPlayersStorageBlobConnector players, IMemoryCache cache)
        {
            _logger = logger;
            _azureStorage = azureStorage;
            _usageTelemetry = usageTelemetry;
            _azureTablesStorage = azureTablesStorage;
            _entitlements = entitlements;
            _collaboration = collaboration;
            _players = players;
            _cache = cache;
        }
        
        [HttpGet(Name = "AlgosController")]

        public async Task<ActionResult<GetAppSetupResponse>> Get([FromHeader(Name = "client_version")] string ver, string uid, string? groupId = null)
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
            var groupConfig = new UserConfigBlobConfig
            {
                UId = access.OwnerId,
                GroupId = access.GroupId
            };
            var groupConfigTask = _azureStorage.ListAsync(groupConfig);
            var ownerDefaultTask =
                access.GroupId == PlayersStorageBlobConnector.DefaultGroupId
                    ? groupConfigTask
                    : _azureStorage.ListAsync(new UserConfigBlobConfig
                    {
                        UId = access.OwnerId,
                        GroupId = PlayersStorageBlobConnector.DefaultGroupId
                    });
            var globalConfigTask = string.Equals(
                    userId,
                    access.OwnerId,
                    StringComparison.Ordinal)
                ? ownerDefaultTask
                : _azureStorage.ListAsync(new UserConfigBlobConfig
                {
                    UId = userId,
                    GroupId = PlayersStorageBlobConnector.DefaultGroupId
                });
            var groupsTask = _players.ListGroupsAsync(access.OwnerId);

            await Task.WhenAll(
                groupConfigTask,
                ownerDefaultTask,
                globalConfigTask,
                groupsTask);

            var groups = await groupsTask;
            if (!groups.Success)
            {
                _logger.LogWarning("App setup group storage read failed; group absence is unknown.");
                return StatusCode(StatusCodes.Status503ServiceUnavailable,
                    GetConfigResponse.Failure("Player groups could not be loaded. Please retry."));
            }
            if (!groups.Groups.Any(group =>
                group.Id == access.GroupId))
            {
                return NotFound();
            }
            var ownerDefaultResponse =
                await ownerDefaultTask as GetConfigResponse;
            var response = await groupConfigTask as GetConfigResponse;
            var globalResponse =
                await globalConfigTask as GetConfigResponse;
            if (!GetConfigResponse.IsAvailable(ownerDefaultResponse)
                || !GetConfigResponse.IsAvailable(response)
                || !GetConfigResponse.IsAvailable(globalResponse))
            {
                _logger.LogWarning("App setup configuration read failed; defaults are suppressed.");
                return StatusCode(StatusCodes.Status503ServiceUnavailable,
                    GetConfigResponse.Failure("Configuration could not be loaded. Please retry."));
            }
            var effectiveGroupConfig = response?.Config
                ?? (access.GroupId == PlayersStorageBlobConnector.DefaultGroupId
                    ? ownerDefaultResponse?.Config
                    : UserConfigScopes.InheritGroupSettings(
                        ownerDefaultResponse?.Config));
            var mergedConfig = UserConfigScopes.Merge(
                effectiveGroupConfig,
                globalResponse?.Config);
            var entitlements = await _entitlements.GetAsync(
                userId,
                RequestUserId.ResolveVerifiedEmail(User));
            if (!entitlements.CanUsePremiumShareTemplates
                && UserConfigScopes.IsPremiumShareImageTemplate(
                    mergedConfig.ShareImageTemplate))
            {
                mergedConfig.ShareImageTemplate = "classic";
            }
            if (!entitlements.CanUseAiAlgorithm
                && mergedConfig.SelectedAlgoKey == 3)
            {
                mergedConfig.SelectedAlgoKey = 0;
            }
            if (!entitlements.CanUseChemistry)
            {
                mergedConfig.UseChemistry = false;
            }
            var appSetup = WebAppAPI.GetAppSetup(
                ver,
                mergedConfig);
            appSetup.InvitationAccess = new InvitationAccessResponse
            {
                Enabled = ownerDefaultResponse?.Config?.EnableMatchInvitations == true
            };
            appSetup.GroupJoinAccess = new InvitationAccessResponse
            {
                Enabled = ownerDefaultResponse?.Config?.EnableGroupJoinRequests != false
            };
            appSetup.Entitlements = new PremiumEntitlementsResponse
            {
                IsPremium = entitlements.IsPremium,
                CanUseAiSummary = entitlements.CanUseAiSummary,
                CanUseChemistry = entitlements.CanUseChemistry,
                CanUseAiAlgorithm = entitlements.CanUseAiAlgorithm,
                CanUseCustomGroupLogo =
                    entitlements.CanUseCustomGroupLogo,
                CanUsePremiumShareTemplates =
                    entitlements.CanUsePremiumShareTemplates,
                CanUseMatchInvitations = entitlements.CanUseMatchInvitations,
                MaximumPlayers = entitlements.MaximumPlayers,
                MaximumOwnedGroups = entitlements.MaximumOwnedGroups,
                MaximumGenerationPlayers = entitlements.MaximumGenerationPlayers,
                GenerationRequestsPerMinute = entitlements.GenerationRequestsPerMinute,
                MaximumConcurrentGenerations = entitlements.MaximumConcurrentGenerations
            };

            if (_cache.TryGetValue<string>(
                "latest-release-version",
                out var releaseVersion))
            {
                appSetup.Config.CurrentVersion = releaseVersion;
            }
            else
            {
                QueueReleaseVersionRefresh();
            }
            
            _usageTelemetry.Track(
                "AppSetupLoaded",
                ver,
                userId,
                new Dictionary<string, string?>
                {
                    ["has_saved_config"] =
                        ((response?.Config ?? ownerDefaultResponse?.Config)
                            != null).ToString().ToLowerInvariant(),
                    ["outcome"] = "succeeded",
                    ["account_tier"] = entitlements.IsPremium ? "premium" : "free"
                });
            if (GetConfigResponse.IsAvailable(globalResponse))
                _usageTelemetry.TrackGlobalPreferences(
                    ver, userId, globalResponse?.Config, entitlements, "loaded");
            if (response is { Success: true, Config: not null }
                || response?.IsMissing == true && GetConfigResponse.IsAvailable(ownerDefaultResponse))
                _usageTelemetry.TrackGroupPreferences(
                    ver, userId, access.OwnerId, access.GroupId, effectiveGroupConfig,
                    "loaded", response?.Config != null ? "saved"
                        : ownerDefaultResponse?.Config != null ? "inherited" : "default");
            return Ok(appSetup);
        }

        private void QueueReleaseVersionRefresh()
        {
            if (_cache.TryGetValue(
                "latest-release-version-refreshing",
                out _))
            {
                return;
            }

            _cache.Set(
                "latest-release-version-refreshing",
                true,
                TimeSpan.FromMinutes(2));
            _ = RefreshReleaseVersionAsync();
        }

        private async Task RefreshReleaseVersionAsync()
        {
            try
            {
                var releases = await _azureTablesStorage
                    .GetAllEntities<UpdateEntity>(
                        "2c607f3d-d645-41a5-ad4f-c96ab9737780");
                var releaseVersion =
                    releases.FirstOrDefault()?.VersionNumber;
                if (!string.IsNullOrWhiteSpace(releaseVersion))
                {
                    _cache.Set(
                        "latest-release-version",
                        releaseVersion,
                        TimeSpan.FromMinutes(5));
                }
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "Failed to refresh the latest release version.");
            }
            finally
            {
                _cache.Remove("latest-release-version-refreshing");
            }
        }

        private static bool IsSharedGroupReference(string groupId) =>
            groupId?.StartsWith(
                "shared.",
                StringComparison.Ordinal) == true;


    }
 
}