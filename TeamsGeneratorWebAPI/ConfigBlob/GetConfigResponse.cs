using TeamsGenerator.API;

namespace TeamsGeneratorWebAPI.ConfigBlob
{
    public class GetConfigResponse : IResponse
    {
        public UserConfigResponse Config { get; set; }

        public string Error { get; set; }

        public bool Success { get; set; }

        [Newtonsoft.Json.JsonIgnore]
        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsMissing { get; private set; }

        public GetConfigResponse(UserConfigResponse config)
        {
            Config = config;
            Success = true;
        }

        private GetConfigResponse()
        {

        }

        public static GetConfigResponse Failure(string error)
        {
            return new GetConfigResponse() { Error = error, Success = false };
        }

        public static GetConfigResponse Missing() =>
            new GetConfigResponse { Error = "config isn't found", Success = false, IsMissing = true };

        public static bool IsAvailable(GetConfigResponse? response) =>
            response?.IsMissing == true || response is { Success: true, Config: not null };
    }
}
