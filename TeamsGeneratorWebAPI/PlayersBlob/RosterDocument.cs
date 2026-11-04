using Newtonsoft.Json.Linq;

namespace TeamsGeneratorWebAPI.PlayersBlob;

public static class RosterDocument
{
    public const string MissingRevision = "missing";

    public static JArray Players(JToken? document) => document switch
    {
        null => new JArray(),
        JArray players => players,
        JObject envelope when envelope["players"] is JArray players
            && envelope["groupJoinReceipts"] is JArray => players,
        _ => throw new InvalidDataException("Invalid player roster document.")
    };

    public static bool IsProtected(JToken? document) => document is JObject;

    public static JToken WithPlayers(JToken? document, JArray players)
    {
        if (document is not JObject envelope) return players;
        var updated = (JObject)envelope.DeepClone();
        updated["players"] = players;
        return updated;
    }

    // Membership and its durable receipt share a CAS write, including across approval retries.
    public static JObject WithReceipt(JToken? document, JArray players, string receipt)
    {
        var receipts = document is JObject envelope
            ? (JArray)envelope["groupJoinReceipts"]!.DeepClone() : new JArray();
        receipts.Add(receipt);
        var updated = WithPlayers(document, players) as JObject ?? new JObject { ["players"] = players };
        updated["groupJoinReceipts"] = receipts;
        return updated;
    }

    public static bool HasReceipt(JToken? document, string receipt) =>
        document is JObject envelope
        && envelope["groupJoinReceipts"] is JArray receipts
        && receipts.Values<string>().Contains(receipt, StringComparer.Ordinal);

    public static bool CanSave(JToken? document, string? actualRevision, string? expectedRevision, JArray players)
    {
        if (expectedRevision != null)
            return expectedRevision == (actualRevision ?? MissingRevision);
        if (document is not JObject envelope) return true;

        // An unversioned omission may be an intentional removal or an unseen approval.
        // Do not merge and acknowledge it: old clients ignore players in the save response.
        var currentKeys = KeyCounts(Players(document));
        var incomingKeys = KeyCounts(players);
        return envelope["groupJoinReceipts"]!.Values<string>().All(receipt =>
            currentKeys.GetValueOrDefault("join_" + receipt) == incomingKeys.GetValueOrDefault("join_" + receipt));
    }

    private static Dictionary<string, int> KeyCounts(JArray players) => players.OfType<JObject>()
        .Select(player => player.GetValue("key", StringComparison.OrdinalIgnoreCase)?.ToString())
        .Where(key => key != null)
        .GroupBy(key => key!, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
}
