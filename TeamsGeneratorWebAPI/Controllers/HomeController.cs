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
            if (!groups.Success || !groups.Groups.Any(group =>
                group.Id == access.GroupId))
            {
                return NotFound();
            }
            var ownerDefaultResponse =
                await ownerDefaultTask as GetConfigResponse;
            var response = await groupConfigTask as GetConfigResponse;
            if (response?.Config == null)
            {
                response = ownerDefaultResponse;
            }
            var globalResponse =
                await globalConfigTask as GetConfigResponse;
            var mergedConfig = UserConfigScopes.Merge(
                response?.Config,
                globalResponse?.Config);
            var appSetup = WebAppAPI.GetAppSetup(
                ver,
                mergedConfig);
            var entitlements = _entitlements.Get(userId);
            appSetup.Entitlements = new PremiumEntitlementsResponse
            {
                IsPremium = entitlements.IsPremium,
                CanUseAiSummary = entitlements.CanUseAiSummary,
                CanUseChemistry = entitlements.CanUseChemistry,
                CanUseAiAlgorithm = entitlements.CanUseAiAlgorithm,
                MaximumPlayers = entitlements.MaximumPlayers
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
                uid,
                new Dictionary<string, string?>
                {
                    ["has_saved_config"] =
                        (response?.Config != null).ToString().ToLowerInvariant()
                });
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