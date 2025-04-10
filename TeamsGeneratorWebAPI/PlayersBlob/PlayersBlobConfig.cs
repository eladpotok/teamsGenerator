using TeamsGeneratorWebAPI.Storage;

namespace TeamsGeneratorWebAPI.PlayersBlob
{
    public class PlayersBlobConfig : IConfig
    {
        public int AlgoType { get; set; }
        
        public string UId { get; set; }
    }

    public class TeamsBlobConfig : IConfig
    {
        public string UId { get; set; }

    }
}
