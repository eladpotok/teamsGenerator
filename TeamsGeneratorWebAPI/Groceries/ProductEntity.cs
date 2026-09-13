using Azure;
using Azure.Data.Tables;

namespace TeamsGeneratorWebAPI.Groceries
{
    public class ProductEntity : ITableEntity
    {
        public string PartitionKey { get; set; }
        public string RowKey { get; set; }
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }

        public string Name { get; set; }
        public string CategoryName { get; set; }
        public bool IsFavorite { get; set; }

        public string MeasurementType { get; set; }
        public string WeightUnit { get; set; }

        public string Description { get; set; }

        public bool HasImage { get; set; }

        public static ProductEntity FromApi(ProductApi product, string uid)
        {
            return new ProductEntity()
            {
                CategoryName = product.Category,
                Name = product.Name,
                PartitionKey = uid,
                RowKey = product.Id,
                Timestamp = DateTime.Parse(product.CreatedAt),
                IsFavorite = product.IsFavorite,
                MeasurementType = product.MeasurementType.ToString(),
                WeightUnit = product.WeightUnit?.ToString(),
                Description = product.Description,
                HasImage = product.ImageBase64 != null
            };
        }
    }
}
