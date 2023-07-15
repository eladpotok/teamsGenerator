using TeamsGenerator.API;

namespace TeamsGeneratorWebAPI.ConfigBlob
{
    public class GetConfigResponse : IResponse
    {
        public UserConfigResponse Config { get; set; }

        public string Error { get; set; }

        public bool Success { get; set; }

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
    }
}
