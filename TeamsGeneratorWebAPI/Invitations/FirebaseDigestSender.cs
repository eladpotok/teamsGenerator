using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;

namespace TeamsGeneratorWebAPI.Invitations;

public enum PushResult { Delivered, InvalidDevice, Retry, Unavailable }
public interface IInvitationPushSender
{
    bool Enabled { get; }
    Task<bool> IsDeliveryConfiguredAsync(CancellationToken ct);
    Task<PushResult> SendAsync(string deviceToken, InvitationState invitation, DigestBatch batch, CancellationToken ct);
}

public interface IInvitationPromotionPushSender
{
    bool Enabled { get; }
    Task<PushResult> SendPromotionAsync(string deviceToken, string userId, InvitationState invitation,
        PromotionNotification promotion, string language, CancellationToken ct);
}

public sealed class FirebaseDigestSender : IInvitationPushSender, IInvitationPromotionPushSender, IDisposable
{
    private readonly string? _projectId;
    private readonly ILogger<FirebaseDigestSender> _logger;
    private readonly SemaphoreSlim _initialize = new(1, 1);
    private FirebaseApp? _app;
    private DateTimeOffset _initializeAfter;
    public bool Enabled { get; }

    public FirebaseDigestSender(IConfiguration config, ILogger<FirebaseDigestSender> logger)
    {
        _logger = logger;
        _projectId = config["Notifications:Fcm:ProjectId"] ?? config["Firebase:ProjectId"];
        Enabled = config.GetValue<bool>("Notifications:Fcm:Enabled") && !string.IsNullOrWhiteSpace(_projectId);
        if (!Enabled)
            _logger.LogWarning("Invitation push delivery is NOT configured. RSVP remains available; pending notifications are retained. Enable Notifications:Fcm and provision Application Default Credentials.");
    }

    public async Task<bool> IsDeliveryConfiguredAsync(CancellationToken ct)
    {
        if (!Enabled) return false;
        if (_app == null)
        {
            await _initialize.WaitAsync(ct);
            try
            {
                if (_app == null)
                {
                    if (DateTimeOffset.UtcNow < _initializeAfter) return false;
                    try
                    {
                        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        timeout.CancelAfter(TimeSpan.FromSeconds(5));
                        var credential = await GoogleCredential.GetApplicationDefaultAsync(timeout.Token);
                        _app = FirebaseApp.Create(new AppOptions { Credential = credential, ProjectId = _projectId },
                            "teamify-invitations-" + Guid.NewGuid().ToString("N"));
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or IOException or System.Security.SecurityException or ArgumentException or HttpRequestException)
                    {
                        _initializeAfter = DateTimeOffset.UtcNow.AddMinutes(5);
                        _logger.LogWarning("Invitation push credentials are unavailable ({ErrorType}); notifications remain queued.", ex.GetType().Name);
                        return false;
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        _initializeAfter = DateTimeOffset.UtcNow.AddMinutes(5);
                        _logger.LogWarning("Invitation push credential discovery timed out; notifications remain queued.");
                        return false;
                    }
                }
            }
            finally { _initialize.Release(); }
        }
        return true;
    }

    public async Task<PushResult> SendAsync(string deviceToken, InvitationState invitation, DigestBatch batch, CancellationToken ct)
        => await SendMessageAsync(BuildMessage(deviceToken, invitation, batch, DateTimeOffset.UtcNow), ct);

    public async Task<PushResult> SendPromotionAsync(string deviceToken, string userId, InvitationState invitation,
        PromotionNotification promotion, string language, CancellationToken ct)
        => await SendMessageAsync(BuildPromotionMessage(deviceToken, userId, invitation, promotion, language,
            DateTimeOffset.UtcNow), ct);

    private async Task<PushResult> SendMessageAsync(Message message, CancellationToken ct)
    {
        if (!await IsDeliveryConfiguredAsync(ct)) return PushResult.Unavailable;
        try
        {
            await FirebaseMessaging.GetMessaging(_app).SendAsync(message, ct);
            return PushResult.Delivered;
        }
        catch (FirebaseMessagingException ex)
        {
            // Never log exception messages: provider diagnostics can contain registration tokens.
            _logger.LogWarning("Invitation FCM delivery failed with code {Code}.", ex.MessagingErrorCode);
            return ex.MessagingErrorCode is MessagingErrorCode.Unregistered or MessagingErrorCode.SenderIdMismatch
                ? PushResult.InvalidDevice : PushResult.Retry;
        }
        catch (HttpRequestException)
        {
            _logger.LogWarning("Invitation FCM transport is unavailable; notifications remain queued.");
            return PushResult.Retry;
        }
        catch (Google.Apis.Auth.OAuth2.Responses.TokenResponseException)
        {
            _logger.LogWarning("Invitation FCM credential authentication failed; notifications remain queued.");
            return PushResult.Unavailable;
        }
    }

    public static Message BuildMessage(string token, InvitationState invitation, DigestBatch batch, DateTimeOffset now) => new()
    {
        Token = token,
        Notification = new Notification { Title = invitation.GroupName, Body = batch.Summary },
        Data = new Dictionary<string, string>
        {
            ["invitationId"] = invitation.Id, ["groupId"] = invitation.GroupId,
            ["scheduledAt"] = invitation.ScheduledAt.UtcDateTime.ToString("O"), ["digestId"] = batch.Id
        },
        Android = new AndroidConfig
        {
            Priority = Priority.Normal, CollapseKey = invitation.Id,
            TimeToLive = TimeSpan.FromSeconds(Math.Clamp((invitation.ScheduledAt - now).TotalSeconds, 0, 6 * 3600)),
            Notification = new AndroidNotification { Tag = invitation.Id, ChannelId = "matchday-rsvp" }
        }
    };

    public static Message BuildPromotionMessage(string token, string userId, InvitationState invitation,
        PromotionNotification promotion, string language, DateTimeOffset now) => new()
    {
        Token = token,
        Notification = new Notification
        {
            Title = language == "he" ? "אתם בפנים!" : "You're in!",
            Body = language == "he"
                ? $"{invitation.GroupName} · {invitation.MatchName}: התפנה מקום ועברתם מרשימת ההמתנה לרשימת המשתתפים."
                : $"{invitation.GroupName} · {invitation.MatchName}: a spot opened up. You've moved from the waiting list into the confirmed squad."
        },
        Data = new Dictionary<string, string>
        {
            ["type"] = "waitlist_promotion", ["invitationId"] = invitation.Id,
            ["invitationToken"] = invitation.Token, ["groupId"] = invitation.GroupId,
            ["scheduledAt"] = invitation.ScheduledAt.UtcDateTime.ToString("O"),
            ["notificationId"] = promotion.Id, ["recipientUserId"] = userId
        },
        Android = new AndroidConfig
        {
            Priority = Priority.High, CollapseKey = promotion.Id,
            TimeToLive = TimeSpan.FromSeconds(Math.Clamp((invitation.ScheduledAt - now).TotalSeconds, 0, 6 * 3600)),
            Notification = new AndroidNotification
            {
                Tag = promotion.Id, ChannelId = "matchday-promotions", Sound = "default"
            }
        }
    };

    public void Dispose()
    {
        _app?.Delete();
        _initialize.Dispose();
    }
}
