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

    public class UpdateEntity : ITableEntity
    {
        public string PartitionKey { get; set; }
        public string RowKey { get; set; }
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }

        public string VersionNumber { get; set; }

        public string ReleaseNotes { get; set; }

        public string VersionName { get; set; }
    }

    public class FeedbackEntity : ITableEntity
    {
        public string PartitionKey { get; set; }
        public string RowKey { get; set; }
        public DateTimeOffset? Timestamp { get; set; }
        public ETag ETag { get; set; }

        public string Feedback { get; set; }
        public int Rating { get; set; }
        public string Email { get; set; }
        public string Name { get; set; }
    }

}
