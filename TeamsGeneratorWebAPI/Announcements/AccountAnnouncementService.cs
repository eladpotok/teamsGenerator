using System.Security.Claims;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using TeamsGeneratorWebAPI.Authentication;
using TeamsGeneratorWebAPI.Invitations;

namespace TeamsGeneratorWebAPI.Announcements;

public sealed class AccountAnnouncementService(IInvitationDocumentStore store,
    IOptions<AnnouncementOptions> options, TimeProvider clock)
{
    public const string CatalogKey = "account-announcements/v1/catalog";
    public static string DismissalKey(string uid) => "account-announcements/v1/dismissals/" + InvitationKeys.Hash(uid);

    public async Task<AnnouncementFeed> FeedAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var uid = Account(user);
        if (!options.Value.Enabled) return new(false, false, []);
        var catalog = await ReadCatalogAsync(ct);
        var now = clock.GetUtcNow();
        var eligible = catalog?.Value.Items.Where(item => !item.Withdrawn
            && item.PublishedAt <= now && item.ExpiresAt > now && Receives(item, uid)).ToArray() ?? [];
        if (eligible.Length == 0) return new(true, CanManage(uid), []);
        var dismissed = await ReadDismissalsAsync(uid, ct);
        var ids = dismissed?.Value.Ids.ToHashSet(StringComparer.Ordinal) ?? [];
        return new(true, CanManage(uid), eligible.Where(item => !ids.Contains(item.Id))
            .OrderByDescending(item => item.PublishedAt).ThenBy(item => item.Id, StringComparer.Ordinal)
            .Select(Message).ToArray());
    }

    public async Task<IReadOnlyList<AnnouncementAdminMessage>> ListAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        RequireAdministrator(user);
        ct.ThrowIfCancellationRequested();
        var catalog = await ReadCatalogAsync(ct);
        return catalog?.Value.Items.OrderByDescending(item => item.PublishedAt)
            .ThenBy(item => item.Id, StringComparer.Ordinal).Select(AdminMessage).ToArray() ?? [];
    }

    public async Task<AnnouncementAdminMessage> PublishAsync(ClaimsPrincipal user,
        PublishAnnouncementRequest input, CancellationToken ct)
    {
        var uid = RequireAdministrator(user);
        if (input == null) throw new AnnouncementException(400, "announcement_invalid_content");
        var id = Id(input.Id);
        var title = input.Title?.Trim();
        var message = input.Message?.Trim();
        if (!ValidText(title, AnnouncementOptions.MaximumTitleLength, false)
            || !ValidText(message, AnnouncementOptions.MaximumMessageLength, true)
            || input.Audience is not ("all" or "accounts"))
            throw new AnnouncementException(400, "announcement_invalid_content");
        var targets = input.TargetUserIds ?? [];
        if (targets.Length > AnnouncementOptions.MaximumTargets || targets.Any(target => !AnnouncementOptions.ValidUserId(target))
            || (input.Audience == "all" ? targets.Length != 0 : targets.Length == 0))
            throw new AnnouncementException(400, "announcement_invalid_audience");
        targets = targets.Distinct(StringComparer.Ordinal).OrderBy(target => target, StringComparer.Ordinal).ToArray();
        var expires = input.ExpiresAt.ToUniversalTime();
        var hash = InvitationKeys.Hash(JsonConvert.SerializeObject(new { uid, title, message, input.Audience, targets, expires }));
        for (var attempt = 0; attempt < 8; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var catalog = await ReadCatalogAsync(ct);
            var items = catalog?.Value.Items ?? [];
            var prior = items.SingleOrDefault(item => item.Id == id);
            if (prior != null)
            {
                if (prior.RequestHash != hash) throw new AnnouncementException(409, "announcement_id_conflict");
                return AdminMessage(prior);
            }
            var now = clock.GetUtcNow();
            if (expires <= now || expires > now.AddDays(AnnouncementOptions.MaximumLifetimeDays))
                throw new AnnouncementException(400, "announcement_invalid_expiry");
            var retained = items.Where(item => item.ExpiresAt > now).ToList();
            if (retained.Count >= AnnouncementOptions.MaximumAnnouncements)
                throw new AnnouncementException(409, "announcement_limit");
            var announcement = new StoredAnnouncement
            {
                Id = id, Title = title!, Message = message!, Audience = input.Audience!,
                TargetUserIds = targets, PublishedAt = now, ExpiresAt = expires, RequestHash = hash
            };
            retained.Add(announcement);
            if (await store.WriteAsync(CatalogKey, new AnnouncementCatalog { Items = retained }, catalog?.ETag, ct))
                return AdminMessage(announcement);
        }
        throw new AnnouncementException(409, "announcements_changed");
    }

    public async Task WithdrawAsync(ClaimsPrincipal user, string messageId, CancellationToken ct)
    {
        RequireAdministrator(user);
        var id = Id(messageId);
        for (var attempt = 0; attempt < 8; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var catalog = await ReadCatalogAsync(ct);
            var item = catalog?.Value.Items.SingleOrDefault(item => item.Id == id)
                ?? throw new AnnouncementException(404, "announcement_not_found");
            if (item.Withdrawn) return;
            var updated = catalog!.Value with
            {
                Items = catalog.Value.Items.Select(item => item.Id == id ? item with { Withdrawn = true } : item).ToList()
            };
            if (await store.WriteAsync(CatalogKey, updated, catalog.ETag, ct)) return;
        }
        throw new AnnouncementException(409, "announcements_changed");
    }

    public async Task DismissAsync(ClaimsPrincipal user, string messageId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var uid = Account(user);
        if (!options.Value.Enabled) throw new AnnouncementException(503, "announcements_disabled");
        var id = Id(messageId);
        for (var attempt = 0; attempt < 8; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var stored = await ReadDismissalsAsync(uid, ct);
            var catalog = await ReadCatalogAsync(ct);
            if (catalog?.Value.Items.Any(item => item.Id == id && Receives(item, uid)) != true)
                throw new AnnouncementException(404, "announcement_not_found");
            if (stored?.Value.Ids.Contains(id, StringComparer.Ordinal) == true) return;
            var knownIds = catalog.Value.Items.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);
            var ids = stored?.Value.Ids.Where(knownIds.Contains).ToHashSet(StringComparer.Ordinal) ?? [];
            ids.Add(id);
            if (await store.WriteAsync(DismissalKey(uid),
                new AnnouncementDismissals { Ids = ids.OrderBy(value => value, StringComparer.Ordinal).ToList() }, stored?.ETag, ct)) return;
        }
        throw new AnnouncementException(409, "announcements_changed");
    }

    private async Task<StoredDocument<AnnouncementCatalog>?> ReadCatalogAsync(CancellationToken ct)
    {
        var stored = await store.ReadAsync<AnnouncementCatalog>(CatalogKey, ct);
        if (stored != null && (stored.Value.Items == null
            || stored.Value.Items.Count > AnnouncementOptions.MaximumAnnouncements
            || stored.Value.Items.Any(item => item == null || !Guid.TryParseExact(item.Id, "D", out _)
                || !ValidText(item.Title, AnnouncementOptions.MaximumTitleLength, false)
                || !ValidText(item.Message, AnnouncementOptions.MaximumMessageLength, true)
                || item.TargetUserIds == null || item.TargetUserIds.Length > AnnouncementOptions.MaximumTargets
                || item.TargetUserIds.Any(target => !AnnouncementOptions.ValidUserId(target))
                || item.Audience is not ("all" or "accounts")
                || (item.Audience == "all" ? item.TargetUserIds.Length != 0 : item.TargetUserIds.Length == 0)
                || item.ExpiresAt <= item.PublishedAt)
            || stored.Value.Items.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != stored.Value.Items.Count))
            throw new InvalidDataException("Invalid announcement catalog.");
        return stored;
    }

    private async Task<StoredDocument<AnnouncementDismissals>?> ReadDismissalsAsync(string uid, CancellationToken ct)
    {
        var stored = await store.ReadAsync<AnnouncementDismissals>(DismissalKey(uid), ct);
        if (stored != null && (stored.Value.Ids == null || stored.Value.Ids.Count > AnnouncementOptions.MaximumAnnouncements
            || stored.Value.Ids.Any(id => !Guid.TryParseExact(id, "D", out _))))
            throw new InvalidDataException("Invalid announcement dismissals.");
        return stored;
    }

    private static bool ValidText(string? value, int maximum, bool multiline) => !string.IsNullOrWhiteSpace(value)
        && value.Length <= maximum && !value.Any(c => char.IsControl(c) && !(multiline && c is '\r' or '\n' or '\t'));
    private static bool Receives(StoredAnnouncement item, string uid) => item.Audience == "all"
        || item.TargetUserIds.Contains(uid, StringComparer.Ordinal);
    private static AnnouncementMessage Message(StoredAnnouncement item) =>
        new(item.Id, item.Title, item.Message, item.PublishedAt, item.ExpiresAt);
    private static AnnouncementAdminMessage AdminMessage(StoredAnnouncement item) =>
        new(item.Id, item.Title, item.Message, item.Audience, item.TargetUserIds, item.PublishedAt, item.ExpiresAt, item.Withdrawn);
    private static string Account(ClaimsPrincipal user) => RequestUserId.ResolveRegisteredAccount(user)
        ?? throw new AnnouncementException(401, "announcement_sign_in_required");
    private bool CanManage(string uid) => options.Value.Enabled
        && options.Value.AdminUserIds?.Contains(uid, StringComparer.Ordinal) == true;
    private string RequireAdministrator(ClaimsPrincipal user)
    {
        var uid = Account(user);
        if (!CanManage(uid)) throw new AnnouncementException(403, "announcement_admin_required");
        return uid;
    }
    private static string Id(string? value) => Guid.TryParseExact(value, "D", out var id)
        ? id.ToString("D") : throw new AnnouncementException(400, "announcement_invalid_id");
}
