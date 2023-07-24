namespace TeamsGeneratorWebAPI.UsersBlob
{
    public class UserContainer
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Password { get; set; }
        public DateTime UserCreatedTime { get; set; }
    }

    public class LoginUserArgs
    {
        public string Email { get; set; }
        public string Password { get; set; }
    }

    public class SignUserArgs
    {
        public string Name { get; set; }
        public string Email { get; set; }
        public string Password { get; set; }
    }
}
