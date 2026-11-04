using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text.Json;
using TeamsGenerator.Ai;
using TeamsGenerator.Algos.BackAndForthAlgo;
using TeamsGenerator.Algos.SkillWiseAlgo;
using TeamsGenerator.API;
using TeamsGenerator.Utilities;
using TeamsGeneratorWebAPI.Clients;
using TeamsGeneratorWebAPI.Authentication;
using TeamsGeneratorWebAPI.DesignCreator;
using TeamsGeneratorWebAPI.PlayersBlob;
using TeamsGeneratorWebAPI.Telemetry;
using TeamsGeneratorWebAPI.Premium;
using TeamsGeneratorWebAPI.Collaboration;
using TeamsGeneratorWebAPI.Capacity;

namespace TeamsGeneratorWebAPI.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class TeamsController : ControllerBase
    {
        private readonly ITeamsStorageBlobConnector _azureStorage;

        private readonly ILogger<TeamsController> _logger;
        private readonly IUsageTelemetry _usageTelemetry;
        private readonly AzureTableStorageService _matchService;
        private readonly Lazy<OpenAiService> _aiService;
        private readonly IAccountEntitlementService _entitlements;
        private readonly IGroupCollaborationService _collaboration;

        public TeamsController(ILogger<TeamsController> logger, IUsageTelemetry usageTelemetry, ITeamsStorageBlobConnector teamsStorageBlobConnector, AzureTableStorageService matchService, Lazy<OpenAiService> aiService, IAccountEntitlementService entitlements, IGroupCollaborationService collaboration)
        {
            _logger = logger;
            _usageTelemetry = usageTelemetry;
            _azureStorage = teamsStorageBlobConnector;
            _matchService = matchService;
            _aiService = aiService;
            _entitlements = entitlements;
            _collaboration = collaboration;
        }

        [HttpPost()]
        public async Task<ActionResult<GetTeamsResponse>> Post(
            [FromHeader(Name = "client_version")] string ver,
            [FromBody] dynamic dicJson,
            int algoKey,
            string? ownerId = null)
        {
            var effectiveOwnerId =
                RequestUserId.ResolveOptional(User, ownerId);
            var stopwatch = Stopwatch.StartNew();
            var outcome = "failed";
            var tier = "unknown";
            var chemistryRequested = false;
            var chemistryEligible = false;
            var historyCount = 0;
            var playerCount = 0;
            GetTeamsResponse? response = null;
            try
            {
                var entitlements = HttpContext.Items[RuntimeCapacityMiddleware.GenerationEntitlementsKey]
                    as AccountEntitlements ?? await _entitlements.GetAsync(
                    effectiveOwnerId ?? string.Empty,
                    RequestUserId.ResolveVerifiedEmail(User), HttpContext.RequestAborted);
                tier = entitlements.IsPremium ? "premium" : "free";
                chemistryEligible = entitlements.CanUseChemistry;
                if (algoKey == 3 && !entitlements.CanUseAiAlgorithm)
                {
                    outcome = "denied";
                    return StatusCode(
                        StatusCodes.Status403Forbidden,
                        new { error = "The AI algorithm requires a Premium account." });
                }
                JObject request =
                    dicJson as JObject ?? JObject.FromObject((object)dicJson);
                using (var document = JsonDocument.Parse(request.ToString(Newtonsoft.Json.Formatting.None)))
                    RuntimePayloadGuard.ValidateGeneration(document.RootElement, entitlements.Capacity,
                        entitlements.MaximumGenerationPlayers);
                chemistryRequested =
                    request["config"]?["useChemistry"]?.Value<bool>() == true;
                playerCount = (request["players"] as JArray)?.Count ?? 0;
                if (chemistryRequested && !entitlements.CanUseChemistry)
                {
                    outcome = "denied";
                    return StatusCode(
                        StatusCodes.Status403Forbidden,
                        new { error = "Team chemistry requires a Premium account." });
                }
                ChemistryHistorySnapshot chemistryHistory =
                    string.IsNullOrWhiteSpace(effectiveOwnerId) || !chemistryRequested
                        ? new ChemistryHistorySnapshot()
                        : await _matchService.GetChemistryHistory(effectiveOwnerId);
                historyCount = chemistryHistory.MatchdayCount;
                TeamsGenerator.Orchestration.GenerationCancellation.ThrowIfCancellationRequested();
                response = WebAppAPI.GetTeams(
                    dicJson, algoKey, chemistryHistory.Scores, historyCount);
                outcome = response.Teams?.Count > 0 ? "succeeded" : "empty";
                return Ok(response);
            }
            finally
            {
                _usageTelemetry.Track(
                    "TeamsGenerated", ver, effectiveOwnerId,
                    new Dictionary<string, string?>
                    {
                        ["outcome"] = outcome,
                        ["account_tier"] = tier,
                        ["algorithm"] = algoKey.ToString(),
                        ["algorithm_name"] = UsageMetrics.AlgorithmName(algoKey),
                        ["chemistry_requested"] = UsageMetrics.Flag(chemistryRequested),
                        ["chemistry_supported"] = UsageMetrics.Flag(algoKey is 0 or 2),
                        ["chemistry_eligible"] = UsageMetrics.Flag(chemistryEligible),
                        ["chemistry_history_used"] = UsageMetrics.Flag(historyCount > 0),
                        ["chemistry_applied"] = UsageMetrics.Flag(response?.Chemistry?.Applied == true)
                    },
                    new Dictionary<string, double>
                    {
                        ["player_count"] = playerCount,
                        ["team_count"] = response?.Teams?.Count ?? 0,
                        ["duration_ms"] = stopwatch.Elapsed.TotalMilliseconds,
                        ["chemistry_matchday_count"] = historyCount,
                        ["chemistry_swap_count"] = response?.Chemistry?.SwapCount ?? 0,
                        ["chemistry_partnership_count"] = response?.Chemistry?.PartnershipCount ?? 0
                    });
            }
        }

        [HttpPost("Chemistry/Reset")]
        public async Task<IActionResult> ResetChemistry()
        {
            var userId = RequestUserId.Resolve(User, string.Empty);
            if (!RequestUserId.IsAuthenticatedOrganizer(User, userId))
            {
                return Unauthorized(new
                {
                    error = "Sign in to reset your team chemistry."
                });
            }
            var outcome = "failed";
            try
            {
                await _matchService.ResetChemistryHistory(userId);
                outcome = "succeeded";
                return Ok(new { success = true });
            }
            finally
            {
                _usageTelemetry.Track("ChemistryReset", userId: userId,
                    properties: new Dictionary<string, string?> { ["outcome"] = outcome });
            }
        }

        [HttpPost("[action]")]

        public GetTeamsResponse PostResultString([FromHeader(Name = "client_version")] string ver, [FromBody] dynamic dicJson)
        {
            return WebAppAPI.GetResultString(dicJson);
        }

    
        [HttpPost("[action]")]
        public async Task<IActionResult> GetTeamsDesign([FromHeader(Name = "client_version")] string ver, [FromBody] dynamic team)
        {
            var teamsSerializedObject = JsonConvert.SerializeObject(team.playerNames, Newtonsoft.Json.Formatting.Indented);
            IEnumerable<string> players = JsonConvert.DeserializeObject<List<string>>(teamsSerializedObject);

            var ms = ImageCreator.CreateTeams(players.ToList(), team.color.ToString());

            // Convert the image to a byte array and add it to the result list
            byte[] imageBytes = ms.ToArray();

            _usageTelemetry.Track(
                "ShareImageGenerated",
                ver,
                properties: new Dictionary<string, string?>
                {
                    ["graphic_type"] = "single_team",
                    ["outcome"] = "succeeded",
                    ["renderer"] = "server"
                },
                measurements: new Dictionary<string, double>
                {
                    ["player_count"] = players.Count(),
                    ["image_bytes"] = imageBytes.Length
                });
            return File(imageBytes, "image/png");
        }

        [HttpPost("[action]")]
        public IActionResult GetAllTeamsDesign([FromHeader(Name = "client_version")] string ver, [FromBody] dynamic config)
        {
            JObject request = config as JObject ?? JObject.FromObject(config);
            var teamInfo = request["teamInfo"] as JObject ?? new JObject();
            var teams = request["teams"]?
                .Children<JObject>()
                .Select((team, index) => new TeamShareItem
                {
                    Name = GetString(team, "name", "teamName"),
                    Color = GetString(team, "color"),
                    Players = team["players"]?
                        .Select(player => player.Type == JTokenType.String
                            ? player.ToString()
                            : GetString(player as JObject ?? new JObject(), "name"))
                        .Where(name => !string.IsNullOrWhiteSpace(name))
                        .Select(name => name.Trim())
                        .ToList() ?? new List<string>()
                })
                .ToList() ?? new List<TeamShareItem>();

            var culture = GetString(teamInfo, "currentCulture", "culture");
            var image = ImageCreator.CreateTeamsOverview(
                teams,
                GetString(teamInfo, "teamName", "matchName"),
                GetString(teamInfo, "location", "venue"),
                GetString(teamInfo, "date", "eventDate"),
                GetString(teamInfo, "dayInWeek"),
                string.IsNullOrWhiteSpace(culture) ? "en-US" : culture);

            _usageTelemetry.Track(
                "ShareImageGenerated",
                ver,
                properties: new Dictionary<string, string?>
                {
                    ["graphic_type"] = "all_teams",
                    ["outcome"] = "succeeded",
                    ["renderer"] = "server",
                    ["language"] = GetLanguage(culture)
                },
                measurements: new Dictionary<string, double>
                {
                    ["team_count"] = teams.Count,
                    ["player_count"] =
                        teams.Sum(team => team.Players.Count),
                    ["image_bytes"] = image.Length
                });
            return File(image, "image/png");
        }

        [HttpPost("[action]")]
        public async Task<IActionResult> GetScoresDesign([FromHeader(Name = "client_version")] string ver, [FromBody] dynamic statsJson)
        {
            var statsSerializedObject = JsonConvert.SerializeObject(statsJson.stats, Newtonsoft.Json.Formatting.Indented);
            var topScorersSerializedObject = JsonConvert.SerializeObject(statsJson.topScorers, Newtonsoft.Json.Formatting.Indented);
            //IEnumerable<string> stats = JsonConvert.DeserializeObject<List<string>>(statsSerializedObject);

            var ms = ImageCreator.CreateTable(statsJson.stats, statsJson.topScorers, ver);

            // Convert the image to a byte array and add it to the result list
            byte[] imageBytes = ms.ToArray();

            _usageTelemetry.Track(
                "ShareImageGenerated",
                ver,
                properties: new Dictionary<string, string?>
                {
                    ["graphic_type"] = "scoreboard",
                    ["outcome"] = "succeeded",
                    ["renderer"] = "server"
                },
                measurements: new Dictionary<string, double>
                {
                    ["image_bytes"] = imageBytes.Length
                });
            return File(imageBytes, "image/png");
        }

        [HttpPost("[action]")]
        public async Task<IActionResult> GetNormalizedScoresDesign([FromHeader(Name = "client_version")] string ver, [FromBody] dynamic statsJson)
        {
            var statsSerializedObject = JsonConvert.SerializeObject(statsJson.stats, Newtonsoft.Json.Formatting.Indented);
            var topScorersSerializedObject = JsonConvert.SerializeObject(statsJson.topScorers, Newtonsoft.Json.Formatting.Indented);
            //IEnumerable<string> stats = JsonConvert.DeserializeObject<List<string>>(statsSerializedObject);

            var ms = ImageCreator.CreateNormalizedTable(statsJson, ver);

            // Convert the image to a byte array and add it to the result list
            byte[] imageBytes = ms.ToArray();

            _usageTelemetry.Track(
                "ShareImageGenerated",
                ver,
                properties: new Dictionary<string, string?>
                {
                    ["graphic_type"] = "scoreboard_normalized",
                    ["outcome"] = "succeeded",
                    ["renderer"] = "server"
                },
                measurements: new Dictionary<string, double>
                {
                    ["image_bytes"] = imageBytes.Length
                });
            return File(imageBytes, "image/png");
        }

        [HttpPost("[action]")]
        public async Task<IActionResult> SaveToStorage([FromHeader(Name = "client_version")] string ver, [FromBody] dynamic teams, string uid, string? groupId = null)
        {
            JObject body = teams as JObject ?? JObject.FromObject((object)teams);
            if (body["writeContractVersion"]?.Type != JTokenType.Integer
                || body["writeContractVersion"]!.ToString() != SafeWriteContract.Version.ToString(System.Globalization.CultureInfo.InvariantCulture)
                || body["expectedRevision"]?.Type != JTokenType.String
                || (body["expectedRevision"]!.Value<string>() != SafeWriteContract.AbsentRevision
                    && !AzureTableStorageService.HasExpectedRevision(body["expectedRevision"]!.Value<string>())))
                return SafeWriteUpgradeRequired();
            if (body["teams"] is not JArray)
                return BadRequest(new { success = false, errorCode = "invalid_teams_snapshot", message = "A teams array is required." });
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
            var teamSnapshot = new JObject { ["teams"] = body["teams"]!.DeepClone() };
            var response = await _azureStorage.UploadVersionedAsync(teamSnapshot, new TeamsBlobConfig()
            {
                UId = access.OwnerId,
                GroupId = access.GroupId
            }, body["expectedRevision"]!.Value<string>()!);
            _usageTelemetry.Track(
                "TeamsSaved",
                ver,
                userId,
                new Dictionary<string, string?>
                {
                    ["outcome"] = response.Success ? "succeeded" : "failed"
                });
            if (response.RequiresUpgrade)
                return StatusCode(StatusCodes.Status428PreconditionRequired, response);
            if (response.IsConflict)
                return Conflict(response);
            return Ok(response);
        }

        private static JObject CreateSharedTeams(JObject storedTeams)
        {
            var result = (JObject)PrivatePlayerFields.RemoveFromSnapshot(storedTeams);
            foreach (var players in result.Descendants().OfType<JProperty>()
                .Where(property => string.Equals(property.Name, "players", StringComparison.OrdinalIgnoreCase))
                .Select(property => property.Value).OfType<JArray>())
            {
                foreach (var player in players.Children<JObject>().ToList())
                {
                    foreach (var property in player.Properties()
                        .Where(property =>
                            !SharedTeamPlayerProperties.Contains(property.Name))
                        .ToList())
                    {
                        property.Remove();
                    }
                }
            }
            return result;
        }

        private static readonly HashSet<string> SharedTeamPlayerProperties =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "key",
                "id",
                "name",
                "photo",
                "isArrived",
                "waitingListOrder",
                "isLocked",
                "isGoalKeeper"
            };

        private static bool IsSharedGroupReference(string groupId) =>
            groupId?.StartsWith(
                "shared.",
                StringComparison.Ordinal) == true;

        [HttpPost("[action]")]
        public async Task<IActionResult> GetTeamsFromStorage([FromHeader(Name = "client_version")] string ver, string uid, string? groupId = null)
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
            var response = await _azureStorage.ListAsync(new TeamsBlobConfig()
            {
                UId = access.OwnerId,
                GroupId = access.GroupId
            });
            if (response is GetTeamsFromStorageResponse storedResponse && storedResponse.Success)
                storedResponse.Teams = (JObject)PrivatePlayerFields.RemoveFromSnapshot(storedResponse.Teams);
            _usageTelemetry.Track(
                "TeamsLoaded",
                ver,
                uid,
                new Dictionary<string, string?>
                {
                    ["outcome"] = response.Success ? "succeeded" : "failed"
                });
            if (
                !access.IsOwner
                && response is GetTeamsFromStorageResponse sharedTeams
                && sharedTeams.Success)
            {
                return Ok(new
                {
                    success = true,
                    teams = CreateSharedTeams(sharedTeams.Teams),
                    revision = sharedTeams.Revision,
                    exists = sharedTeams.Exists,
                    writeContractVersion = sharedTeams.WriteContractVersion
                });
            }
            return Ok(response);
        }

        [HttpPost("[action]")]
        public Dictionary<string, Score> GetScores([FromHeader(Name = "client_version")] string ver, [FromBody] dynamic stats)
        {
            return TableCalculator.Create(stats.stats);
        }



        [HttpPost("[action]")]
        public async Task<IActionResult> AddMatch(
            [FromHeader(Name = "client_version")] string ver,
            [FromBody] MatchEntity match)
        {
            var result = await _matchService.AddMatchAsync(match);
            TrackMatchMutation("MatchRecorded", result, ver, match.PartitionKey, match.RowKey);
            if (result == MatchMutationResult.Closed)
            {
                return ClosedMatchdayResponse();
            }
            if (result == MatchMutationResult.Conflict)
            {
                return await ConflictMatchdayResponse(match.PartitionKey);
            }

            var matches = await _matchService.GetAllMatchesAsync(match.PartitionKey);
            return Ok(matches.Select(MatchReadResponse.FromEntity).ToArray());
        }

        [HttpPost("[action]")]
        public async Task<IActionResult> ReadMatches(string partitionKey)
        {
            var matches = await _matchService.GetAllMatchesAsync(partitionKey);
            return Ok(matches.Select(MatchReadResponse.FromEntity).ToArray());
        }

        [HttpPost("[action]")]
        public async Task<IActionResult> AddPlayerSwap(
            [FromHeader(Name = "client_version")] string ver,
            [FromBody] PlayerSwapEntity playerSwap)
        {
            var result = await _matchService.AddPlayerSwapAsync(playerSwap);
            TrackMatchMutation("PlayerSwapRecorded", result, ver, playerSwap.PartitionKey, playerSwap.RowKey);
            if (result == MatchMutationResult.Closed)
            {
                return ClosedMatchdayResponse();
            }
            if (result == MatchMutationResult.Conflict)
            {
                var authoritativeSwaps =
                    await _matchService.GetAllPlayerSwapsAsync(
                        playerSwap.PartitionKey);
                return Conflict(new
                {
                    IsConflict = true,
                    Message = "Another lineup update was saved first. The latest player swaps were loaded.",
                    PlayerSwaps = authoritativeSwaps
                });
            }

            return Ok(await _matchService.GetAllPlayerSwapsAsync(
                playerSwap.PartitionKey));
        }

        [HttpPost("[action]")]
        public async Task<IActionResult> ReadPlayerSwaps(string partitionKey)
        {
            return Ok(await _matchService.GetAllPlayerSwapsAsync(
                partitionKey));
        }

        [HttpPost("[action]")]
        public async Task<IActionResult> DoneAndReadMatches(
            [FromHeader(Name = "client_version")] string ver,
            string partitionKey,
            string? ownerId = null)
        {
            try
            {
                var effectiveOwnerId =
                    RequestUserId.ResolveOptional(User, ownerId);
                var matches = await _matchService.FinalizeMatchday(
                    partitionKey,
                    effectiveOwnerId);
                _usageTelemetry.Track(
                    "MatchdayCompleted",
                    ver,
                    userId: effectiveOwnerId,
                    properties: new Dictionary<string, string?>
                    {
                        ["outcome"] = "succeeded",
                        ["matchday_key"] = UsageMetrics.ResourceKey("matchday", partitionKey)
                    },
                    measurements: new Dictionary<string, double>
                    {
                        ["match_count"] = matches.Count()
                    });
                return Ok(matches.Select(MatchReadResponse.FromEntity).ToArray());
            }
            catch (MatchdayConcurrencyException)
            {
                _usageTelemetry.Track(
                    "MatchdayCompleted",
                    ver,
                    userId: RequestUserId.ResolveOptional(User, ownerId),
                    properties: new Dictionary<string, string?>
                    {
                        ["outcome"] = "conflict",
                        ["matchday_key"] = UsageMetrics.ResourceKey("matchday", partitionKey)
                    });
                return await ConflictMatchdayResponse(partitionKey);
            }
        }

        [HttpPost("[action]")]
        public async Task<IActionResult> EditMatch(
            [FromHeader(Name = "client_version")] string ver,
            [FromBody] MatchWriteRequest request,
            string? ownerId = null)
        {
            if (request.WriteContractVersion != SafeWriteContract.Version
                || !AzureTableStorageService.HasExpectedRevision(request.ExpectedRevision))
                return SafeWriteUpgradeRequired();
            if (string.IsNullOrWhiteSpace(request.SerializedMatch))
                return BadRequest(new { success = false, errorCode = "invalid_match_snapshot", message = "Match details are required." });
            var match = request.ToEntity();
            var result = await _matchService.EditMatch(match, request.ExpectedRevision);
            TrackMatchMutation("MatchEdited", result, ver, match.PartitionKey, match.RowKey, ownerId);
            if (result == MatchMutationResult.Closed)
            {
                return ClosedMatchdayResponse();
            }
            if (result == MatchMutationResult.Succeeded)
            {
                var matches = await _matchService.GetAllMatchesAsync(match.PartitionKey);
                return Ok(matches.Select(MatchReadResponse.FromEntity).ToArray());
            }
            if (result == MatchMutationResult.NotFound)
            {
                return NotFound();
            }
            return await ConflictMatchdayResponse(match.PartitionKey);
        }

        [HttpPost("[action]")]
        public async Task<IActionResult> DeleteMatch(
            [FromHeader(Name = "client_version")] string ver,
            [FromBody] MatchWriteRequest request,
            string? ownerId = null)
        {
            if (request.WriteContractVersion != SafeWriteContract.Version
                || !AzureTableStorageService.HasExpectedRevision(request.ExpectedRevision))
                return SafeWriteUpgradeRequired();
            var match = request.ToEntity();
            var result = await _matchService.DeleteMatch(match, request.ExpectedRevision);
            TrackMatchMutation("MatchDeleted", result, ver, match.PartitionKey, match.RowKey, ownerId);
            if (result == MatchMutationResult.Closed)
            {
                return ClosedMatchdayResponse();
            }
            if (result == MatchMutationResult.Succeeded)
            {
                var matches = await _matchService.GetAllMatchesAsync(match.PartitionKey);
                return Ok(matches.Select(MatchReadResponse.FromEntity).ToArray());
            }
            if (result == MatchMutationResult.NotFound)
            {
                return NotFound();
            }
            return await ConflictMatchdayResponse(match.PartitionKey);
        }

        [HttpPost("[action]")]
        public async Task<IActionResult> GetAiSummary(
            [FromHeader(Name = "client_version")] string ver,
            [FromBody] dynamic matchesHistory,
            CancellationToken cancellationToken = default)
        {
            const string language = "he";
            var userId = RequestUserId.ResolveOptional(User, null);
            if (!(await _entitlements.GetAsync(
                userId ?? string.Empty,
                RequestUserId.ResolveVerifiedEmail(User))).CanUseAiSummary)
            {
                _usageTelemetry.Track("AiReportGenerated", ver, userId,
                    new Dictionary<string, string?>
                    {
                        ["outcome"] = "denied",
                        ["language"] = language
                    });
                return StatusCode(
                    StatusCodes.Status403Forbidden,
                    new { error = "AI summaries require a Premium account." });
            }
            var stopwatch = Stopwatch.StartNew();
            try
            {
                var reply = await _aiService.Value.GetResponseFromAgent(
                    matchesHistory,
                    language,
                    cancellationToken);
                _usageTelemetry.Track(
                    "AiReportGenerated",
                    ver,
                    RequestUserId.ResolveOptional(User, null),
                    new Dictionary<string, string?>
                    {
                        ["outcome"] = "succeeded",
                        ["language"] = language
                    },
                    new Dictionary<string, double>
                    {
                        ["duration_ms"] = stopwatch.Elapsed.TotalMilliseconds
                    });
                return Ok(reply);
            }
            catch (OperationCanceledException)
            {
                _usageTelemetry.Track(
                    "AiReportGenerated",
                    ver,
                    RequestUserId.ResolveOptional(User, null),
                    new Dictionary<string, string?>
                    {
                        ["outcome"] = "canceled",
                        ["language"] = language
                    },
                    new Dictionary<string, double>
                    {
                        ["duration_ms"] = stopwatch.Elapsed.TotalMilliseconds
                    });
                throw;
            }
            catch
            {
                _usageTelemetry.Track(
                    "AiReportGenerated",
                    ver,
                    RequestUserId.ResolveOptional(User, null),
                    new Dictionary<string, string?>
                    {
                        ["outcome"] = "failed",
                        ["language"] = language
                    },
                    new Dictionary<string, double>
                    {
                        ["duration_ms"] = stopwatch.Elapsed.TotalMilliseconds
                    });
                throw;
            }
        }

        [HttpPost("[action]")]
        public async Task<IActionResult> GetMatchday([FromHeader(Name = "client_version")] string ver, string partitionKey)
        {
            var matchday = await _matchService.GetMatchday(partitionKey);
            if(matchday == null)
            {
                return NotFound();
            }

            return Ok(matchday);
        }

        [HttpPost("[action]")]
        public async Task<IActionResult> StartScoreboard([FromHeader(Name = "client_version")] string ver, string partitionKey,
            string? groupId = null, string? expectedTeamsRevision = null)
        {
            if (string.IsNullOrWhiteSpace(partitionKey))
                return BadRequest(new { success = false, message = "A matchday identifier is required." });
            string? ownerId = null;
            string? serializedTeams = null;
            if (groupId != null || expectedTeamsRevision != null)
            {
                if (User.Identity?.IsAuthenticated != true) return Unauthorized();
                if (string.IsNullOrWhiteSpace(groupId) || string.IsNullOrWhiteSpace(expectedTeamsRevision)
                    || expectedTeamsRevision == "*" || expectedTeamsRevision == SafeWriteContract.AbsentRevision)
                    return SafeWriteUpgradeRequired();
                var access = await _collaboration.ResolveAccessAsync(RequestUserId.ResolveOptional(User, null), groupId);
                if (access == null) return Forbid();
                var response = await _azureStorage.ListAsync(new TeamsBlobConfig {
                    UId = access.OwnerId, GroupId = access.GroupId
                });
                if (response is not GetTeamsFromStorageResponse stored || !stored.Success
                    || stored.Teams?["teams"] is not JArray lineup || lineup.Count == 0)
                    return StatusCode(StatusCodes.Status503ServiceUnavailable,
                        new { success = false, message = "The saved matchday teams could not be loaded." });
                if (stored.Revision != expectedTeamsRevision)
                    return Conflict(new { success = false, isConflict = true,
                        message = "The lineup changed. Reload the teams before starting a live matchday." });
                ownerId = access.OwnerId;
                groupId = access.GroupId;
                serializedTeams = PrivatePlayerFields.RemoveFromSnapshot(lineup).ToString(Newtonsoft.Json.Formatting.None);
                if (System.Text.Encoding.Unicode.GetByteCount(serializedTeams) > 64000)
                    return BadRequest(new { success = false, message = "The matchday lineup is too large to share." });
            }
            var started = await _matchService.StartMatchday(partitionKey, ownerId, groupId, serializedTeams, expectedTeamsRevision);
            _usageTelemetry.Track(
                "MatchdayStarted",
                ver,
                RequestUserId.ResolveOptional(User, null),
                new Dictionary<string, string?>
                {
                    ["outcome"] = started ? "succeeded" : "failed",
                    ["matchday_key"] = UsageMetrics.ResourceKey("matchday", partitionKey)
                });
            if (started) return Ok();
            if (await _matchService.IsClosed(partitionKey))
                return Conflict(new { success = false, isClosed = true,
                    message = "This matchday is already closed. Start a new matchday instead." });
            return Conflict(new { success = false, isConflict = true,
                message = "This matchday could not be started with the current lineup. Reload the matchday before trying again." });

        }

        private IActionResult ClosedMatchdayResponse()
        {
            return Ok(new
            {
                IsClosed = true,
                Message = "Matchday is closed. No further matches can be added."
            });
        }

        private IActionResult SafeWriteUpgradeRequired() =>
            StatusCode(StatusCodes.Status428PreconditionRequired, SafeWriteContract.UpgradeRequired());

        private async Task<IActionResult> ConflictMatchdayResponse(
            string partitionKey)
        {
            var matches = await _matchService.GetAllMatchesAsync(partitionKey);
            return Conflict(new
            {
                IsConflict = true,
                Message = "Another user updated the scoreboard. The latest saved matches were loaded.",
                Matches = matches.Select(MatchReadResponse.FromEntity).ToArray()
            });
        }

        private void TrackMatchMutation(
            string eventName,
            MatchMutationResult result,
            string? clientVersion,
            string matchdayId,
            string recordId,
            string? fallbackUserId = null)
        {
            _usageTelemetry.Track(
                eventName,
                clientVersion,
                userId: RequestUserId.ResolveOptional(
                    User,
                    fallbackUserId),
                properties: new Dictionary<string, string?>
                {
                    ["outcome"] = result.ToString().ToLowerInvariant(),
                    ["matchday_key"] = UsageMetrics.ResourceKey("matchday", matchdayId),
                    ["record_key"] = UsageMetrics.ResourceKey("match-record", matchdayId, recordId)
                });
        }

        private static string GetLanguage(string culture)
        {
            if (culture.StartsWith(
                "he",
                StringComparison.OrdinalIgnoreCase))
            {
                return "he";
            }
            if (culture.StartsWith(
                "ar",
                StringComparison.OrdinalIgnoreCase))
            {
                return "ar";
            }
            return "other";
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

    }
}