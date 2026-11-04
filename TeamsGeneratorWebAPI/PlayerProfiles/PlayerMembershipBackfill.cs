using TeamsGeneratorWebAPI.GroupJoins;
using TeamsGeneratorWebAPI.Invitations;

namespace TeamsGeneratorWebAPI.PlayerProfiles;

public sealed record PlayerMembershipBackfillResult(bool Applied, int RequestAccounts, int ApprovedRequests,
    int HistoricalBindings, int MissingReceipts);

public sealed class PlayerMembershipBackfill(IInvitationDocumentStore store)
{
    public async Task<PlayerMembershipBackfillResult> RunAsync(bool apply = false, CancellationToken ct = default)
    {
        var memberships = new PlayerMembershipService(store);
        var requestAccounts = 0;
        var approved = 0;
        var bindings = 0;
        var missingReceipts = 0;
        await foreach (var key in Keys("group-joins/v1/groups/", ct))
        {
            var state = (await store.ReadAsync<GroupJoinState>(key, ct))?.Value;
            if (state == null) continue;
            foreach (var requests in state.Requests.Where(request => request.ProfileId != null).GroupBy(request => request.ProfileId!))
            {
                var group = Group(state.OwnerId, state.GroupId);
                var existing = (await store.ReadAsync<PlayerMembership>(PlayerMembershipService.Key(requests.Key, group), ct))?.Value;
                group = group with { Name = string.IsNullOrWhiteSpace(existing?.GroupName) ? state.GroupId : existing.GroupName,
                    Logo = existing?.GroupLogoDataUrl };
                requestAccounts++;
                if (apply) await memberships.TrackAsync(group, requests.Key, ct);
                foreach (var request in requests.Where(request => request.Status is "approved" or "approving"))
                {
                    if (!await new GroupJoinRoster(store).HasReceiptAsync(group, request, ct))
                    {
                        missingReceipts++;
                        continue;
                    }
                    approved++;
                    if (apply) await memberships.RecordAsync(group, request, ct);
                }
            }
        }
        await foreach (var key in Keys("player-memberships/v1/profiles/", ct))
        {
            var membership = (await store.ReadAsync<PlayerMembership>(key, ct))?.Value;
            if (membership == null) continue;
            var profileId = key["player-memberships/v1/profiles/".Length..].Split('/')[0];
            var group = Group(membership.OwnerId, membership.GroupId);
            foreach (var playerKey in membership.PlayerKeys)
            {
                bindings++;
                if (apply) await memberships.BindAsync(group, playerKey, profileId, ct);
            }
        }
        return new(apply, requestAccounts, approved, bindings, missingReceipts);
    }

    private static InvitationGroup Group(string owner, string group) =>
        new(owner, group, "", "", "", null, 0, Array.Empty<RosterPlayer>(), false, "en");

    private async IAsyncEnumerable<string> Keys(string prefix,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        string? continuation = null;
        do
        {
            var page = await store.ListAsync(prefix, continuation, 100, ct);
            foreach (var key in page.Keys) yield return key;
            continuation = page.Continuation;
        } while (continuation != null);
    }
}
