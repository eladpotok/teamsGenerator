using Azure;
using Azure.Data.Tables;

namespace TeamsGeneratorWebAPI.Groceries
{
    public class ShoppingListEntity : ITableEntity
    {
        public string PartitionKey { get; set; }
        public string RowKey { get; set; }
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }

        public string Name { get; set; }
        public string CreatedAt { get; set; }
        public string UpdatedAt { get; set; }
        public bool IsDone { get; set; }

        public static ShoppingListEntity FromApi(ShoppingListApi shoppingList, string partitionKey)
        {
            return new ShoppingListEntity()
            {
                PartitionKey = partitionKey,
                RowKey = shoppingList.Id,
                CreatedAt = shoppingList.CreatedAt,
                IsDone = shoppingList.IsDone,
                Name = shoppingList.Name,
                UpdatedAt = shoppingList.UpdatedAt,
                Timestamp = DateTime.Now
            };
        }
    }
}
