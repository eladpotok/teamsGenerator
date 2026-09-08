using Azure.Storage.Blobs;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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
        JArray players);
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
        foreach (var player in sharedPlayers.Children<JObject>())
        {
            var key = GetKey(player);
            if (
                key != null
                && assessments.TryGetValue(key, out var assessment))
            {
                player.Merge(
                    assessment,
                    new JsonMergeSettings
                    {
                        MergeArrayHandling = MergeArrayHandling.Replace
                    });
            }
        }
        return sharedPlayers;
    }

    public async Task SaveAsync(
        string organizerId,
        string groupReference,
        int algoType,
        JArray players)
    {
        var assessments = new Dictionary<string, JObject>(
            StringComparer.Ordinal);
        foreach (var player in players.Children<JObject>())
        {
            var key = GetKey(player);
            if (key == null) continue;
            var assessment = new JObject();
            foreach (var property in player.Properties().Where(property =>
                !SharedProperties.Contains(property.Name)))
            {
                assessment[property.Name] = property.Value.DeepClone();
            }
            if (assessment.HasValues)
            {
                assessments[key] = assessment;
            }
        }

        await _container.GetBlobClient(
                GetPath(organizerId, groupReference, algoType))
            .UploadAsync(
                BinaryData.FromString(
                    JsonConvert.SerializeObject(assessments)),
                overwrite: true);
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
