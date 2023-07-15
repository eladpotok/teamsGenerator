using TeamsGenerator.Orchestration.Contracts;

namespace TeamsGeneratorWebAPI.PlayersBlob
{
    public class GetPlayersResponse : IResponse
    {
        public IEnumerable<IPlayer> Players { get; set; }

        public string Error { get; set; }

        public bool Success { get; set; }

        public GetPlayersResponse(IEnumerable<IPlayer> players)
        {
            Players = players;  
            Success = true;
        }

        private GetPlayersResponse()
        {

        }

        public static GetPlayersResponse Failure(string error)
        {
            return new GetPlayersResponse() { Error = error, Success = false };
        }
    }
}
