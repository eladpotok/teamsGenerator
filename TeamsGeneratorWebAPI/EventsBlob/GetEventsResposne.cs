using TeamsGenerator.Orchestration.Contracts;

namespace TeamsGeneratorWebAPI.PlayersBlob
{
    public class EventData
    {
        public string EventKey { get; set; }
        public IEnumerable<IPlayer> Players { get; set; }
        public string DateTime { get; set; }
        public int PlayersLimit { get; set; }
        public int AlgoKey { get; set; }
        public int TeamsNumber { get; set; }
        public string Name { get; set; }
        public bool IsOwner { get; set; }
    }

    public class GetEventsResposne : IResponse
    {
        public IEnumerable<EventData> Event { get; set; }

        public string Error { get; set; }

        public bool Success { get; set; }

        public GetEventsResposne(IEnumerable<EventData> eventData)
        {
            Event = eventData;  
            Success = true;
        }

        private GetEventsResposne()
        {

        }

        public static GetEventsResposne Failure(string error)
        {
            return new GetEventsResposne() { Error = error, Success = false };
        }
    }
}
