using TeamsGenerator.Orchestration.Contracts;

namespace TeamsGeneratorWebAPI.PlayersBlob
{
    public class SaveConfigResponse : IResponse
    {
        public string Error { get; set; }

        public bool Success { get; set; }

        public SaveConfigResponse()
        {
            Success = true;
        }

        public static SaveConfigResponse Failure(string error)
        {
            return new SaveConfigResponse() { Error = error, Success = false };
        }
    }
}
