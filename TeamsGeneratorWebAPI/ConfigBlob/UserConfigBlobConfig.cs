using TeamsGeneratorWebAPI.Storage;

namespace TeamsGeneratorWebAPI.ConfigBlob
{
    public class UserConfigBlobConfig : IConfig
    {
        public string UId { get; set; }

        public string GroupId { get; set; }
    }
}
