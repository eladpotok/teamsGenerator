using System.Security.Cryptography;
using System.Text;
using Azure.Storage.Blobs;
using Newtonsoft.Json;

namespace TeamsGeneratorWebAPI.Collaboration;

public sealed class GroupMembership
{
    public string OwnerId { get; set; }
    public string GroupId { get; set; }
    public string GroupName { get; set; }
    public string MemberId { get; set; }
    public string MemberName { get; set; }
    public DateTimeOffset JoinedAt { get; set; }
}

public sealed class GroupInvitation
{
    public string OwnerId { get; set; }
    public string GroupId { get; set; }
    public string GroupName { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

public sealed class CreatedGroupInvitation
{
    public string Token { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

public sealed class GroupAccess
{
    public string OwnerId { get; init; }
    public string GroupId { get; init; }
    public bool IsOwner { get; init; }
}

public interface IGroupCollaborationService
{
    Task<CreatedGroupInvitation> CreateInvitationAsync(
        string ownerId,
        string groupId,
        string groupName);

    Task<GroupMembership> RedeemInvitationAsync(
        string memberId,
        string memberName,
        string token);

    Task<IReadOnlyList<GroupMembership>> ListMembershipsAsync(
        string memberId);

    Task<GroupAccess?> ResolveAccessAsync(
        string requesterId,
        string groupReference);
}

public sealed class GroupCollaborationService : IGroupCollaborationService
{
    private readonly BlobContainerClient _container;

    public GroupCollaborationService(IConfiguration configuration)
    {
        _container = new BlobContainerClient(
            configuration.GetValue<string>("BlobConnectionString"),
            configuration.GetValue<string>("PlayersBlobContainerName"));
    }

    public async Task<CreatedGroupInvitation> CreateInvitationAsync(
        string ownerId,
        string groupId,
        string groupName)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        var invite = new GroupInvitation
        {
            OwnerId = ownerId,
            GroupId = NormalizeGroupId(groupId),
            GroupName = string.IsNullOrWhiteSpace(groupName)
                ? "Player group"
                : groupName.Trim(),
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(7)
        };
        await _container.GetBlobClient(GetInvitePath(token))
            .UploadAsync(
                BinaryData.FromString(JsonConvert.SerializeObject(invite)),
                overwrite: false);
        return new CreatedGroupInvitation
        {
            Token = token,
            ExpiresAt = invite.ExpiresAt
        };
    }

    public async Task<GroupMembership> RedeemInvitationAsync(
        string memberId,
        string memberName,
        string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ArgumentException(
                "A co-organizer invitation token is required.");
        }
        var inviteClient = _container.GetBlobClient(GetInvitePath(token));
        if (!await inviteClient.ExistsAsync())
        {
            throw new ArgumentException(
                "This co-organizer invitation is invalid or has been revoked.");
        }
        var content = await inviteClient.DownloadContentAsync();
        var invite = JsonConvert.DeserializeObject<GroupInvitation>(
            content.Value.Content.ToString());
        if (invite == null || invite.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            throw new ArgumentException(
                "This co-organizer invitation has expired.");
        }
        if (string.Equals(invite.OwnerId, memberId, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The group owner cannot join as a co-organizer.");
        }

        var memberships = (await ListMembershipsAsync(memberId)).ToList();
        var membership = memberships.FirstOrDefault(item =>
            item.OwnerId == invite.OwnerId
            && item.GroupId == invite.GroupId);
        if (membership == null)
        {
            membership = new GroupMembership
            {
                OwnerId = invite.OwnerId,
                GroupId = invite.GroupId,
                GroupName = invite.GroupName,
                MemberId = memberId,
                MemberName = memberName,
                JoinedAt = DateTimeOffset.UtcNow
            };
            memberships.Add(membership);
            await SaveMembershipsAsync(memberId, memberships);
        }
        await inviteClient.DeleteIfExistsAsync();
        return membership;
    }

    public async Task<IReadOnlyList<GroupMembership>> ListMembershipsAsync(
        string memberId)
    {
        var client = _container.GetBlobClient(GetMembershipPath(memberId));
        if (!await client.ExistsAsync())
        {
            return Array.Empty<GroupMembership>();
        }
        var content = await client.DownloadContentAsync();
        return JsonConvert.DeserializeObject<List<GroupMembership>>(
            content.Value.Content.ToString()) ?? new List<GroupMembership>();
    }

    public async Task<GroupAccess?> ResolveAccessAsync(
        string requesterId,
        string groupReference)
    {
        if (!TryParseSharedGroup(groupReference, out var ownerId, out var groupId))
        {
            return new GroupAccess
            {
                OwnerId = requesterId,
                GroupId = NormalizeGroupId(groupReference),
                IsOwner = true
            };
        }
        var hasMembership = (await ListMembershipsAsync(requesterId)).Any(item =>
            item.OwnerId == ownerId && item.GroupId == groupId);
        return hasMembership
            ? new GroupAccess
            {
                OwnerId = ownerId,
                GroupId = groupId,
                IsOwner = false
            }
            : null;
    }

    public static string CreateSharedGroupId(string ownerId, string groupId)
    {
        var encodedOwner = Convert.ToBase64String(
                Encoding.UTF8.GetBytes(ownerId))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        return $"shared.{encodedOwner}.{NormalizeGroupId(groupId)}";
    }

    private async Task SaveMembershipsAsync(
        string memberId,
        IReadOnlyList<GroupMembership> memberships)
    {
        await _container.GetBlobClient(GetMembershipPath(memberId))
            .UploadAsync(
                BinaryData.FromString(
                    JsonConvert.SerializeObject(memberships)),
                overwrite: true);
    }

    private static bool TryParseSharedGroup(
        string value,
        out string ownerId,
        out string groupId)
    {
        ownerId = string.Empty;
        groupId = string.Empty;
        var parts = value?.Split('.', 3);
        if (parts?.Length != 3 || parts[0] != "shared")
        {
            return false;
        }
        try
        {
            var encoded = parts[1].Replace('-', '+').Replace('_', '/');
            encoded = encoded.PadRight(
                encoded.Length + ((4 - encoded.Length % 4) % 4),
                '=');
            ownerId = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            groupId = NormalizeGroupId(parts[2]);
            return !string.IsNullOrWhiteSpace(ownerId);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string GetInvitePath(string token)
    {
        var hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        return $"collaboration/invitations/{hash}.json";
    }

    private static string GetMembershipPath(string memberId) =>
        $"{memberId}_private/group-memberships.json";

    private static string NormalizeGroupId(string groupId) =>
        string.IsNullOrWhiteSpace(groupId)
            ? "default"
            : groupId.Trim().ToLowerInvariant();
}
