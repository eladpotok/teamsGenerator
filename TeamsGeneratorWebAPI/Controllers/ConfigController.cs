using Microsoft.AspNetCore.Mvc;
using TeamsGenerator;
using TeamsGenerator.API;
using TeamsGeneratorWebAPI.ConfigBlob;
using TeamsGeneratorWebAPI.Authentication;
using TeamsGeneratorWebAPI.PlayersBlob;
using TeamsGeneratorWebAPI.Storage;
using TeamsGeneratorWebAPI.Telemetry;

namespace TeamsGeneratorWebAPI.Controllers
{

    [ApiController]
    [Route("[controller]")]
    public class ConfigController : ControllerBase
    {
        private readonly ILogger<HomeController> _logger;
        private readonly IUserConfigAzureStorage _azureStorage;
        private readonly IUsageTelemetry _usageTelemetry;

        public ConfigController(ILogger<HomeController> logger, IUserConfigAzureStorage azureStorage, IUsageTelemetry usageTelemetry)
        {
            _logger = logger;
            _azureStorage = azureStorage;
            _usageTelemetry = usageTelemetry;
        }

        [HttpPost("Upload")]
        public async Task<IResponse> Post(
            [FromHeader(Name = "client_version")] string ver,
            [FromBody] UserConfigResponse userConfig,
            string uid)
        {
            if (userConfig.SkillDefinitions == null
                || userConfig.SkillDefinitions.Count == 0)
            {
                userConfig.SkillDefinitions =
                    SkillDefinition.CreateDefaults();
            }

            var suppliedSkills = userConfig.SkillDefinitions?
                .Where(skill =>
                    skill != null
                    && !string.IsNullOrWhiteSpace(skill.Id)
                    && !string.IsNullOrWhiteSpace(skill.Name))
                .ToList();
            if (suppliedSkills.Count < SkillDefinition.MinimumCount
                || suppliedSkills.Count > SkillDefinition.MaximumCount
                || suppliedSkills.Select(skill => skill.Id.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count() != suppliedSkills.Count
                || suppliedSkills.Select(skill => skill.Name.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count() != suppliedSkills.Count
                || suppliedSkills.Any(skill =>
                    !SkillDefinition.IsValidId(skill.Id)
                    || !SkillDefinition.IsValidName(skill.Name)))
            {
                return SaveConfigResponse.Failure(
                    "Choose between 3 and 8 uniquely named skills.");
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
            var userId = RequestUserId.Resolve(User, uid);
            var config = new UserConfigBlobConfig() { UId = userId };
            var response = await _azureStorage.UploadAsync(userConfig, config);
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
                        userConfig.SkillDefinitions.Count
                });
            return response;
        }


        [HttpGet(Name = "ConfigController")]

        public async Task<IResponse> Get([FromHeader(Name = "client_version")] string ver, string uid)
        {
            var config = new UserConfigBlobConfig() { UId = uid };
            return await _azureStorage.ListAsync(config);
        }
    }
 
}