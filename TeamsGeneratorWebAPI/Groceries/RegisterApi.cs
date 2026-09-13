namespace TeamsGeneratorWebAPI.Groceries
{
    public class RegisterApi
    {
        public string Username { get; set; }
        public string Password { get; set; }

        public string GetUsername()
        {
            return Username.ToLower();
        }
    }
}
