using Azure;
using Azure.Data.Tables;

namespace TeamsGeneratorWebAPI.Groceries
{
    public class ShoppingListItemEntity : ITableEntity
    {
        public string PartitionKey { get; set; } // the shopping list id
        public string RowKey { get; set; } // product id
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }

        public int Quantity { get; set; }
        public bool IsChecked { get; set; }

        public string Status { get; set; }

        internal static ShoppingListItemEntity FromApi(ShoppingListProductApi item, string partitionKey)
        {
            return new ShoppingListItemEntity() 
            {
                IsChecked = item.IsChecked,
                PartitionKey = partitionKey,
                Quantity = item.Quantity,
                RowKey = item.ProductId,
                Timestamp = DateTime.Now,
                Status = item.Status
            };
        }
    }
}
