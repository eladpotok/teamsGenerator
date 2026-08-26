using Microsoft.AspNetCore.Mvc;
using TeamsGenerator;
using TeamsGenerator.API;
using TeamsGeneratorWebAPI.ConfigBlob;
using TeamsGeneratorWebAPI.Authentication;
using TeamsGeneratorWebAPI.PlayersBlob;
using TeamsGeneratorWebAPI.Storage;

namespace TeamsGeneratorWebAPI.Controllers
{

    [ApiController]
    [Route("[controller]")]
    public class ConfigController : ControllerBase
    {
        private readonly ILogger<HomeController> _logger;
        private readonly IUserConfigAzureStorage _azureStorage;

        public ConfigController(ILogger<HomeController> logger, IUserConfigAzureStorage azureStorage)
        {
            _logger = logger;
            _azureStorage = azureStorage;
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
            return await _azureStorage.UploadAsync(userConfig, config);
        }


        [HttpGet(Name = "ConfigController")]

        public async Task<IResponse> Get([FromHeader(Name = "client_version")] string ver, string uid)
        {
            var config = new UserConfigBlobConfig() { UId = uid };
            return await _azureStorage.ListAsync(config);
        }
    }
 
}