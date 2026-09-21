using TeamsGenerator.API;
using TeamsGeneratorWebAPI.Collaboration;
using TeamsGeneratorWebAPI.ConfigBlob;
using TeamsGeneratorWebAPI.PlayersBlob;
using TeamsGeneratorWebAPI.Premium;

namespace TeamsGeneratorWebAPI.Invitations;

public interface IInvitationGroupSource
{
    Task<InvitationGroup> ResolveAsync(string requesterId, string groupReference, CancellationToken cancellationToken);
    Task<InvitationGroup> LoadAsync(string ownerId, string groupId, CancellationToken cancellationToken);
}

public sealed class InvitationGroupSource : IInvitationGroupSource
{
    private readonly IGroupCollaborationService _collaboration;
    private readonly IPlayersStorageBlobConnector _players;
    private readonly IUserConfigAzureStorage _config;
    private readonly IAccountEntitlementService _entitlements;
    public InvitationGroupSource(IGroupCollaborationService collaboration, IPlayersStorageBlobConnector players,
        IUserConfigAzureStorage config, IAccountEntitlementService entitlements)
        => (_collaboration, _players, _config, _entitlements) = (collaboration, players, config, entitlements);

    public async Task<InvitationGroup> ResolveAsync(string requesterId, string groupReference, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(groupReference) || groupReference.Length > 500)
            throw new InvitationException(400, "A valid groupId is required.");
        var access = await _collaboration.ResolveAccessAsync(requesterId, groupReference);
        if (access == null) throw new InvitationException(403, "Group access denied.");
        return await LoadAsync(access.OwnerId, access.GroupId, cancellationToken);
    }

    public async Task<InvitationGroup> LoadAsync(string ownerId, string groupId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var groups = await _players.ListGroupsAsync(ownerId);
        if (!groups.Success) throw new InvitationException(503, "Group storage is unavailable.");
        var group = groups.Groups.FirstOrDefault(item => item.Id == groupId)
            ?? throw new InvitationException(404, "Group not found.");
        var defaults = await LoadConfigAsync(ownerId, PlayersStorageBlobConnector.DefaultGroupId) ?? new UserConfigResponse();
        var entitlements = await _entitlements.GetAsync(ownerId, cancellationToken: cancellationToken);
        var config = groupId == PlayersStorageBlobConnector.DefaultGroupId ? defaults
            : await LoadConfigAsync(ownerId, groupId) ?? UserConfigScopes.InheritGroupSettings(defaults);
        config ??= new UserConfigResponse();
        var response = await _players.ListAsync(new PlayersBlobConfig
            { UId = ownerId, GroupId = groupId, AlgoType = config.SelectedAlgoKey }) as GetPlayersResponse;
        if (response?.Success != true && response?.Error != "Players were not found")
            throw new InvitationException(503, "The current group roster could not be loaded.");
        var roster = response?.Success == true
            ? response.Players.Select(player => new RosterPlayer(player.Key, player.Name)).ToArray()
            : Array.Empty<RosterPlayer>();
        if (roster.Any(player => string.IsNullOrWhiteSpace(player.Key))
            || roster.Select(player => player.Key).Distinct(StringComparer.Ordinal).Count() != roster.Length)
            throw new InvitationException(409, "The group roster needs unique player identifiers.");
        return new(ownerId, groupId, group.Name, config.TeamName ?? "", config.Location ?? "",
            string.IsNullOrEmpty(config.GroupLogoDataUrl) ? null : config.GroupLogoDataUrl,
            config.HasCustomMatchdayPlayerLimit ? Math.Clamp(config.MaxMatchdayPlayers, 5, 50)
                : Math.Clamp(config.NumberOfTeams * 5, 5, 50), roster,
            defaults.EnableMatchInvitations && entitlements.CanUseMatchInvitations,
            NormalizeLanguage(defaults.Language));
    }

    public static string NormalizeLanguage(string? language) =>
        language?.Trim().ToLowerInvariant() is "he" or "heb" or "he-il" or "iw" ? "he" : "en";

    private async Task<UserConfigResponse?> LoadConfigAsync(string owner, string group)
    {
        var response = await _config.ListAsync(new UserConfigBlobConfig { UId = owner, GroupId = group })
            as GetConfigResponse;
        if (response?.Success == true) return response.Config;
        if (response?.Error == "config isn't found") return null;
        throw new InvitationException(503, "Group configuration could not be loaded.");
    }
}
