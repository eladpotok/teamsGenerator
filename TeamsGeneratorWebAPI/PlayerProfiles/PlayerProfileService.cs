using System.Security.Claims;
using SkiaSharp;
using TeamsGenerator.API;
using TeamsGeneratorWebAPI.Authentication;
using TeamsGeneratorWebAPI.Invitations;

namespace TeamsGeneratorWebAPI.PlayerProfiles;

public sealed record PlayerProfile
{
    public string Id { get; init; } = "";
    public int Revision { get; init; } = 1;
    public string Name { get; init; } = "";
    public string? PhotoUrl { get; init; }
    public string Background { get; init; } = "";
    public bool IsGoalKeeper { get; init; }
    public bool IsRecoveringFromInjury { get; init; }
    public string? AccountMode { get; init; }
    public Dictionary<string, double> Ratings { get; init; } = new();
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public bool IsComplete => !string.IsNullOrWhiteSpace(Name);
}

public sealed record PlayerProfileUpdate(int Revision, string? Name, string? PhotoUrl,
    string? Background, bool IsGoalKeeper, Dictionary<string, double>? Ratings = null, bool? IsRecoveringFromInjury = null);
public sealed record AccountModeUpdate(int Revision, string? Mode);

public sealed class PlayerProfileService(IInvitationDocumentStore store, IInvitationClock clock)
{
    public static string Key(string uid) => "player-profiles/v1/accounts/" + InvitationKeys.Hash(uid);
    public static string Id(string uid) => !string.IsNullOrWhiteSpace(uid)
        ? InvitationKeys.Hash("player-profile:" + uid)
        : throw new InvitationException(401, "Sign in to load your player profile.");

    public static string Account(ClaimsPrincipal user) => RequestUserId.ResolveRegisteredAccount(user)
        ?? throw new InvitationException(401, "Sign in with a registered account to use your player profile.");

    public async Task<PlayerProfile> EnsureAsync(ClaimsPrincipal user, CancellationToken ct)
    {
        var uid = Account(user);
        var key = Key(uid);
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var stored = await store.ReadAsync<PlayerProfile>(key, ct);
            if (stored != null) return WholeRatings(stored.Value);
            var name = (user.FindFirstValue("name") ?? user.FindFirstValue(ClaimTypes.Name) ?? "").Trim();
            var photo = user.FindFirstValue("picture");
            var profile = new PlayerProfile
            {
                Id = Id(uid),
                Name = ValidName(name) ? name : "",
                PhotoUrl = ValidPhoto(photo) && !string.IsNullOrWhiteSpace(photo) ? photo.Trim() : null,
                CreatedAt = clock.UtcNow, UpdatedAt = clock.UtcNow
            };
            if (await store.WriteAsync(key, profile, null, ct)) return profile;
        }
        throw Conflict();
    }

    public async Task<PlayerProfile> GetAsync(string uid, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(uid)) throw new InvitationException(401, "Sign in to load your player profile.");
        return WholeRatings((await store.ReadAsync<PlayerProfile>(Key(uid), ct))?.Value
            ?? throw new InvitationException(404, "Complete your player profile first."));
    }

    public async Task<PlayerProfile> UpdateAsync(ClaimsPrincipal user, PlayerProfileUpdate input, CancellationToken ct)
    {
        var key = Key(Account(user));
        if (input == null || !ValidName(input.Name) || !ValidPhoto(input.PhotoUrl)
            || input.Background is { Length: > 2000 }
            || (input.Ratings != null && !ValidRatings(input.Ratings)))
            throw new InvitationException(400, "Supply an optional name (up to 80 characters), an optional HTTPS photo URL or PNG photo (up to 256 x 256 and 512 KB), and optional football background (up to 2000). Legacy ratings, if supplied, must contain all five values from 1 to 10.");
        var stored = await store.ReadAsync<PlayerProfile>(key, ct)
            ?? throw new InvitationException(409, "Load your player profile before saving.");
        if (input.Revision != stored.Value.Revision) throw Conflict();
        var updated = stored.Value with
        {
            Name = (input.Name ?? "").Trim(), PhotoUrl = string.IsNullOrWhiteSpace(input.PhotoUrl) ? null : input.PhotoUrl.Trim(),
            Background = (input.Background ?? "").Trim(), IsGoalKeeper = input.IsGoalKeeper,
            IsRecoveringFromInjury = input.IsRecoveringFromInjury ?? stored.Value.IsRecoveringFromInjury,
            Ratings = (input.Ratings ?? stored.Value.Ratings).ToDictionary(rating => rating.Key,
                rating => Math.Round(rating.Value, MidpointRounding.AwayFromZero), StringComparer.Ordinal),
            Revision = stored.Value.Revision + 1, UpdatedAt = clock.UtcNow
        };
        if (!await store.WriteAsync(key, updated, stored.ETag, ct)) throw Conflict();
        return updated;
    }

    public async Task<PlayerProfile> SetModeAsync(ClaimsPrincipal user, AccountModeUpdate input, CancellationToken ct)
    {
        var key = Key(Account(user));
        if (input == null || input.Mode is not (null or "player" or "organizer"))
            throw new InvitationException(400, "Choose player or organizer.");
        var stored = await store.ReadAsync<PlayerProfile>(key, ct)
            ?? throw new InvitationException(409, "Load your player profile before saving.");
        if (input.Revision != stored.Value.Revision) throw Conflict();
        // Workflow preference only; it never grants group access or changes a roster.
        var updated = stored.Value with
        {
            AccountMode = input.Mode, Revision = stored.Value.Revision + 1, UpdatedAt = clock.UtcNow
        };
        if (!await store.WriteAsync(key, updated, stored.ETag, ct)) throw Conflict();
        return WholeRatings(updated);
    }

    internal static bool ValidRatings(Dictionary<string, double>? ratings) =>
        ratings?.Count == 5 && SkillDefinition.CreateDefaults().All(skill =>
            ratings.TryGetValue(skill.Id, out var value) && double.IsFinite(value) && value is >= 1 and <= 10);

    private static PlayerProfile WholeRatings(PlayerProfile profile) => profile with
    {
        Ratings = profile.Ratings.ToDictionary(rating => rating.Key,
            rating => double.IsFinite(rating.Value) && rating.Value is >= 1 and <= 10
                ? Math.Round(rating.Value, MidpointRounding.AwayFromZero) : rating.Value, StringComparer.Ordinal)
    };

    private static bool ValidName(string? name) => name == null
        || (name.Trim().Length <= 80 && !name.Any(char.IsControl));
    private static bool ValidPhoto(string? photo)
    {
        if (string.IsNullOrWhiteSpace(photo)) return true;
        const string prefix = "data:image/png;base64,";
        const int maxBytes = 512 * 1024;
        if (!photo.StartsWith(prefix, StringComparison.Ordinal))
            return photo.Length <= 2048 && Uri.TryCreate(photo.Trim(), UriKind.Absolute, out var url)
                && url.Scheme == Uri.UriSchemeHttps && url.UserInfo.Length == 0;
        if (photo.Length > prefix.Length + 4 * ((maxBytes + 2) / 3)) return false;
        var bytes = new byte[maxBytes];
        if (!Convert.TryFromBase64String(photo[prefix.Length..], bytes, out var length) || length == 0) return false;
        using var stream = new MemoryStream(bytes, 0, length, false);
        using var codec = SKCodec.Create(stream);
        if (codec == null || codec.EncodedFormat != SKEncodedImageFormat.Png
            || codec.Info.Width is < 1 or > 256 || codec.Info.Height is < 1 or > 256 || codec.FrameCount > 1)
            return false;
        using var bitmap = new SKBitmap(codec.Info);
        return codec.GetPixels(bitmap.Info, bitmap.GetPixels()) == SKCodecResult.Success;
    }
    private static InvitationException Conflict() => new(409, "Your player profile changed. Reload it before saving or requesting to join.");
}
