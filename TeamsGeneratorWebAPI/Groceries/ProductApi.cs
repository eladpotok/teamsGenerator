using System.Text.Json.Serialization;

namespace TeamsGeneratorWebAPI.Groceries
{
    public class ProductApi
    {
        public string Name { get; set; }
        public string Category { get; set; }
        public string Id { get; set; }
        public bool IsFavorite { get; set; }
        public string CreatedAt { get; set; }

        public string MeasurementType { get; set; }

        public string? WeightUnit { get; set; }

        public string? Description { get; set; }

        public string? ImageBase64 { get; set; }
    }

    public enum MeasurementType
    {
        count,
        weight
    }

    public enum WeightUnit
    {
        gram,
        kilogram
    }
}
