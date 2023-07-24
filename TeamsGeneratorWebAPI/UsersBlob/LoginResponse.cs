using TeamsGenerator.Orchestration.Contracts;
using TeamsGeneratorWebAPI.UsersBlob;

namespace TeamsGeneratorWebAPI.PlayersBlob
{
    public class LoginResponse : IResponse
    {
        public LoginUserResponse User { get; set; }

        public string Error { get; set; }

        public bool Success { get; set; }

        public LoginResponse(LoginUserResponse user)
        {
            User = user;
            Success = true;
        }

        private LoginResponse()
        {

        }

        public static LoginResponse Failure(string error)
        {
            return new LoginResponse() { Error = error, Success = false };
        }
    }
}
