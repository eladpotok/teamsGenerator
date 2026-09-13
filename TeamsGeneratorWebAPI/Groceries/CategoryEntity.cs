using Azure;
using Azure.Data.Tables;

namespace TeamsGeneratorWebAPI.Groceries
{
    public class CategoryEntity : ITableEntity
    {
        public string PartitionKey { get; set; }
        public string RowKey { get; set; }
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }

        public string Name { get; set; }
        public int SupermarketOrder { get; set; }
        public string Color { get; set; }

        public static CategoryEntity FromApi(CategoryApi categoryApi, string partitionKey)
        {
            return new CategoryEntity()
            {
                Name = categoryApi.Name,
                PartitionKey = partitionKey,
                RowKey = categoryApi.Id,
                SupermarketOrder = categoryApi.SupermarketOrder,
                Timestamp = DateTime.Now,
                Color = categoryApi.Color
            };
        }
    }
}
