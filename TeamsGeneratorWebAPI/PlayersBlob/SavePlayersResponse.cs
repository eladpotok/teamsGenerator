using TeamsGenerator.Orchestration.Contracts;

namespace TeamsGeneratorWebAPI.PlayersBlob
{
    public class SavePlayersResponse : IResponse
    {
        public string Error { get; set; }

        public bool Success { get; set; }

        public SavePlayersResponse()
        {
            Success = true;
        }

        public static SavePlayersResponse Failure(string error)
        {
            return new SavePlayersResponse() { Error = error, Success = false };
        }
    }
}
