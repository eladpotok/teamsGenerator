namespace TeamsGeneratorWebAPI.Invitations;

public record CreateInvitationRequest(string GroupId, string ScheduledAt, string TimeZone);
public record InvitationResponseRequest(string PlayerKey, string Status);
public record DeviceRequest(string Token, string? Platform);
public record NotificationPreferences(bool Enabled = true, int PlayerThreshold = 10, int IntervalMinutes = 360);
public record NotificationPreferencesResponse(bool Enabled, int PlayerThreshold, int IntervalMinutes, bool DeliveryConfigured);
public record InvitationPlayer(string Key, string Name, string Status, int? WaitingListPosition);
public record InvitationCounts(int In, int Waiting, int Out);

public class PublicInvitation
{
    public string Id { get; set; } = "";
    public string GroupName { get; set; } = "";
    public string MatchName { get; set; } = "";
    public string Location { get; set; } = "";
    public string? GroupLogoDataUrl { get; set; }
    public DateTimeOffset ScheduledAt { get; set; }
    public string TimeZone { get; set; } = "";
    public string Language { get; set; } = "en";
    public int Capacity { get; set; }
    public string Status { get; set; } = "open";
    public long Revision { get; set; }
    public List<InvitationPlayer> Players { get; set; } = new();
    public InvitationCounts Counts { get; set; } = new(0, 0, 0);
}

public sealed class OrganizerInvitation : PublicInvitation
{
    public string Token { get; set; } = "";
    public string GroupId { get; set; } = "";
}

public record RosterPlayer(string Key, string Name);
public record InvitationGroup(string OwnerId, string GroupId, string Name, string MatchName,
    string Location, string? Logo, int Capacity, IReadOnlyList<RosterPlayer> Players,
    bool InvitationsEnabled, string Language);
public record Rsvp(string Status, long Order);
public record ResponseEvent(long Revision, string PlayerKey, string Status, DateTimeOffset At);
public record TokenPointer(string InvitationId);
public record RegisteredDevice(string Token, DateTimeOffset UpdatedAt, string? OwnershipVersion = null);
public record DeviceOwnership(string? OwnerId, string Version, DateTimeOffset RegisteredAt);
public sealed class NotificationAccount
{
    public NotificationPreferences Preferences { get; set; } = new();
    public Dictionary<string, RegisteredDevice> Devices { get; set; } = new();
}

// One conditional blob write commits the RSVP, event journal, and pending digest together.
public sealed class InvitationState
{
    public string Id { get; set; } = "";
    public string OwnerId { get; set; } = "";
    public string GroupId { get; set; } = "";
    public string Token { get; set; } = "";
    public string GroupName { get; set; } = "";
    public string MatchName { get; set; } = "";
    public string Location { get; set; } = "";
    public string? GroupLogoDataUrl { get; set; }
    public DateTimeOffset ScheduledAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public string TimeZone { get; set; } = "";
    public int Capacity { get; set; }
    public bool Revoked { get; set; }
    public long Revision { get; set; }
    public Dictionary<string, Rsvp> Responses { get; set; } = new();
    public List<ResponseEvent> Events { get; set; } = new();
    public Dictionary<string, bool> Baseline { get; set; } = new();
    public long BaselineRevision { get; set; }
    public DateTimeOffset? PendingSince { get; set; }
    public DateTimeOffset? LastDeliveredAt { get; set; }
    public DateTimeOffset? RetryAt { get; set; }
    public int Failures { get; set; }
    public string? LeaseOwner { get; set; }
    public DateTimeOffset? LeaseUntil { get; set; }
    public DigestBatch? Batch { get; set; }
}

public sealed class DigestBatch
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public long Revision { get; set; }
    public string Summary { get; set; } = "";
    public Dictionary<string, bool> Snapshot { get; set; } = new();
    public HashSet<string> TargetDevices { get; set; } = new();
    public HashSet<string> CompletedDevices { get; set; } = new();
    public bool AnyDelivered { get; set; }
}

public sealed class InvitationException : Exception
{
    public int StatusCode { get; }
    public InvitationException(int statusCode, string message) : base(message) => StatusCode = statusCode;
}

public interface IInvitationClock { DateTimeOffset UtcNow { get; } }
public sealed class InvitationClock : IInvitationClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
