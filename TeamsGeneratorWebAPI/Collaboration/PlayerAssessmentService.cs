using Azure.Storage.Blobs;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TeamsGenerator.API;

namespace TeamsGeneratorWebAPI.Collaboration;

public interface IPlayerAssessmentService
{
    Task<JArray> ApplyAsync(
        string organizerId,
        string groupReference,
        int algoType,
        JArray sharedPlayers);

    Task SaveAsync(
        string organizerId,
        string groupReference,
        int algoType,
        JArray players,
        bool dedicatedOnly = false);
}

public sealed class PlayerAssessmentService : IPlayerAssessmentService
{
    private readonly BlobContainerClient _container;

    public PlayerAssessmentService(IConfiguration configuration)
    {
        _container = new BlobContainerClient(
            configuration.GetValue<string>("BlobConnectionString"),
            configuration.GetValue<string>("PlayersBlobContainerName"));
    }

    public async Task<JArray> ApplyAsync(
        string organizerId,
        string groupReference,
        int algoType,
        JArray sharedPlayers)
    {
        var assessments = await LoadAsync(
            organizerId,
            groupReference,
            algoType);
        return ApplyAssessments(sharedPlayers, assessments);
    }

    internal static JArray ApplyAssessments(
        JArray sharedPlayers, IReadOnlyDictionary<string, JObject> assessments)
    {
        foreach (var player in sharedPlayers.Children<JObject>())
        {
            var key = GetKey(player);
            if (
                key != null
                && assessments.TryGetValue(key, out var assessment))
            {
                foreach (var property in assessment.Properties())
                {
                    var existing = player.Properties().FirstOrDefault(candidate =>
                        string.Equals(candidate.Name, property.Name, StringComparison.OrdinalIgnoreCase));
                    player[existing?.Name ?? property.Name] =
                        PrivatePlayerFields.NormalizeAssessment(property.Name, property.Value);
                }
            }
        }
        return sharedPlayers;
    }

    public async Task SaveAsync(
        string organizerId,
        string groupReference,
        int algoType,
        JArray players,
        bool dedicatedOnly = false)
    {
        var previous = await LoadAsync(organizerId, groupReference, algoType);
        var assessments = ExtractAssessments(players, previous, dedicatedOnly);
        await _container.GetBlobClient(
                GetPath(organizerId, groupReference, algoType))
            .UploadAsync(
                BinaryData.FromString(
                    JsonConvert.SerializeObject(assessments)),
                overwrite: true);
    }

    internal static Dictionary<string, JObject> ExtractAssessments(
        JArray players, IReadOnlyDictionary<string, JObject> previous, bool dedicatedOnly)
    {
        var assessments = new Dictionary<string, JObject>(
            StringComparer.Ordinal);
        foreach (var player in players.Children<JObject>())
        {
            var key = GetKey(player);
            if (key == null) continue;
            var assessment = new JObject();
            if (previous.TryGetValue(key, out var prior))
            {
                foreach (var property in prior.Properties().Where(property =>
                    PrivatePlayerFields.IsDedicatedAssessment(property.Name)))
                    assessment[property.Name] = PrivatePlayerFields.NormalizeAssessment(property.Name, property.Value);
            }
            foreach (var property in player.Properties().Where(property =>
                !SharedProperties.Contains(property.Name)
                && (!dedicatedOnly || PrivatePlayerFields.IsDedicatedAssessment(property.Name))))
            {
                var existing = assessment.Properties().FirstOrDefault(candidate =>
                    string.Equals(candidate.Name, property.Name, StringComparison.OrdinalIgnoreCase));
                assessment[existing?.Name ?? property.Name] =
                    PrivatePlayerFields.NormalizeAssessment(property.Name, property.Value);
            }
            if (assessment.HasValues)
            {
                assessments[key] = assessment;
            }
        }

        return assessments;
    }

    private async Task<Dictionary<string, JObject>> LoadAsync(
        string organizerId,
        string groupReference,
        int algoType)
    {
        var client = _container.GetBlobClient(
            GetPath(organizerId, groupReference, algoType));
        if (!await client.ExistsAsync())
        {
            return new Dictionary<string, JObject>(StringComparer.Ordinal);
        }
        var content = await client.DownloadContentAsync();
        return JsonConvert.DeserializeObject<Dictionary<string, JObject>>(
            content.Value.Content.ToString())
            ?? new Dictionary<string, JObject>(StringComparer.Ordinal);
    }

    private static string GetPath(
        string organizerId,
        string groupReference,
        int algoType)
    {
        var safeGroupReference = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(groupReference ?? "default")));
        return $"{organizerId}_private/player-assessments/{safeGroupReference}_{algoType}.json";
    }

    private static string GetKey(JObject player) =>
        player.Properties().FirstOrDefault(property =>
            string.Equals(
                property.Name,
                "key",
                StringComparison.OrdinalIgnoreCase))
            ?.Value?.ToString();

    private static readonly HashSet<string> SharedProperties =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "key",
            "id",
            "name",
            "photo",
            "isArrived",
            "isWaiting",
            "waitingListOrder",
            "modifyTime",
            "isLocked",
            "isGoalKeeper"
        };
}
