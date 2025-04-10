using Newtonsoft.Json.Linq;
using TeamsGenerator.Orchestration.Contracts;

namespace TeamsGeneratorWebAPI.PlayersBlob
{
    public class GetTeamsFromStorageResponse : IResponse
    {
        public JObject Teams { get; set; }

        public string Error { get; set; }

        public bool Success { get; set; }

        public GetTeamsFromStorageResponse(JObject teams)
        {
            Teams = teams;  
            Success = true;
        }

        private GetTeamsFromStorageResponse()
        {

        }

        public static GetTeamsFromStorageResponse Failure(string error)
        {
            return new GetTeamsFromStorageResponse() { Error = error, Success = false };
        }
    }
}
