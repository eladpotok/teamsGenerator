using Azure;
using Azure.Data.Tables;
using TeamsGenerator.API;

namespace TeamsGeneratorWebAPI.Clients
{
    public class MatchEntity : ITableEntity
    {
        public string PartitionKey { get; set; } // matchdayId
        public string RowKey { get; set; } // matchId
        private string _serializedMatch;
        public string SerializedMatch
        {
            get => _serializedMatch;
            set => _serializedMatch = PrivatePlayerFields.SanitizeJson(value);
        }
        public ETag ETag { get; set; }
        [System.Runtime.Serialization.IgnoreDataMember]
        public string Revision => ETag.ToString();
        public DateTime CreatedAt { get; set; }

        public DateTimeOffset? Timestamp { get; set; }
    }

    public record MatchReadResponse(
        string PartitionKey, string RowKey, string SerializedMatch,
        DateTime CreatedAt, DateTimeOffset? Timestamp, ETag ETag,
        [property: Newtonsoft.Json.JsonProperty("revision")] string Revision)
    {
        public static MatchReadResponse FromEntity(MatchEntity match) => new(
            match.PartitionKey, match.RowKey, match.SerializedMatch,
            match.CreatedAt, match.Timestamp, match.ETag, match.Revision);
    }

    public class MatchWriteRequest
    {
        public string PartitionKey { get; set; }
        public string RowKey { get; set; }
        private string? _serializedMatch;
        public string? SerializedMatch
        {
            get => _serializedMatch;
            set => _serializedMatch = value == null ? null : PrivatePlayerFields.SanitizeJson(value);
        }
        public int? WriteContractVersion { get; set; }
        public string? ExpectedRevision { get; set; }

        public MatchEntity ToEntity() => new()
        {
            PartitionKey = PartitionKey,
            RowKey = RowKey,
            SerializedMatch = SerializedMatch ?? string.Empty
        };
    }

    public class PlayerSwapEntity : ITableEntity
    {
        public string PartitionKey { get; set; }
        public string RowKey { get; set; }
        private string _serializedSwap;
        public string SerializedSwap
        {
            get => _serializedSwap;
            set => _serializedSwap = PrivatePlayerFields.SanitizeJson(value);
        }
        public DateTime CreatedAt { get; set; }
        public ETag ETag { get; set; }
        public DateTimeOffset? Timestamp { get; set; }
    }

    internal sealed class MatchdayEventEntity : ITableEntity
    {
        public string PartitionKey { get; set; } = "";
        public string RowKey { get; set; } = "";
        public string? SerializedMatch { get; set; }
        public string? SerializedSwap { get; set; }
        public DateTime CreatedAt { get; set; }
        public ETag ETag { get; set; }
        public DateTimeOffset? Timestamp { get; set; }
    }

    public class MatchdayMetadataEntity : ITableEntity
    {
        public string PartitionKey { get; set; } // matchdayId
        public string RowKey { get; set; } // matchId
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public ETag ETag { get; set; }
        public DateTimeOffset? Timestamp { get; set; }
        public bool IsClosed { get; set; }
        public DateTime? LastMatchCreatedAt { get; set; }
        public string? OwnerId { get; set; }
        public string? GroupId { get; set; }
        public string? SerializedTeams { get; set; }
        public string? TeamsRevision { get; set; }

    }

    public class ChemistryMatchdayEntity : ITableEntity
    {
        public string PartitionKey { get; set; }
        public string RowKey { get; set; }
        public string PairEvidenceJson { get; set; }
        public DateTimeOffset CompletedAt { get; set; }
        public ETag ETag { get; set; }
        public DateTimeOffset? Timestamp { get; set; }
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
        private string _jsonData;
        public string JsonData
        {
            get => _jsonData;
            set => _jsonData = PrivatePlayerFields.SanitizeJson(value);
        }

        public ETag ETag { get; set; }
        public DateTimeOffset? Timestamp { get; set; }
    }

}
