using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TeamsGenerator.Algos.BackAndForthAlgo;
using TeamsGenerator.Algos.SkillWiseAlgo;
using TeamsGenerator.API;
using TeamsGenerator.Orchestration.Contracts;
using TeamsGeneratorWebAPI.DesignCreator;
using TeamsGeneratorWebAPI.Authentication;
using TeamsGeneratorWebAPI.PlayersBlob;
using TeamsGeneratorWebAPI.Storage;
using TeamsGeneratorWebAPI.Telemetry;
using TeamsGeneratorWebAPI.Premium;
using TeamsGeneratorWebAPI.Collaboration;
using TeamsGeneratorWebAPI.ConfigBlob;

namespace TeamsGeneratorWebAPI.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class UserPlayersController : ControllerBase
    {
        private readonly ILogger<UserPlayersController> _logger;
        private readonly IPlayersStorageBlobConnector _azureStorage;
        private readonly IUsageTelemetry _usageTelemetry;
        private readonly IAccountEntitlementService _entitlements;
        private readonly IGroupCollaborationService _collaboration;
        private readonly IPlayerAssessmentService _assessments;
        private readonly IUserConfigAzureStorage _configStorage;

        public UserPlayersController(ILogger<UserPlayersController> logger, IPlayersStorageBlobConnector azureStorage, IUsageTelemetry usageTelemetry, IAccountEntitlementService entitlements, IGroupCollaborationService collaboration, IPlayerAssessmentService assessments, IUserConfigAzureStorage configStorage)
        {
            _logger = logger;
            _azureStorage = azureStorage;
            _usageTelemetry = usageTelemetry;
            _entitlements = entitlements;
            _collaboration = collaboration;
            _assessments = assessments;
            _configStorage = configStorage;
        }

        [HttpPost("Upload")]
        public async Task<IActionResult> Post([FromHeader(Name = "client_version")] string ver, [FromBody] dynamic players, string uid, int algoKey, string groupId = null)
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
            var playerItems = GetPlayerItems((object)players);
            var entitlements = _entitlements.Get(userId);
            if (!entitlements.IsPremium
                && playerItems.Count > entitlements.MaximumPlayers)
            {
                return BadRequest(SavePlayersResponse.Failure(
                    $"Free accounts can store up to {entitlements.MaximumPlayers} players."));
            }
            var config = new PlayersBlobConfig()
            {
                UId = access.OwnerId,
                AlgoType = algoKey,
                GroupId = access.GroupId
            };
            var playersToSave = access.IsOwner
                ? players
                : await MergeSharedRosterAsync(players, config);
            if (!access.IsOwner)
            {
                await _assessments.SaveAsync(
                    userId,
                    groupId,
                    algoKey,
                    new JArray(playerItems.Select(item => item.DeepClone())));
            }
            var response = await _azureStorage.UploadAsync(
                playersToSave,
                config);
            _usageTelemetry.Track(
                "PlayerRosterSaved",
                ver,
                userId,
                new Dictionary<string, string?>
                {
                    ["outcome"] = response.Success ? "succeeded" : "failed",
                    ["algorithm"] = algoKey.ToString()
                },
                new Dictionary<string, double>
                {
                    ["player_count"] = playerItems.Count,
                    ["arrived_count"] = playerItems.Count(IsArrived),
                    ["waiting_count"] = playerItems.Count(IsWaiting)
                });
            return Ok(response);
        }

        [HttpPost("SharePlayers")]
        public async Task<IActionResult> PostSharePlayers([FromHeader(Name = "client_version")] string ver, [FromBody] dynamic config, string uid)
        {
            JObject request = config as JObject ?? JObject.FromObject(config);
            var playersList = request["players"]?
                .Children()
                .Select((player, index) =>
                {
                    if (player.Type == JTokenType.String)
                    {
                        return new PlayerShareItem
                        {
                            Name = player.ToString()
                        };
                    }

                    var playerObject = player as JObject ?? new JObject();
                    return new PlayerShareItem
                    {
                        Name = GetString(playerObject, "name"),
                        IsWaiting =
                            playerObject["isWaiting"]?.Value<bool>() == true,
                        WaitingListOrder =
                            playerObject["waitingListOrder"]?.Value<int?>()
                    };
                })
                .Where(player => !string.IsNullOrWhiteSpace(player.Name))
                .Select(player =>
                {
                    player.Name = player.Name.Trim();
                    return player;
                })
                .OrderBy(player => player.IsWaiting)
                .ThenBy(player => player.IsWaiting
                    ? player.WaitingListOrder ?? int.MaxValue
                    : 0)
                .ToList() ?? new List<PlayerShareItem>();
            var teamInfo = request["teamInfo"] as JObject ?? new JObject();

            var teamName = GetString(teamInfo, "teamName", "matchName");
            var location = GetString(teamInfo, "location", "venue");
            var date = GetString(teamInfo, "date", "eventDate");
            var dayInWeek = GetString(teamInfo, "dayInWeek");
            var culture = GetString(teamInfo, "currentCulture", "culture");

            using var ms = ImageCreator.CreatePlayersList(
                playersList,
                teamName,
                location,
                date,
                dayInWeek,
                string.IsNullOrWhiteSpace(culture) ? "en-US" : culture);

            // Convert the image to a byte array and add it to the result list
            byte[] imageBytes = ms.ToArray();

            _usageTelemetry.Track(
                "ShareImageGenerated",
                ver,
                uid,
                new Dictionary<string, string?>
                {
                    ["graphic_type"] = "players",
                    ["language"] = GetLanguage(culture)
                },
                new Dictionary<string, double>
                {
                    ["player_count"] = playersList.Count,
                    ["waiting_count"] =
                        playersList.Count(player => player.IsWaiting),
                    ["image_bytes"] = imageBytes.Length
                });
            return File(imageBytes, "image/png");
        }

        private static string GetString(JObject source, params string[] names)
        {
            foreach (var name in names)
            {
                var property = source.Properties()
                    .FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
                var value = property?.Value?.ToString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return string.Empty;
        }

        private static List<JToken> GetPlayerItems(object players)
        {
            var token = players as JToken ?? JToken.FromObject(players);
            return (token as JArray ?? token["players"] as JArray)
                ?.Children()
                .ToList() ?? new List<JToken>();
        }

        private static bool IsArrived(JToken player)
        {
            return player["isArrived"]?.Value<bool>() == true;
        }

        private static bool IsWaiting(JToken player)
        {
            return !IsArrived(player)
                && player["waitingListOrder"]?.Type != JTokenType.Null
                && player["waitingListOrder"]?.Value<int?>() >= 0;
        }

        private static string GetLanguage(string culture)
        {
            if (culture.StartsWith("he", StringComparison.OrdinalIgnoreCase))
            {
                return "he";
            }
            if (culture.StartsWith("ar", StringComparison.OrdinalIgnoreCase))
            {
                return "ar";
            }
            return "other";
        }


        [HttpGet(Name = "UserPlayersController")]

        public async Task<IActionResult> Get([FromHeader(Name = "client_version")] string ver, string uid, int algoType, string groupId = null)
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
            var config = new PlayersBlobConfig()
            {
                UId = access.OwnerId,
                AlgoType = algoType,
                GroupId = access.GroupId
            };
            var response = await _azureStorage.ListAsync(config);
            var playerCount = response is GetPlayersResponse playersResponse
                ? playersResponse.Players?.Count() ?? 0
                : 0;
            _usageTelemetry.Track(
                "PlayerRosterLoaded",
                ver,
                uid,
                new Dictionary<string, string?>
                {
                    ["outcome"] = response.Success ? "succeeded" : "failed",
                    ["algorithm"] = algoType.ToString()
                },
                new Dictionary<string, double>
                {
                    ["player_count"] = playerCount
                });
            if (
                !access.IsOwner
                && response is GetPlayersResponse sharedResponse
                && sharedResponse.Success)
            {
                var sharedPlayers = CreateSharedRoster(
                    sharedResponse.Players);
                await _assessments.ApplyAsync(
                    userId,
                    groupId,
                    algoType,
                    sharedPlayers);
                return Ok(new
                {
                    success = true,
                    players = sharedPlayers
                });
            }
            return Ok(response);
        }

        [HttpGet("Groups")]
        public async Task<PlayerGroupsResponse> GetGroups(string uid)
        {
            var userId = RequestUserId.Resolve(User, uid);
            var owned = await _azureStorage.ListGroupsAsync(userId);
            if (!owned.Success)
            {
                return owned;
            }
            var defaultGroup = owned.Groups.FirstOrDefault(group =>
                group.IsDefault);
            if (defaultGroup?.IsPersisted == false)
            {
                var defaultConfigResponse = await _configStorage.ListAsync(
                    new UserConfigBlobConfig
                    {
                        UId = userId,
                        GroupId =
                            PlayersStorageBlobConnector.DefaultGroupId
                    }) as GetConfigResponse;
                var matchdayName =
                    defaultConfigResponse?.Config?.TeamName?.Trim();
                if (!string.IsNullOrWhiteSpace(matchdayName))
                {
                    var migration =
                        await _azureStorage.InitializeDefaultGroupNameAsync(
                            userId,
                            matchdayName);
                    if (migration.Success)
                    {
                        defaultGroup.Name = migration.Group.Name;
                    }
                }
            }
            var memberships =
                await _collaboration.ListMembershipsAsync(userId);
            var shared = memberships.Select(membership => new PlayerGroup
            {
                Id = GroupCollaborationService.CreateSharedGroupId(
                    membership.OwnerId,
                    membership.GroupId),
                Name = $"{membership.GroupName} (shared)",
                IsDefault = false,
                IsShared = true,
                CanManage = false,
                CreatedAt = membership.JoinedAt
            });
            return PlayerGroupsResponse.Succeeded(
                owned.Groups.Concat(shared).ToList());
        }

        [HttpPost("Groups")]
        public Task<PlayerGroupMutationResponse> CreateGroup(
            string uid,
            [FromBody] CreatePlayerGroupRequest request)
        {
            return _azureStorage.CreateGroupAsync(
                RequestUserId.Resolve(User, uid),
                request?.Name);
        }

        [HttpPut("Groups/{groupId}")]
        public Task<PlayerGroupMutationResponse> RenameGroup(
            string groupId,
            string uid,
            [FromBody] RenamePlayerGroupRequest request)
        {
            return _azureStorage.RenameGroupAsync(
                RequestUserId.Resolve(User, uid),
                groupId,
                request?.Name);
        }

        [HttpDelete("Groups/{groupId}")]
        public Task<SavePlayersResponse> DeleteGroup(
            string groupId,
            string uid)
        {
            return _azureStorage.DeleteGroupAsync(
                RequestUserId.Resolve(User, uid),
                groupId);
        }

        [HttpPost("Move")]
        public Task<SavePlayersResponse> MovePlayer(
            string uid,
            int algoType,
            [FromBody] MovePlayerRequest request)
        {
            return _azureStorage.MovePlayerAsync(
                RequestUserId.Resolve(User, uid),
                algoType,
                request);
        }

        [HttpPost("Copy")]
        public Task<SavePlayersResponse> CopyPlayer(
            string uid,
            int algoType,
            [FromBody] MovePlayerRequest request)
        {
            return _azureStorage.CopyPlayerAsync(
                RequestUserId.Resolve(User, uid),
                algoType,
                request);
        }

        private async Task<JArray> MergeSharedRosterAsync(
            dynamic suppliedPlayers,
            PlayersBlobConfig config)
        {
            var existingResponse =
                await _azureStorage.ListAsync(config) as GetPlayersResponse;
            var existingPlayers = existingResponse?.Success == true
                ? JArray.FromObject(existingResponse.Players)
                : new JArray();
            var existingByKey = existingPlayers
                .Children<JObject>()
                .Where(player => GetPlayerKey(player) != null)
                .ToDictionary(
                    player => GetPlayerKey(player),
                    player => player,
                    StringComparer.Ordinal);
            var merged = new JArray();
            foreach (var incoming in GetPlayerItems((object)suppliedPlayers)
                .OfType<JObject>())
            {
                var key = GetPlayerKey(incoming);
                var target = key != null
                    && existingByKey.TryGetValue(key, out var existing)
                    ? (JObject)existing.DeepClone()
                    : new JObject();
                foreach (var property in incoming.Properties().Where(
                    property => SharedPlayerProperties.Contains(
                        property.Name)))
                {
                    SetPropertyCaseInsensitive(
                        target,
                        property.Name,
                        property.Value.DeepClone());
                }
                merged.Add(target);
            }
            return merged;
        }

        private static JArray CreateSharedRoster(
            IEnumerable<IPlayer> players)
        {
            var result = new JArray();
            foreach (var player in JArray.FromObject(players)
                .Children<JObject>())
            {
                var shared = new JObject();
                foreach (var property in player.Properties().Where(
                    property => SharedPlayerProperties.Contains(
                        property.Name)))
                {
                    shared[ToCamelCase(property.Name)] =
                        property.Value.DeepClone();
                }
                result.Add(shared);
            }
            return result;
        }

        private static string GetPlayerKey(JObject player)
        {
            return player.Properties().FirstOrDefault(property =>
                string.Equals(
                    property.Name,
                    "key",
                    StringComparison.OrdinalIgnoreCase))
                ?.Value?.ToString();
        }

        private static void SetPropertyCaseInsensitive(
            JObject target,
            string name,
            JToken value)
        {
            var existing = target.Properties().FirstOrDefault(property =>
                string.Equals(
                    property.Name,
                    name,
                    StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                existing.Value = value;
                return;
            }

            target[ToCamelCase(name)] = value;
        }

        private static string ToCamelCase(string name)
        {
            return string.IsNullOrEmpty(name)
                ? name
                : char.ToLowerInvariant(name[0]) + name[1..];
        }

        private static readonly HashSet<string> SharedPlayerProperties =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "key",
                "id",
                "name",
                "photo",
                "isArrived",
                "isWaiting",
                "waitingListOrder",
                "modifyTime",
                "isLocked",
                "isGoalKeeper"
            };

        private static bool IsSharedGroupReference(string groupId) =>
            groupId?.StartsWith(
                "shared.",
                StringComparison.Ordinal) == true;
    }

}