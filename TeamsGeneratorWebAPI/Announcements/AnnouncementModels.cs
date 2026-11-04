using Microsoft.Extensions.Options;

namespace TeamsGeneratorWebAPI.Announcements;

public sealed class AnnouncementOptions
{
    public bool Enabled { get; set; }
    public string[] AdminUserIds { get; set; } = [];
    public const int MaximumAnnouncements = 50;
    public const int MaximumTargets = 50;
    public const int MaximumTitleLength = 120;
    public const int MaximumMessageLength = 2000;
    public const int MaximumLifetimeDays = 90;

    public static bool ValidUserId(string? value) => value is { Length: > 0 and <= 128 }
        && value == value.Trim() && !value.Any(char.IsControl);
}

public static class AnnouncementRegistration
{
    public static IServiceCollection AddAccountAnnouncements(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AnnouncementOptions>().Bind(configuration.GetSection("Announcements"))
            .Validate(o => o.AdminUserIds != null && o.AdminUserIds.Length <= 10
                && o.AdminUserIds.All(AnnouncementOptions.ValidUserId),
                "Announcement administrators must be at most ten Firebase user IDs.")
            .ValidateOnStart();
        services.AddSingleton<AccountAnnouncementService>();
        services.AddScoped<AnnouncementExceptionFilter>();
        return services;
    }
}

public sealed record PublishAnnouncementRequest(string? Id, string? Title, string? Message,
    string? Audience, string[]? TargetUserIds, DateTimeOffset ExpiresAt);
public sealed record AnnouncementMessage(string Id, string Title, string Message,
    DateTimeOffset PublishedAt, DateTimeOffset ExpiresAt);
public sealed record AnnouncementFeed(bool Enabled, bool CanManage, IReadOnlyList<AnnouncementMessage> Items);
public sealed record AnnouncementAdminMessage(string Id, string Title, string Message,
    string Audience, string[] TargetUserIds, DateTimeOffset PublishedAt, DateTimeOffset ExpiresAt, bool Withdrawn);

public sealed record StoredAnnouncement
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string Message { get; init; } = "";
    public string Audience { get; init; } = "";
    public string[] TargetUserIds { get; init; } = [];
    public DateTimeOffset PublishedAt { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public bool Withdrawn { get; init; }
    public string RequestHash { get; init; } = "";
}

public sealed record AnnouncementCatalog
{
    public List<StoredAnnouncement> Items { get; init; } = [];
}

public sealed record AnnouncementDismissals
{
    public List<string> Ids { get; init; } = [];
}

public sealed class AnnouncementException(int status, string code) : Exception(code)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}
