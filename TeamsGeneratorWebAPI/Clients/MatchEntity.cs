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


    public class MatchdayEntity : ITableEntity
    {
        public string PartitionKey { get; set; } = "Matchday";
        public string RowKey { get; set; } // matchdayId
        public string Name { get; set; }
        public string Key { get; set; }
        public string OwnerId { get; set; }
        public DateTime Date { get; set; }
        public int MaxPlayers { get; set; }
        public string Status { get; set; } = "waiting";
        public ETag ETag { get; set; }
        public DateTimeOffset? Timestamp { get; set; }
        public int AlgoKey { get; set; }
        public string Location { get; set; }

    }

    public class MatchdayPlayerEntity : ITableEntity
    {
        public string PartitionKey { get; set; } // matchdayId
        public string RowKey { get; set; } // userId
        public bool IsArrived { get; set; }
        public int WaitingListOrderNumber { get; set; } = -1;
        public DateTime JoinedAt { get; set; }
        public ETag ETag { get; set; }
        public DateTimeOffset? Timestamp { get; set; }
        public string Name { get; set; }
        public int Attack { get; set; } = 5;
        public int Stamina { get; set; } = 5;
        public int Leadership { get; set; } = 5;
        public int Defence { get; set; } = 5;
        public int Passing { get; set; } = 5;
        public bool isGoalKeeper { get; set; }
        public int Rank { get; set; } = 4;
        public string Positions { get; set; }
    }

    public class PlayerEntity : ITableEntity
    {
        public string PartitionKey { get; set; } // e.g. "Player"
        public string RowKey { get; set; }       // playerId (same as MatchdayPlayerEntity.RowKey)
        public string JsonData { get; set; }     // full player data as JSON

        public ETag ETag { get; set; }
        public DateTimeOffset? Timestamp { get; set; }
    }

}
