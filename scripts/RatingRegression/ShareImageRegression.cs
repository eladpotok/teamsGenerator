using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using SkiaSharp;
using TeamsGeneratorWebAPI.Controllers;
using TeamsGeneratorWebAPI.Telemetry;

internal static class ShareImageRegression
{
    public static async Task Run(Action<bool, string> check)
    {
        foreach (var culture in new[] { "en-US", "he-IL" })
        foreach (var legacyStrings in new[] { false, true })
        {
            var names = culture == "he-IL"
                ? new[] { "\u05d3\u05e0\u05d9\u05d0\u05dc \u05db\u05d4\u05df", "\u05e0\u05d5\u05e2\u05dd \u05dc\u05d5\u05d9", "\u05d0\u05d5\u05e8\u05d9 \u05d9\u05e9\u05e8\u05d0\u05dc\u05d9" }
                : new[] { "Synthetic Alex", "Synthetic Ben", "Synthetic Chris" };
            var label = $"{culture}, legacy={legacyStrings}";
            using var image = await Render(names, culture, legacyStrings, false, check);
            using var differentNames = await Render(names.Select(name => name + " MMM").ToArray(),
                culture, legacyStrings, false, check);
            using var differentMetadata = await Render(names, culture, legacyStrings, false, check, true);
            check(image.Width == 1080 && image.Height == 1350, $"Classic canvas dimensions: {label}");
            for (var index = 0; index < 3; index++)
            {
                var logicalColumn = index / 2;
                var visualColumn = culture == "he-IL" ? 1 - logicalColumn : logicalColumn;
                var left = 70 + visualColumn * 480;
                var top = 430 + index % 2 * 80;
                var nameRect = new SKRectI(left + (culture == "he-IL" ? 18 : 70), top + 12,
                    left + (culture == "he-IL" ? 390 : 442), top + 60);
                check(Count(image, nameRect, color => color.Red > 180 && color.Green > 180 && color.Blue > 180) > 100,
                    $"Classic player {index + 1} name is visible inside its card: {label}");
                check(Differences(image, differentNames, nameRect) > 100,
                    $"Classic player {index + 1} card contains the supplied name: {label}");
            }
            foreach (var field in new[]
            {
                ("team name", new SKRectI(70, 140, 1010, 230)),
                ("date", new SKRectI(94, 275, 507, 325)),
                ("location", new SKRectI(573, 275, 986, 325))
            })
            {
                check(Count(image, field.Item2,
                    color => color.Red > 180 && color.Green > 180 && color.Blue > 180) > 100,
                    $"Classic {field.Item1} is visible in its intended region: {label}");
                check(Differences(image, differentMetadata, field.Item2) > 100,
                    $"Classic {field.Item1} contains the supplied metadata, not static fallback content: {label}");
            }
        }

        foreach (var culture in new[] { "en-US", "he-IL" })
        {
            using var image = await Render(new[] { "Ready Alex", "Ready Ben", "Waiting Chris" },
                culture, false, true, check);
            var left = culture == "he-IL" ? 70 : 550;
            check(Count(image, new SKRectI(left + 18, 442, left + 442, 490),
                color => color.Red > 200 && color.Green > 140 && color.Blue < 150) > 100,
                $"Classic next-waiting name is visible: {culture}");
        }

        await RunScoreboards(check);
    }

    private static async Task RunScoreboards(Action<bool, string> check)
    {
        foreach (var culture in new[] { "en-US", "he-IL" })
        {
            var request = ScoreboardRequest(culture);
            var changedTable = (JObject)request.DeepClone();
            foreach (var score in changedTable["stats"]!["stats"]!.Children<JProperty>())
            {
                var values = (JObject)score.Value;
                values["W"] = values.Value<int>("W") + 3;
                values["D"] = values.Value<int>("D") + 2;
                values["L"] = values.Value<int>("L") + 4;
                values["Gf"] = values.Value<int>("Gf") + 8;
                values["Ga"] = values.Value<int>("Ga") + 1;
                // GP and Points are computed by Score; keep the frontend payload consistent.
                values["GP"] = values.Value<int>("W") + values.Value<int>("D") + values.Value<int>("L");
                values["Points"] = values.Value<int>("W") * 3 + values.Value<int>("D");
            }
            var changedNames = (JObject)request.DeepClone();
            var changedTotals = (JObject)request.DeepClone();
            foreach (var category in new[] { "topScorers", "topAssists" })
            {
                foreach (var player in changedNames["stats"]!["topPlayers"]![category]!.Children<JObject>())
                    player["name"] = culture == "he-IL" ? "\u05ea\u05de\u05d9\u05e8 " + player.Value<string>("name") : "Morgan " + player.Value<string>("name");
                foreach (var player in changedTotals["stats"]!["topPlayers"]![category]!.Children<JObject>())
                    player["scores"] = player.Value<int>("scores") + 7;
            }

            using var image = await RenderScoreboard(request, check);
            using var differentTable = await RenderScoreboard(changedTable, check);
            using var differentNames = await RenderScoreboard(changedNames, check);
            using var differentTotals = await RenderScoreboard(changedTotals, check);
            check(image.Width == 1080 && image.Height == 1350, $"Classic scoreboard canvas dimensions: {culture}");

            // These interior regions exclude borders, shirts, neighboring cells and metadata.
            // Numeric changes preserve ranking, row count and colors, so layout cannot satisfy a diff.
            var rtl = culture == "he-IL";
            var centers = rtl ? new[] { 105, 195, 285, 375, 465, 555 } : new[] { 525, 615, 705, 795, 885, 965 };
            var columns = new[] { "P", "W", "D", "L", "GD", "PTS" };
            for (var column = 0; column < columns.Length; column++)
            {
                var x = centers[column];
                var header = new SKRectI(x - 27, 447, x + 27, 472);
                check(Count(image, header, IsScoreboardText) > 20,
                    $"Classic scoreboard {columns[column]} header is visible: {culture}");
                for (var row = 0; row < 3; row++)
                {
                    var top = 490 + row * 59;
                    var cell = new SKRectI(x - 27, top + 12, x + 27, top + 40);
                    CheckScoreboardText(image, differentTable, cell, check,
                        $"table row {row + 1} {columns[column]}: {culture}");
                    check(Differences(image, differentNames, cell) == 0 && Differences(image, differentTotals, cell) == 0,
                        $"Classic scoreboard table row {row + 1} {columns[column]} is independent of leader edits: {culture}");
                }
            }
            for (var row = 0; row < 3; row++)
            {
                var x = rtl ? 968 : 112;
                var top = 490 + row * 59;
                var rank = new SKRectI(x - 9, top + 16, x + 9, top + 34);
                check(Count(image, rank, color => color.Red < 35 && color.Green < 60 && color.Blue < 80) > 10,
                    $"Classic scoreboard rank {row + 1} glyph is visible inside its circle: {culture}");
                check(Differences(image, differentTable, rank) == 0,
                    $"Classic scoreboard numeric variant preserves rank {row + 1}: {culture}");
            }
            foreach (var category in new[] { "topScorers", "topAssists" })
            {
                var left = (category == "topScorers") == rtl ? 550 : 70;
                for (var row = 0; row < 3; row++)
                {
                    var top = 930 + row * 57;
                    var name = new SKRectI(left + (rtl ? 80 : 74), top + 8,
                        left + (rtl ? 386 : 380), top + 40);
                    var scoreX = left + (rtl ? 46 : 414);
                    var total = new SKRectI(scoreX - 25, top + 10, scoreX + 25, top + 40);
                    CheckScoreboardText(image, differentNames, name, check, $"{category} row {row + 1} supplied name: {culture}");
                    CheckScoreboardText(image, differentTotals, total, check, $"{category} row {row + 1} supplied total: {culture}");
                    check(Differences(image, differentTotals, name) == 0 && Differences(image, differentTable, name) == 0,
                        $"Classic scoreboard {category} row {row + 1} name is independent of numeric edits: {culture}");
                    check(Differences(image, differentNames, total) == 0 && Differences(image, differentTable, total) == 0,
                        $"Classic scoreboard {category} row {row + 1} total is independent of names and table: {culture}");
                }
            }
            var metadata = new SKRectI(70, 130, 1010, 340);
            check(Differences(image, differentTable, metadata) == 0
                && Differences(image, differentNames, metadata) == 0
                && Differences(image, differentTotals, metadata) == 0,
                $"Classic scoreboard variants preserve metadata: {culture}");
        }
    }

    private static bool IsScoreboardText(SKColor color) =>
        color.Green > 170 && color.Blue > 130 && color.Red > 100;

    private static void CheckScoreboardText(SKBitmap image, SKBitmap variant, SKRectI rect,
        Action<bool, string> check, string label)
    {
        var visible = Count(image, rect, IsScoreboardText);
        var changed = Differences(image, variant, rect);
        check(visible > 35, $"Classic scoreboard {label} has visible text ({visible} pixels)");
        check(changed > 35, $"Classic scoreboard {label} reflects its request field ({changed} changed pixels)");
    }

    private static JObject ScoreboardRequest(string culture)
    {
        var colors = new[] { "red", "blue", "green" };
        var table = new JObject();
        for (var index = 0; index < colors.Length; index++)
        {
            var wins = 8 - index * 2;
            var draws = 2 + index;
            var losses = 1 + index;
            table[colors[index]] = new JObject
            {
                ["GP"] = wins + draws + losses, ["W"] = wins, ["D"] = draws, ["L"] = losses,
                ["Gf"] = 23 - index * 5, ["Ga"] = 5 + index, ["Points"] = wins * 3 + draws
            };
        }
        var names = culture == "he-IL"
            ? new[] { "\u05d3\u05e0\u05d9\u05d0\u05dc \u05db\u05d4\u05df", "\u05e0\u05d5\u05e2\u05dd \u05dc\u05d5\u05d9", "\u05d0\u05d5\u05e8\u05d9 \u05d9\u05e9\u05e8\u05d0\u05dc\u05d9",
                "\u05d0\u05d9\u05ea\u05d9 \u05d3\u05d5\u05d3", "\u05e8\u05d5\u05df \u05d0\u05d5\u05e8\u05df", "\u05d2\u05d9\u05d0 \u05d2\u05d5\u05dc\u05df" }
            : new[] { "Alex River", "Ben Stone", "Chris Lake", "Dana Field", "Eli Hill", "Fran Brook" };
        JArray Leaders(int offset) => new JArray(Enumerable.Range(0, 3).Select(index => new JObject
        {
            ["name"] = names[offset + index], ["scores"] = 12 - offset - index * 3, ["teamColor"] = colors[index]
        }));
        return new JObject
        {
            ["stats"] = new JObject
            {
                ["stats"] = table,
                ["topPlayers"] = new JObject { ["topScorers"] = Leaders(0), ["topAssists"] = Leaders(3) },
                ["scorers"] = Leaders(0)
            },
            ["teamInfo"] = new JObject
            {
                ["teamName"] = "Synthetic match", ["location"] = "Test pitch",
                ["date"] = "2026-09-27 18:00", ["currentCulture"] = culture
            }
        };
    }

    private static async Task<SKBitmap> RenderScoreboard(JObject request, Action<bool, string> check)
    {
        var telemetry = new Telemetry();
        var controller = new TeamsController(NullLogger<TeamsController>.Instance,
            telemetry, null!, null!, null!, null!, null!);
        var result = await controller.GetNormalizedScoresDesign("20.11", JObject.Parse(request.ToString()));
        check(result is FileContentResult { ContentType: "image/png" }, "Classic scoreboard returns a PNG");
        check(telemetry.GraphicType == "scoreboard_normalized" && telemetry.ImageBytes > 0,
            "Classic scoreboard controller records successful image generation without attendance measurements");
        return SKBitmap.Decode(((FileContentResult)result).FileContents)
            ?? throw new InvalidOperationException("Classic scoreboard returned an invalid PNG.");
    }

    private static async Task<SKBitmap> Render(string[] names, string culture, bool legacyStrings,
        bool waiting, Action<bool, string> check, bool differentMetadata = false)
    {
        var players = new JArray(names.Select((name, index) => legacyStrings
            ? (JToken)new JValue(name)
            : new JObject
            {
                ["name"] = name,
                ["isWaiting"] = waiting && index == 2,
                ["waitingListOrder"] = waiting && index == 2 ? new JValue(0) : JValue.CreateNull()
            }));
        // Exercise the actual controller JSON contract, not only the renderer's typed input.
        var request = JObject.Parse(new JObject
        {
            ["players"] = players,
            ["teamInfo"] = new JObject
            {
                ["teamName"] = differentMetadata ? "Away game" : "Synthetic match",
                ["location"] = culture == "he-IL"
                    ? differentMetadata ? "\u05d4\u05d0\u05e6\u05d8\u05d3\u05d9\u05d5\u05df" : "\u05d4\u05de\u05d2\u05e8\u05e9"
                    : differentMetadata ? "North stadium" : "Test pitch",
                ["date"] = differentMetadata ? "2026-12-04 20:30" : "2026-09-27 18:00",
                ["currentCulture"] = culture
            }
        }.ToString());
        var telemetry = new Telemetry();
        var controller = new UserPlayersController(NullLogger<UserPlayersController>.Instance,
            null!, telemetry, null!, null!, null!, null!);
        var result = await controller.PostSharePlayers("20.11", request, "synthetic-image-regression");
        check(result is FileContentResult { ContentType: "image/png" }, "Classic returns a PNG");
        check(telemetry.PlayerCount == 3 && telemetry.WaitingCount == (waiting ? 1 : 0),
            "Classic parses all three names and preserves attendance");
        return SKBitmap.Decode(((FileContentResult)result).FileContents)
            ?? throw new InvalidOperationException("Classic returned an invalid PNG.");
    }

    private static int Count(SKBitmap image, SKRectI rect, Func<SKColor, bool> predicate)
    {
        var pixels = image.Pixels;
        var width = image.Width;
        var count = 0;
        for (var y = rect.Top; y < rect.Bottom; y++)
        for (var x = rect.Left; x < rect.Right; x++)
            if (predicate(pixels[y * width + x])) count++;
        return count;
    }

    private static int Differences(SKBitmap first, SKBitmap second, SKRectI rect)
    {
        var firstPixels = first.Pixels;
        var secondPixels = second.Pixels;
        var width = first.Width;
        var count = 0;
        for (var y = rect.Top; y < rect.Bottom; y++)
        for (var x = rect.Left; x < rect.Right; x++)
            if (firstPixels[y * width + x] != secondPixels[y * width + x]) count++;
        return count;
    }

    private sealed class Telemetry : IUsageTelemetry
    {
        public double? PlayerCount { get; private set; }
        public double? WaitingCount { get; private set; }
        public double? ImageBytes { get; private set; }
        public string? GraphicType { get; private set; }
        public void Track(string eventName, string? clientVersion = null, string? userId = null,
            IReadOnlyDictionary<string, string?>? properties = null,
            IReadOnlyDictionary<string, double>? measurements = null)
        {
            PlayerCount = measurements != null && measurements.TryGetValue("player_count", out var players) ? players : null;
            WaitingCount = measurements != null && measurements.TryGetValue("waiting_count", out var waiting) ? waiting : null;
            ImageBytes = measurements != null && measurements.TryGetValue("image_bytes", out var bytes) ? bytes : null;
            GraphicType = properties != null && properties.TryGetValue("graphic_type", out var type) ? type : null;
        }
    }
}
