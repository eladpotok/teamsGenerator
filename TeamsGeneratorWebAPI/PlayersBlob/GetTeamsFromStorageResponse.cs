using Newtonsoft.Json.Linq;
using TeamsGenerator.Orchestration.Contracts;

namespace TeamsGeneratorWebAPI.PlayersBlob
{
    public class GetTeamsFromStorageResponse : IResponse
    {
        public JObject Teams { get; set; }

        public string Error { get; set; }

        public bool Success { get; set; }
        public string Revision { get; set; } = Clients.SafeWriteContract.AbsentRevision;
        public bool Exists { get; set; }
        public int WriteContractVersion => Clients.SafeWriteContract.Version;

        public GetTeamsFromStorageResponse(JObject teams)
        {
            Teams = teams;  
            Success = true;
            Exists = true;
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
