using Azure;
using Azure.Data.Tables;

namespace TeamsGeneratorWebAPI.Groceries
{
    public class RegisterEntity : ITableEntity
    {
        public string PartitionKey { get; set; }
        public string RowKey { get; set; }
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }

        public string Name { get; set; }
        public string Password { get; set; }

        public static RegisterEntity FromApi(RegisterApi register)
        {
            return new RegisterEntity() 
            {
                PartitionKey = "user",
                RowKey = Guid.NewGuid().ToString(),
                Name = register.GetUsername(),
                Password = Utils.HashPassword(register.Password),
                Timestamp = DateTime.Now
            };
        }
    }

}
