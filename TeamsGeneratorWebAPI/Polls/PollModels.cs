using Azure;
using Azure.Data.Tables;
using Newtonsoft.Json;
using System.Security.Cryptography;
using System.Text;

namespace TeamsGeneratorWebAPI.Polls;

public sealed record PollCandidate(string Key, string Name);
public sealed record PollResult(string Key, string Name, long FirstChoiceVotes, long SecondChoiceVotes, long Points, int Rank);
public sealed record PollSnapshot(string Token, string MatchdayKey, string Status, DateTimeOffset ClosesAt,
    IReadOnlyList<PollCandidate> Candidates, bool HasVoted,
    [property: JsonProperty(NullValueHandling = NullValueHandling.Ignore)] long? BallotCount = null,
    [property: JsonProperty(NullValueHandling = NullValueHandling.Ignore)] IReadOnlyList<PollResult>? Results = null,
    [property: JsonProperty(NullValueHandling = NullValueHandling.Ignore)] DateTimeOffset? EventStartedAt = null,
    [property: JsonProperty(NullValueHandling = NullValueHandling.Ignore)] PollEventDetails? EventDetails = null);
public sealed record PollEventDetails(string MatchName, string Location, DateTimeOffset ScheduledAt, string TimeZone, string Language)
{
    public PollEventDetails Validate()
    {
        if (MatchName == null || MatchName.Length > 200 || Location == null || Location.Length > 500
            || MatchName.Any(char.IsControl) || Location.Any(char.IsControl)
            || ScheduledAt <= DateTimeOffset.UnixEpoch || ScheduledAt.Year > 9998
            || string.IsNullOrWhiteSpace(TimeZone) || TimeZone.Length > 100
            || Language is not ("en" or "he"))
            throw Invalid();
        try { TimeZoneInfo.FindSystemTimeZoneById(TimeZone); }
        catch (TimeZoneNotFoundException) { throw Invalid(); }
        catch (InvalidTimeZoneException) { throw Invalid(); }
        return this with { MatchName = MatchName.Trim(), Location = Location.Trim() };
    }

    private static PollException Invalid() => new(400, "invalid_event_details", "The match event details are invalid. Check the event settings before sharing.");
}
public sealed record PollBallotRequest(string? FirstChoiceKey, string? SecondChoiceKey);
public sealed class PollException(int statusCode, string errorCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string ErrorCode { get; } = errorCode;
}

public sealed class PollTally
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public long FirstChoiceVotes { get; set; }
    public long SecondChoiceVotes { get; set; }
}

public sealed class PollEntity : ITableEntity
{
    public string PartitionKey { get; set; } = "";
    public string RowKey { get; set; } = "poll";
    public ETag ETag { get; set; }
    public DateTimeOffset? Timestamp { get; set; }
    public string Token { get; set; } = "";
    public string MatchdayKey { get; set; } = "";
    public string TalliesJson { get; set; } = "";
    public DateTimeOffset ClosesAt { get; set; }
    public bool Closed { get; set; }
    public long BallotCount { get; set; }
    public string? EventDetailsJson { get; set; }
}

public sealed class PollBallotEntity : ITableEntity
{
    public string PartitionKey { get; set; } = "";
    public string RowKey { get; set; } = "";
    public ETag ETag { get; set; }
    public DateTimeOffset? Timestamp { get; set; }
    public string FirstChoiceKey { get; set; } = "";
    public string SecondChoiceKey { get; set; } = "";
}

public static class PollKeys
{
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    public static string NewSecret() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
    public static bool IsSecret(string? value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    public static string Partition(string matchdayKey) => "day-" + Hash(matchdayKey);
    public static string TokenPartition(string token)
    {
        var parts = token.Split('.');
        if (parts.Length != 2 || !IsSecret(parts[0]) || !IsSecret(parts[1]))
            throw new PollException(404, "poll_not_found", "The poll was not found.");
        return "day-" + parts[0];
    }
}
