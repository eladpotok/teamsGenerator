using Newtonsoft.Json.Linq;
using TeamsGenerator.API;
using TeamsGenerator.Orchestration;
using TeamsGeneratorWebAPI.Invitations;
using TeamsGeneratorWebAPI.PlayersBlob;

namespace TeamsGeneratorWebAPI.GroupJoins;

public sealed class GroupJoinRoster
{
    private readonly IInvitationDocumentStore _store;
    public GroupJoinRoster(IInvitationDocumentStore store) => _store = store;

    private static string Path(InvitationGroup group, int algorithm) =>
        PlayersStorageBlobConnector.GetPlayersPath(new PlayersBlobConfig
            { UId = group.OwnerId, GroupId = group.GroupId, AlgoType = algorithm });

    internal async Task<bool> HasReceiptAsync(InvitationGroup group, GroupJoinRequest request, CancellationToken ct) =>
        request.ApprovedAlgorithm.HasValue && RosterDocument.HasReceipt(
            (await _store.ReadAsync<JToken>(Path(group, request.ApprovedAlgorithm.Value), ct))?.Value, request.Id);

    public async Task CheckAdditionAsync(InvitationGroup group, GroupJoinRequest request, CancellationToken ct)
    {
        var stored = await _store.ReadAsync<JToken>(Path(group, group.Configuration.SelectedAlgoKey), ct);
        CheckAddition(group, request, RosterDocument.Players(stored?.Value));
    }

    private static void CheckAddition(InvitationGroup group, GroupJoinRequest request, JArray players)
    {
        if (group.MaximumPlayers > 0 && players.Count >= group.MaximumPlayers)
            throw new InvitationException(409, "The group player limit has been reached.");
        if (players.OfType<JObject>().Any(player =>
            string.Equals(player.GetValue("name", StringComparison.OrdinalIgnoreCase)?.Value<string>()?.Trim(),
                request.Name, StringComparison.OrdinalIgnoreCase)))
            throw new InvitationException(409, "A player with this name already exists. Resolve the duplicate before approving.");
    }

    public async Task AddAsync(InvitationGroup group, GroupJoinRequest request, CancellationToken ct)
    {
        var path = Path(group, request.ApprovedAlgorithm!.Value);
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var stored = await _store.ReadAsync<JToken>(path, ct);
            if (RosterDocument.HasReceipt(stored?.Value, request.Id)) return;
            var players = RosterDocument.Players(stored?.Value);
            CheckAddition(group, request, players);
            var playerKey = "join_" + request.Id;
            var player = new JObject
            {
                ["key"] = playerKey, ["id"] = playerKey, ["name"] = request.Name,
                ["isArrived"] = false, ["waitingListOrder"] = null, ["isLocked"] = false,
                ["isGoalKeeper"] = request.IsGoalKeeper, ["modifyTime"] = request.ReviewedAt!.Value.ToString("O"),
                ["description"] = request.Background,
                ["preferredWithKeys"] = new JArray(), ["avoidWithKeys"] = new JArray()
            };
            if (request.ApprovedAlgorithm == (int)AlgoType.Positions) player["positions"] = new JArray(request.Positions);
            if (request.GoalkeeperSkill.HasValue) player["goalkeeperSkill"] = request.GoalkeeperSkill.Value;
            if (request.IsRecoveringFromInjury) player["isRecoveringFromInjury"] = true;
            if (request.FinalRatings!.Count > 0) player["rank"] = request.FinalRatings.Values.Average();
            foreach (var rating in request.FinalRatings) player[rating.Key] = rating.Value;
            players.Add(player);
            if (await _store.WriteAsync(path, RosterDocument.WithReceipt(stored?.Value, players, request.Id), stored?.ETag, ct))
                return;
        }
        throw new InvitationException(409, "The roster changed while approving. Retry the approval.");
    }

    public async Task<object> ContextAsync(InvitationGroup group, IReadOnlyList<SkillDefinition> skills, bool includeRatings, CancellationToken ct)
    {
        var stored = await _store.ReadAsync<JToken>(Path(group, group.Configuration.SelectedAlgoKey), ct);
        var players = RosterDocument.Players(stored?.Value);
        return new
        {
            playerCount = players.Count,
            ratingContextAvailable = includeRatings,
            skills = skills.Select(skill =>
            {
                var values = (includeRatings ? players.OfType<JObject>() : Enumerable.Empty<JObject>())
                    .Select(player => player.GetValue(skill.Id, StringComparison.OrdinalIgnoreCase))
                    .Where(value => value?.Type is JTokenType.Integer or JTokenType.Float)
                    .Select(value => value!.Value<double>())
                    .Where(value => double.IsFinite(value) && value >= 1 && value <= 10).ToArray();
                return new { skillId = skill.Id, ratedPlayers = values.Length,
                    average = values.Length == 0 ? (double?)null : Math.Round(values.Average(), 2) };
            }).ToArray()
        };
    }
}
