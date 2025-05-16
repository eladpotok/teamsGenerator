using Azure;
using Azure.Data.Tables;

namespace TeamsGeneratorWebAPI.Clients
{
    public class MatchEntity : ITableEntity
    {
        public string PartitionKey { get; set; } // matchdayId
        public string RowKey { get; set; } // matchId
        public string SerializedMatch { get; set; }
        public ETag ETag { get; set; }
        public DateTime CreatedAt { get; set; }

        public DateTimeOffset? Timestamp { get; set; }
        //public ETag ETag { get; set; } consider use it for editing matches
    }

    public class MatchdayMetadataEntity : ITableEntity
    {
        public string PartitionKey { get; set; } // matchdayId
        public string RowKey { get; set; } // matchId
        public DateTime CreatedAt => DateTime.UtcNow;
        public ETag ETag { get; set; }
        public DateTimeOffset? Timestamp { get; set; }
        public bool IsClosed { get; set; }

    }
}
