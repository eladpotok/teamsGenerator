using SkiaSharp;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TeamsGenerator.Utilities;
using TeamsGeneratorWebAPI.DesignCreator;

namespace TeamsDesignCreator
{
    public static class SkiaImageCreator
    {
        public static Point TopLeft = new Point(91, 116);
        public static Point TopMiddle = new Point(273, 116);
        public static Point TopRight = new Point(455, 116);

        public static Point MiddleMostLeft = new Point(51, 281);
        public static Point MiddleLeft = new Point(172, 358);
        public static Point Center = new Point(274, 281);
        public static Point MiddleRight = new Point(378, 358);
        public static Point MiddleMostRight = new Point(512, 281);

        public static Point BottomLeft = new Point(91, 472);
        public static Point BottomMiddle = new Point(273, 427);
        public static Point BottomRight = new Point(455, 472);


        public static readonly Dictionary<int, List<Point>> locations = new Dictionary<int, List<Point>>() 
        {
           { 1, new List<Point>() {  Center } },
           { 2, new List<Point>() { MiddleLeft, MiddleRight } },
           { 3, new List<Point>() { MiddleLeft, MiddleRight, TopMiddle } },
           { 4, new List<Point>() { TopLeft, TopRight, BottomLeft, BottomRight } },
           { 5, new List<Point>() { TopLeft, TopMiddle, TopRight, MiddleLeft, MiddleRight  } },
           { 6, new List<Point>() { TopLeft, TopRight, TopMiddle, MiddleMostLeft, MiddleMostRight, Center} },
           { 7, new List<Point>() { BottomLeft, BottomMiddle, BottomRight, MiddleLeft, MiddleRight, Center, TopMiddle } },
           { 8, new List<Point>() { TopLeft, TopRight, BottomLeft, BottomRight, TopMiddle, BottomMiddle, MiddleMostLeft, MiddleMostRight } },
           { 9, new List<Point>() { TopLeft, TopRight, BottomLeft, BottomRight, TopMiddle, BottomMiddle, MiddleMostLeft, MiddleMostRight, Center } },
           { 10, new List<Point>() { TopLeft, TopRight, BottomLeft, BottomRight, TopMiddle, BottomMiddle, MiddleMostLeft, MiddleMostRight, MiddleLeft, MiddleRight } },
           { 11, new List<Point>() { TopLeft, TopRight, BottomLeft, BottomRight, TopMiddle, BottomMiddle, MiddleMostLeft, MiddleMostRight, MiddleLeft, MiddleRight, Center } },

        };

        internal static MemoryStream GenerateTable(dynamic stats, dynamic topScorers, string ver)
        {
            var orderedTable = TableCalculator.Create(stats);
            return DrawTable(orderedTable, topScorers, ver);
        }

        internal static MemoryStream GenerateNormalizedTable(dynamic stats, string ver)
        {
            Dictionary<string, Score> scores = JsonSerializer.Deserialize<Dictionary<string, Score>>(stats.stats.stats.ToString(), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (double.TryParse(ver, NumberStyles.Any, CultureInfo.InvariantCulture, out var version) && version >= 20.1)
            {
                return DrawBrandedScoreboard(scores, stats);
            }

            return DrawTable(scores, stats, ver);
        }


        public static MemoryStream DrawTable(Dictionary<string, Score> table, dynamic stats, string ver)
        {
            if (!string.IsNullOrEmpty(ver) && double.TryParse(ver, out double verNum) && verNum >= 19.0)
            {
                return DrawScoreTableWithAssist(table, stats);
            }
            else
            {
                return DrawScoreTableLegacy(table, stats);
            }
        }

        private static MemoryStream DrawBrandedScoreboard(Dictionary<string, Score> table, dynamic stats)
        {
            var request = JObject.FromObject(stats);
            var finalMatchday = request["stats"] as JObject ?? new JObject();
            var teamInfo = request["teamInfo"] as JObject ?? new JObject();
            var topPlayers = finalMatchday["topPlayers"] as JObject ?? new JObject();
            var topScorers = ReadScoreboardLeaders(topPlayers["topScorers"]);
            var topAssists = ReadScoreboardLeaders(topPlayers["topAssists"]);
            var teamName = GetJsonString(teamInfo, "teamName", "matchName");
            var location = GetJsonString(teamInfo, "location", "venue");
            var date = GetJsonString(teamInfo, "date", "eventDate");
            var dayInWeek = GetJsonString(teamInfo, "dayInWeek");
            var currentCulture = GetJsonString(teamInfo, "currentCulture", "culture");
            var culture = GetCulture(string.IsNullOrWhiteSpace(currentCulture) ? "en-US" : currentCulture);
            var content = string.Join(
                " ",
                table.Keys
                    .Concat(topScorers.Select(player => player.Name))
                    .Concat(topAssists.Select(player => player.Name))
                    .Prepend(teamName)
                    .Prepend(location));
            var hasHebrewContent = content.Any(character => character >= '\u0590' && character <= '\u05FF');
            var hasArabicContent = content.Any(character => character >= '\u0600' && character <= '\u08FF');
            var isRtlLayout = culture.TextInfo.IsRightToLeft || hasHebrewContent || hasArabicContent;
            if (hasHebrewContent && !culture.TextInfo.IsRightToLeft)
            {
                culture = GetCulture("he-IL");
            }
            else if (hasArabicContent && !culture.TextInfo.IsRightToLeft)
            {
                culture = GetCulture("ar");
            }

            var matchDate = ParseMatchDate(date);
            var title = string.IsNullOrWhiteSpace(teamName)
                ? GetLocalizedLabel(culture, "MATCHDAY RESULTS", "תוצאות הערב", "نتائج المباراة")
                : teamName;
            var resultsLabel = GetLocalizedLabel(culture, "MATCHDAY RESULTS", "תוצאות הערב", "نتائج المباراة");
            var standingsLabel = GetLocalizedLabel(culture, "STANDINGS", "טבלת הערב", "الترتيب");
            var scorersLabel = GetLocalizedLabel(culture, "TOP SCORERS", "מלכי השערים", "الهدافون");
            var assistsLabel = GetLocalizedLabel(culture, "TOP ASSISTS", "מלכי הבישולים", "أفضل الممررين");
            var locationLabel = GetLocalizedLabel(culture, "VENUE", "מיקום", "المكان");
            var displayLocation = string.IsNullOrWhiteSpace(location)
                ? GetLocalizedLabel(culture, "Location to be confirmed", "המיקום יעודכן", "سيتم تحديد المكان")
                : location;
            var displayDate = matchDate.HasValue ? FormatRosterDate(matchDate.Value, culture) : dayInWeek;

            using (var templateStream = System.IO.File.OpenRead(@"templates/playersListTemplate3.png"))
            using (var templateBitmap = SKBitmap.Decode(templateStream))
            using (var surface = SKSurface.Create(new SKImageInfo(templateBitmap.Width, templateBitmap.Height)))
            using (var accentPaint = new SKPaint { Color = SKColor.Parse("#50F3AA"), IsAntialias = true })
            using (var subtlePaint = new SKPaint { Color = new SKColor(255, 255, 255, 20), IsAntialias = true })
            using (var borderPaint = new SKPaint { Color = new SKColor(133, 255, 203, 46), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2 })
            using (var titlePaint = CreateRosterTextPaint(SKColors.White, 70, true))
            using (var labelPaint = CreateRosterTextPaint(SKColor.Parse("#88F8BF"), 25, true))
            using (var metadataPaint = CreateRosterTextPaint(SKColor.Parse("#D8F5E8"), 28, false))
            using (var headerPaint = CreateRosterTextPaint(new SKColor(215, 245, 231, 205), 20, true))
            using (var teamPaint = CreateRosterTextPaint(SKColors.White, 27, true))
            using (var statPaint = CreateRosterTextPaint(SKColors.White, 25, true))
            using (var footerPaint = CreateRosterTextPaint(new SKColor(218, 245, 233, 185), 20, false))
            {
                var canvas = surface.Canvas;
                canvas.DrawBitmap(templateBitmap, SKPoint.Empty);

                DrawRosterPill(canvas, resultsLabel, 70, 72, labelPaint, accentPaint, isRtlLayout);
                DrawFittedRosterText(canvas, title, new SKRect(70, 130, 1010, 230), 205, titlePaint, SKTextAlign.Center, 42);

                var metadataGap = 18f;
                var metadataWidth = (940f - metadataGap) / 2f;
                var firstMetadataRect = new SKRect(70, 260, 70 + metadataWidth, 340);
                var secondMetadataRect = new SKRect(firstMetadataRect.Right + metadataGap, 260, 1010, 340);
                DrawMetadataCard(canvas, firstMetadataRect, displayDate, subtlePaint, borderPaint, metadataPaint, isRtlLayout);
                DrawMetadataCard(canvas, secondMetadataRect, $"{locationLabel}  ·  {displayLocation}", subtlePaint, borderPaint, metadataPaint, isRtlLayout);

                DrawFittedRosterText(
                    canvas,
                    standingsLabel,
                    new SKRect(70, 360, 1010, 410),
                    397,
                    labelPaint,
                    isRtlLayout ? SKTextAlign.Right : SKTextAlign.Left,
                    20);

                DrawStandingsCard(
                    canvas,
                    table,
                    culture,
                    isRtlLayout,
                    subtlePaint,
                    borderPaint,
                    headerPaint,
                    teamPaint,
                    statPaint,
                    accentPaint);

                var firstLeadersRect = new SKRect(70, 850, 530, 1238);
                var secondLeadersRect = new SKRect(550, 850, 1010, 1238);
                DrawLeadersCard(
                    canvas,
                    isRtlLayout ? secondLeadersRect : firstLeadersRect,
                    scorersLabel,
                    topScorers,
                    culture,
                    isRtlLayout,
                    subtlePaint,
                    borderPaint,
                    labelPaint,
                    teamPaint,
                    statPaint);
                DrawLeadersCard(
                    canvas,
                    isRtlLayout ? firstLeadersRect : secondLeadersRect,
                    assistsLabel,
                    topAssists,
                    culture,
                    isRtlLayout,
                    subtlePaint,
                    borderPaint,
                    labelPaint,
                    teamPaint,
                    statPaint);

                var rights = $"TEAMIFY  ·  © {DateTime.UtcNow.Year} ALL RIGHTS RESERVED";
                DrawFittedRosterText(canvas, rights, new SKRect(70, 1268, 1010, 1296), 1290, footerPaint, SKTextAlign.Center, 15);

                using (var image = surface.Snapshot())
                using (var png = image.Encode(SKEncodedImageFormat.Png, 100))
                using (var ms = new MemoryStream())
                {
                    png.SaveTo(ms);
                    return ms;
                }
            }
        }

        private static void DrawStandingsCard(
            SKCanvas canvas,
            IReadOnlyDictionary<string, Score> table,
            CultureInfo culture,
            bool isRtlLayout,
            SKPaint cardPaint,
            SKPaint borderPaint,
            SKPaint headerPaint,
            SKPaint teamPaint,
            SKPaint statPaint,
            SKPaint accentPaint)
        {
            var cardRect = new SKRect(70, 425, 1010, 825);
            canvas.DrawRoundRect(cardRect, 22, 22, cardPaint);
            canvas.DrawRoundRect(cardRect, 22, 22, borderPaint);

            var numericCenters = isRtlLayout
                ? new[] { 105f, 195f, 285f, 375f, 465f, 555f }
                : new[] { 525f, 615f, 705f, 795f, 885f, 965f };
            var headers = new[] { "P", "W", "D", "L", "GD", "PTS" };
            for (var index = 0; index < headers.Length; index++)
            {
                headerPaint.TextAlign = SKTextAlign.Center;
                canvas.DrawText(headers[index], numericCenters[index], 468, headerPaint);
            }

            var entries = table
                .OrderByDescending(entry => entry.Value.Points)
                .ThenByDescending(entry => entry.Value.Gf - entry.Value.Ga)
                .ThenByDescending(entry => entry.Value.Gf)
                .Take(6)
                .ToList();
            var rowHeight = Math.Min(59f, 330f / Math.Max(1, entries.Count));
            for (var index = 0; index < entries.Count; index++)
            {
                var entry = entries[index];
                var rowTop = 490 + index * rowHeight;
                var rowRect = new SKRect(84, rowTop, 996, rowTop + rowHeight - 7);
                using (var rowPaint = new SKPaint
                {
                    Color = index == 0
                        ? new SKColor(80, 243, 170, 24)
                        : new SKColor(255, 255, 255, 12),
                    IsAntialias = true
                })
                using (var rankPaint = CreateRosterTextPaint(SKColor.Parse("#071A2E"), 18, true))
                {
                    canvas.DrawRoundRect(rowRect, 13, 13, rowPaint);
                    var teamColor = GetTeamAccentColor(entry.Key);
                    var rankX = isRtlLayout ? 968 : 112;
                    var jerseyX = isRtlLayout ? 925 : 155;
                    var nameRect = isRtlLayout
                        ? new SKRect(610, rowRect.Top, 892, rowRect.Bottom)
                        : new SKRect(188, rowRect.Top, 470, rowRect.Bottom);
                    using (var rankCirclePaint = new SKPaint { Color = index == 0 ? SKColor.Parse("#50F3AA") : new SKColor(218, 245, 233, 210), IsAntialias = true })
                    {
                        canvas.DrawCircle(rankX, rowRect.MidY, 18, rankCirclePaint);
                    }
                    rankPaint.TextAlign = SKTextAlign.Center;
                    canvas.DrawText((index + 1).ToString(CultureInfo.InvariantCulture), rankX, rowRect.MidY + 6, rankPaint);
                    DrawJerseyIcon(canvas, jerseyX, rowRect.MidY, teamColor, 0.55f);
                    DrawFittedRosterText(
                        canvas,
                        GetLocalizedTeamName(new TeamShareItem { Color = entry.Key }, culture, index),
                        nameRect,
                        rowRect.MidY + 9,
                        teamPaint,
                        isRtlLayout ? SKTextAlign.Right : SKTextAlign.Left,
                        17);
                }

                var values = new[]
                {
                    entry.Value.GP,
                    entry.Value.W,
                    entry.Value.D,
                    entry.Value.L,
                    entry.Value.Gf - entry.Value.Ga,
                    entry.Value.Points
                };
                for (var valueIndex = 0; valueIndex < values.Length; valueIndex++)
                {
                    statPaint.Color = valueIndex == values.Length - 1 ? SKColor.Parse("#7AFCB7") : SKColors.White;
                    statPaint.TextAlign = SKTextAlign.Center;
                    canvas.DrawText(values[valueIndex].ToString(CultureInfo.InvariantCulture), numericCenters[valueIndex], rowRect.MidY + 9, statPaint);
                }
            }
        }

        private static void DrawLeadersCard(
            SKCanvas canvas,
            SKRect cardRect,
            string title,
            IReadOnlyList<ScoreboardLeader> leaders,
            CultureInfo culture,
            bool isRtlLayout,
            SKPaint cardPaint,
            SKPaint borderPaint,
            SKPaint titlePaint,
            SKPaint namePaint,
            SKPaint scorePaint)
        {
            canvas.DrawRoundRect(cardRect, 20, 20, cardPaint);
            canvas.DrawRoundRect(cardRect, 20, 20, borderPaint);
            DrawFittedRosterText(canvas, title, new SKRect(cardRect.Left + 24, cardRect.Top + 18, cardRect.Right - 24, cardRect.Top + 64), cardRect.Top + 49, titlePaint, SKTextAlign.Center, 18);

            if (leaders.Count == 0)
            {
                var emptyText = GetLocalizedLabel(culture, "No entries yet", "עדיין אין נתונים", "لا توجد بيانات بعد");
                DrawFittedRosterText(canvas, emptyText, new SKRect(cardRect.Left + 30, cardRect.Top + 130, cardRect.Right - 30, cardRect.Bottom - 30), cardRect.MidY, namePaint, SKTextAlign.Center, 16);
                return;
            }

            for (var index = 0; index < Math.Min(5, leaders.Count); index++)
            {
                var leader = leaders[index];
                var rowTop = cardRect.Top + 80 + index * 57;
                var rowRect = new SKRect(cardRect.Left + 16, rowTop, cardRect.Right - 16, rowTop + 49);
                using (var rowPaint = new SKPaint
                {
                    Color = index == 0 ? new SKColor(80, 243, 170, 24) : new SKColor(255, 255, 255, 12),
                    IsAntialias = true
                })
                {
                    canvas.DrawRoundRect(rowRect, 12, 12, rowPaint);
                }

                var jerseyX = isRtlLayout ? rowRect.Right - 30 : rowRect.Left + 30;
                var scoreX = isRtlLayout ? rowRect.Left + 30 : rowRect.Right - 30;
                var nameRect = isRtlLayout
                    ? new SKRect(rowRect.Left + 64, rowRect.Top, rowRect.Right - 58, rowRect.Bottom)
                    : new SKRect(rowRect.Left + 58, rowRect.Top, rowRect.Right - 64, rowRect.Bottom);
                DrawJerseyIcon(canvas, jerseyX, rowRect.MidY, GetTeamAccentColor(leader.TeamColor), 0.48f);
                DrawFittedRosterText(
                    canvas,
                    leader.Name,
                    nameRect,
                    rowRect.MidY + 8,
                    namePaint,
                    isRtlLayout ? SKTextAlign.Right : SKTextAlign.Left,
                    15);
                scorePaint.Color = index == 0 ? SKColor.Parse("#7AFCB7") : SKColors.White;
                scorePaint.TextAlign = SKTextAlign.Center;
                canvas.DrawText(leader.Scores.ToString(CultureInfo.InvariantCulture), scoreX, rowRect.MidY + 9, scorePaint);
            }
        }

        private static List<ScoreboardLeader> ReadScoreboardLeaders(JToken token)
        {
            return token?
                .Children<JObject>()
                .Select(item => new ScoreboardLeader
                {
                    Name = GetJsonString(item, "name"),
                    TeamColor = GetJsonString(item, "teamColor", "color"),
                    Scores = int.TryParse(GetJsonString(item, "scores", "score"), out var score) ? score : 0
                })
                .Where(item => !string.IsNullOrWhiteSpace(item.Name))
                .ToList() ?? new List<ScoreboardLeader>();
        }

        private static string GetJsonString(JObject source, params string[] names)
        {
            foreach (var name in names)
            {
                var property = source.Properties()
                    .FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
                var value = property?.Value?.ToString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return string.Empty;
        }

        private sealed class ScoreboardLeader
        {
            public string Name { get; set; } = string.Empty;
            public string TeamColor { get; set; } = string.Empty;
            public int Scores { get; set; }
        }

        private static MemoryStream DrawScoreTableWithAssist(Dictionary<string, Score> table, dynamic stats)
        {
            var topScorers = stats.stats?.topPlayers?.topScorers;
            var topAssists = stats.stats?.topPlayers?.topAssists;
            using (var templateStream = System.IO.File.OpenRead($@"templates/standingsWithAssists.png"))
            using (var templateBitmap = SKBitmap.Decode(templateStream))
            {
                // Create an SKImage from the template bitmap
                using (var surface = SKSurface.Create(new SKImageInfo(templateBitmap.Width, templateBitmap.Height)))
                {
                    var canvas = surface.Canvas;
                    // Draw the template onto the canvas
                    canvas.DrawBitmap(templateBitmap, SKPoint.Empty);

                    var paint = new SKPaint
                    {
                        Color = SKColors.White,
                        TextSize = 16,
                        IsAntialias = true,
                        TextAlign = SKTextAlign.Center,
                        Typeface = SKTypeface.FromFamilyName("Berlin Sans FB Demi", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
                    };

                    using (var shirtIconStream = System.IO.File.OpenRead($@"templates/teamPlaceHolder.png"))
                    using (var placeHolder = SKBitmap.Decode(shirtIconStream))
                    {
                        //float yOffset = 477; // Adjust as necessary for your design
                        var index = 0;

                        foreach (var team in table)
                        {
                            var linePoint = new Point(106, 144 + index * 44);
                            var lineTextPoint = new Point(106, 170 + index * 44);
                            canvas.DrawBitmap(placeHolder, linePoint.X, linePoint.Y);
                            DrawText(144, lineTextPoint.Y, paint, canvas, $"{team.Key}");
                            DrawText(232, lineTextPoint.Y, paint, canvas, $"{team.Value.GP}");
                            DrawText(293, lineTextPoint.Y, paint, canvas, $"{team.Value.W}");
                            DrawText(325, lineTextPoint.Y, paint, canvas, $"{team.Value.D}");
                            DrawText(357, lineTextPoint.Y, paint, canvas, $"{team.Value.L}");
                            DrawText(392, lineTextPoint.Y, paint, canvas, $"{team.Value.Gf}");
                            DrawText(438, lineTextPoint.Y, paint, canvas, $"{team.Value.Ga}");
                            DrawText(494, lineTextPoint.Y, paint, canvas, $"{team.Value.Points}");

                            index++;
                        }
                    }

                    var topScorerName = new SKPaint
                    {
                        Color = SKColors.White,
                        TextSize = 18,
                        IsAntialias = true,
                        TextAlign = SKTextAlign.Center,
                        Typeface = SKTypeface.FromFamilyName("Myriad Hebrew", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
                    };

                    var topScorerValue = new SKPaint
                    {
                        Color = SKColors.White,
                        TextSize = 30,
                        IsAntialias = true,
                        TextAlign = SKTextAlign.Center,
                        Typeface = SKTypeface.FromFamilyName("Berlin Sans FB Demi", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
                    };

                    var scorersPaint = new SKPaint
                    {
                        Color = SKColors.White,
                        TextSize = 14,
                        TextAlign = SKTextAlign.Left,
                        IsAntialias = true,
                        Typeface = SKTypeface.FromFamilyName("Berlin Sans FB Demi", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
                    };

                    var scorerValuePaint = new SKPaint
                    {
                        Color = SKColors.White,
                        TextSize = 14,
                        TextAlign = SKTextAlign.Right,
                        IsAntialias = true,
                        Typeface = SKTypeface.FromFamilyName("Berlin Sans FB Demi", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
                    };

                    var scorers = topScorers == null
                        ? Enumerable.Empty<dynamic>()
                        : (IEnumerable<dynamic>)topScorers;
                    if (scorers.Any())
                    {
                        DrawText(186, 441, topScorerName, canvas, Helpers.ReverseIfNeeded(topScorers[0].name.ToString()));
                        DrawText(186, 474, topScorerValue, canvas, topScorers[0].scores.ToString());


                        var indexScorers = 0;
                        foreach (var item in scorers.Skip(1))
                        {
                            DrawText(83, 515 + indexScorers * 20, scorersPaint, canvas, Helpers.ReverseIfNeeded(item.name.ToString()));
                            DrawText(288, 515 + indexScorers * 20, scorerValuePaint, canvas, item.scores.ToString());
                            indexScorers++;
                        }
                    }

                    var assists = topAssists == null
                        ? Enumerable.Empty<dynamic>()
                        : (IEnumerable<dynamic>)topAssists;
                    if (assists.Any())
                    {
                        DrawText(489, 441, topScorerName, canvas, Helpers.ReverseIfNeeded(topAssists[0].name.ToString()));
                        DrawText(489, 474, topScorerValue, canvas, topAssists[0].scores.ToString());

                        var indexScorers = 0;
                        foreach (var item in assists.Skip(1))
                        {
                            DrawText(409, 515 + indexScorers * 20, scorersPaint, canvas, Helpers.ReverseIfNeeded(item.name.ToString()));
                            DrawText(569, 515 + indexScorers * 20, scorerValuePaint, canvas, item.scores.ToString());
                            indexScorers++;
                        }
                    }

                    // Save the final image
                    using (var image = surface.Snapshot())
                    using (var png = image.Encode(SKEncodedImageFormat.Png, 100))
                    using (var ms = new MemoryStream())
                    {
                        png.SaveTo(ms);
                        return ms;
                        //using (FileStream fileStream = new FileStream(@"C:\Users\potok\OneDrive\שולחן העבודה\ddd2.jpg", FileMode.Create))
                        //{
                        //    fileStream.Write(ms.ToArray());
                        //    return fileStream;
                        //}
                    }
                }
            }
        }

        private static MemoryStream DrawScoreTableLegacy(Dictionary<string, Score> table, dynamic stats)
        {
            var topScorers = stats.stats.scorers;

            using (var templateStream = System.IO.File.OpenRead($@"templates/standings.png"))
            using (var templateBitmap = SKBitmap.Decode(templateStream))
            {
                // Create an SKImage from the template bitmap
                using (var surface = SKSurface.Create(new SKImageInfo(templateBitmap.Width, templateBitmap.Height)))
                {
                    var canvas = surface.Canvas;
                    // Draw the template onto the canvas
                    canvas.DrawBitmap(templateBitmap, SKPoint.Empty);

                    var paint = new SKPaint
                    {
                        Color = SKColors.White,
                        TextSize = 16,
                        IsAntialias = true,
                        TextAlign = SKTextAlign.Center,
                        Typeface = SKTypeface.FromFamilyName("Berlin Sans FB Demi", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
                    };

                    using (var shirtIconStream = System.IO.File.OpenRead($@"templates/teamPlaceHolder.png"))
                    using (var placeHolder = SKBitmap.Decode(shirtIconStream))
                    {
                        //float yOffset = 477; // Adjust as necessary for your design
                        var index = 0;

                        foreach (var team in table)
                        {
                            var linePoint = new Point(106, 144 + index * 44);
                            var lineTextPoint = new Point(106, 170 + index * 44);
                            canvas.DrawBitmap(placeHolder, linePoint.X, linePoint.Y);
                            DrawText(144, lineTextPoint.Y, paint, canvas, $"{team.Key}");
                            DrawText(232, lineTextPoint.Y, paint, canvas, $"{team.Value.GP}");
                            DrawText(293, lineTextPoint.Y, paint, canvas, $"{team.Value.W}");
                            DrawText(325, lineTextPoint.Y, paint, canvas, $"{team.Value.D}");
                            DrawText(357, lineTextPoint.Y, paint, canvas, $"{team.Value.L}");
                            DrawText(392, lineTextPoint.Y, paint, canvas, $"{team.Value.Gf}");
                            DrawText(438, lineTextPoint.Y, paint, canvas, $"{team.Value.Ga}");
                            DrawText(494, lineTextPoint.Y, paint, canvas, $"{team.Value.Points}");

                            index++;
                        }
                    }

                    var topScorerName = new SKPaint
                    {
                        Color = SKColors.White,
                        TextSize = 18,
                        IsAntialias = true,
                        TextAlign = SKTextAlign.Center,
                        Typeface = SKTypeface.FromFamilyName("Myriad Hebrew", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
                    };

                    var topScorerValue = new SKPaint
                    {
                        Color = SKColors.White,
                        TextSize = 30,
                        IsAntialias = true,
                        TextAlign = SKTextAlign.Center,
                        Typeface = SKTypeface.FromFamilyName("Berlin Sans FB Demi", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
                    };


                    var scorers = (IEnumerable<dynamic>)topScorers;
                    if (scorers.Any())
                    {
                        DrawText(315, 478, topScorerName, canvas, Helpers.ReverseIfNeeded(topScorers[0].name.ToString()));
                        DrawText(315, 507, topScorerValue, canvas, topScorers[0].scores.ToString());

                        var scorersPaint = new SKPaint
                        {
                            Color = SKColors.White,
                            TextSize = 14,
                            TextAlign = SKTextAlign.Left,
                            IsAntialias = true,
                            Typeface = SKTypeface.FromFamilyName("Berlin Sans FB Demi", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
                        };

                        var indexScorers = 0;
                        foreach (var item in scorers.Skip(1))
                        {
                            DrawText(184, 552 + indexScorers * 20, scorersPaint, canvas, Helpers.ReverseIfNeeded(item.name.ToString()));
                            DrawText(438, 552 + indexScorers * 20, scorersPaint, canvas, item.scores.ToString());
                            indexScorers++;
                        }
                    }

                    // Save the final image
                    using (var image = surface.Snapshot())
                    using (var png = image.Encode(SKEncodedImageFormat.Png, 100))
                    using (var ms = new MemoryStream())
                    {
                        png.SaveTo(ms);
                        return ms;
                        //using (FileStream fileStream = new FileStream(@"C:\Users\potok\OneDrive\שולחן העבודה\ddd2.jpg", FileMode.Create))
                        //{
                        //    fileStream.Write(ms.ToArray());
                        //    return fileStream;
                        //}
                    }
                }
            }
        }


        private static void DrawText(float x, float y, SKPaint paint, SKCanvas canvas, string text)
        {
            canvas.DrawText(text, x, y, paint);
        }

        private static CultureInfo GetCulture(string symbol)
        {
            try
            {
                return new CultureInfo(symbol);
            }
            catch (Exception)
            {
                return CultureInfo.CurrentCulture;
            }
        }

        internal static object GeneratePlayersListImage(List<string> players, string teamName, string location, string date, string dayInWeek, string currentCulture)
        {
            var sidePadding = 10;
            var topPadding = 30;

            var culture = GetCulture(currentCulture);

            var dateTime = DateTime.Parse(date);
            var dateTimeDisplay = dateTime.ToString("g", culture);

            var dayOfWeekCurrentCulture = Helpers.ReverseIfNeeded(culture.DateTimeFormat.GetDayName(dateTime.DayOfWeek));
            var dayInWeekDisplay = dayOfWeekCurrentCulture.ToUpper() == dayInWeek.ToUpper() ? dayInWeek.ToUpper() : $"{dayInWeek} | {dayOfWeekCurrentCulture}";

            teamName = Helpers.ReverseIfNeeded(teamName);
            location = Helpers.ReverseIfNeeded(location);

            using (var templateStream = System.IO.File.OpenRead($@"templates/playersListTemplate1.png"))
            using (var templateBitmap = SKBitmap.Decode(templateStream))
            {
                // Create an SKImage from the template bitmap
                using (var surface = SKSurface.Create(new SKImageInfo(templateBitmap.Width, templateBitmap.Height)))
                {
                    var canvas = surface.Canvas;
                    // Draw the template onto the canvas
                    canvas.DrawBitmap(templateBitmap, SKPoint.Empty);

                    var mainHeaderPaint = new SKPaint
                    {
                        Color = SKColor.Parse("#1f2b3b"),
                        TextSize = 33,
                        IsAntialias = true,
                        Typeface = SKTypeface.FromFamilyName("Myriad Hebrew", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
                    };

                    var timeAndLocationpaint = new SKPaint
                    {
                        Color = SKColor.Parse("#1f2b3b"),
                        TextSize = 16,
                        IsAntialias = true,
                        Typeface = SKTypeface.FromFamilyName("Myriad Hebrew", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
                    };

                    var firstNamePaint = new SKPaint
                    {
                        Color = SKColor.Parse("#1f2b3b"),
                        TextSize = 18,
                        IsAntialias = true,
                        TextAlign = SKTextAlign.Center,
                        Typeface = SKTypeface.FromFamilyName("Myriad Hebrew", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
                    };

                    var lastNamesPaint = new SKPaint
                    {
                        Color = SKColor.Parse("#1f2b3b"),
                        TextSize = 12,
                        IsAntialias = true,
                        TextAlign = SKTextAlign.Center,
                    };

                    var isRtl = Helpers.IsRightToLeft(teamName);
                    DrawText(isRtl ? templateBitmap.Width - mainHeaderPaint.MeasureText(teamName) - 10 : 221, topPadding, mainHeaderPaint, canvas, teamName);
                    DrawText(isRtl ? templateBitmap.Width - mainHeaderPaint.MeasureText(players.Count + " PARTICIPANTS") - 10 : 221, topPadding + mainHeaderPaint.TextSize + 5, mainHeaderPaint, canvas, players.Count + " PARTICIPANTS");               
                    DrawText(195 - timeAndLocationpaint.MeasureText(dayInWeekDisplay), 20, timeAndLocationpaint, canvas, dayInWeekDisplay);
                    DrawText(195 - timeAndLocationpaint.MeasureText(dateTimeDisplay), 25 +  timeAndLocationpaint.TextSize, timeAndLocationpaint, canvas, dateTimeDisplay);
                    //DrawText(195 - timeAndLocationpaint.MeasureText(date) - timeAndLocationpaint.MeasureText(time) - 15, 25 + timeAndLocationpaint.TextSize, timeAndLocationpaint, canvas, time);
                    DrawText(isRtl ? 195 - timeAndLocationpaint.MeasureText(location) : 4, 40 + timeAndLocationpaint.TextSize * 2, timeAndLocationpaint, canvas, location);

                    //float yOffset = 477; // Adjust as necessary for your design
                    var offsetX = templateBitmap.Width-10;
                    var offsetY = 100;
                    var spaceBetween = 17;
                    var numberOfPlayersInRow = 7;
                    var currRow = 0;
                    var currPlayerIndex = 0;

                    foreach (var name in players)
                    {
                        currRow = currPlayerIndex / numberOfPlayersInRow;
                        

                        var nameAsParts = name.Split(" ");
                        var firstName = nameAsParts[0];
                        var lastNames = string.Join(" ", nameAsParts.Take(new Range(1, nameAsParts.Length)));

                        using (var shirtIconStream = System.IO.File.OpenRead($@"templates/plyaerShirt.png"))
                        using (var shirtIconBitmap = SKBitmap.Decode(shirtIconStream))
                        {
                            var currShirtX = offsetX - shirtIconBitmap.Width * ((currPlayerIndex % numberOfPlayersInRow) + 1);
                            canvas.DrawBitmap(shirtIconBitmap, currShirtX, offsetY);
                            string bidiLine = Helpers.ReverseIfNeeded(firstName.ToUpper());
                            canvas.DrawText(bidiLine, currShirtX + shirtIconBitmap.Width / 2, offsetY + shirtIconBitmap.Height + 15, firstNamePaint);

                            string bidiLine2 = Helpers.ReverseIfNeeded(lastNames.ToUpper());
                            canvas.DrawText(bidiLine2, currShirtX + shirtIconBitmap.Width / 2, offsetY + shirtIconBitmap.Height + 15 + firstNamePaint.TextSize, lastNamesPaint);

                        }
                        currPlayerIndex++;
                        if (currPlayerIndex % numberOfPlayersInRow == 0)
                        {
                            offsetY += 120;
                            offsetX = templateBitmap.Width-10;
                        }
                        else
                        {
                            offsetX -= spaceBetween;
                        }
                    }

                    // Save the final image
                    using (var image = surface.Snapshot())
                    using (var png = image.Encode(SKEncodedImageFormat.Png, 100))
                    using (var ms = new MemoryStream())
                    {
                        png.SaveTo(ms);
                        return ms;
                        //using (FileStream fileStream = new FileStream(@"C:\Users\potok\OneDrive\שולחן העבודה\ddd2.jpg", FileMode.Create))
                        //{
                        //    fileStream.Write(ms.ToArray());
                        //    return fileStream;
                        //}
                    }
                }
            }
        }



        internal static MemoryStream GeneratePlayersListImageTemplate3(List<string> players, string teamName, string location, string date, string dayInWeek, string currentCulture)
        {
            var culture = GetCulture(currentCulture);
            var content = string.Join(" ", players.Prepend(teamName).Prepend(location));
            var hasHebrewContent = content.Any(character => character >= '\u0590' && character <= '\u05FF');
            var hasArabicContent = content.Any(character => character >= '\u0600' && character <= '\u08FF');
            var isRtlLayout = culture.TextInfo.IsRightToLeft || hasHebrewContent || hasArabicContent;
            if (hasHebrewContent && !culture.TextInfo.IsRightToLeft)
            {
                culture = GetCulture("he-IL");
            }
            else if (hasArabicContent && !culture.TextInfo.IsRightToLeft)
            {
                culture = GetCulture("ar");
            }

            var matchDate = ParseMatchDate(date);
            var matchdayLabel = GetLocalizedLabel(culture, "MATCHDAY SQUAD", "סגל המשחק", "قائمة المباراة");
            var playersLabel = GetLocalizedLabel(culture, "PLAYERS", "שחקנים", "اللاعبون");
            var locationLabel = GetLocalizedLabel(culture, "VENUE", "מיקום", "المكان");
            var displayTeamName = string.IsNullOrWhiteSpace(teamName) ? "TEAMIFY MATCH" : teamName.Trim();
            var displayLocation = string.IsNullOrWhiteSpace(location)
                ? GetLocalizedLabel(culture, "Location to be confirmed", "המיקום יעודכן", "سيتم تحديد المكان")
                : location.Trim();
            var displayDate = matchDate.HasValue
                ? FormatRosterDate(matchDate.Value, culture)
                : dayInWeek;

            using (var templateStream = System.IO.File.OpenRead(@"templates/playersListTemplate3.png"))
            using (var templateBitmap = SKBitmap.Decode(templateStream))
            using (var surface = SKSurface.Create(new SKImageInfo(templateBitmap.Width, templateBitmap.Height)))
            using (var accentPaint = new SKPaint { Color = SKColor.Parse("#50F3AA"), IsAntialias = true })
            using (var subtlePaint = new SKPaint { Color = new SKColor(255, 255, 255, 20), IsAntialias = true })
            using (var borderPaint = new SKPaint { Color = new SKColor(133, 255, 203, 46), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2 })
            using (var titlePaint = CreateRosterTextPaint(SKColors.White, 70, true))
            using (var labelPaint = CreateRosterTextPaint(SKColor.Parse("#88F8BF"), 25, true))
            using (var metadataPaint = CreateRosterTextPaint(SKColor.Parse("#D8F5E8"), 28, false))
            using (var playerPaint = CreateRosterTextPaint(SKColors.White, 34, true))
            using (var numberPaint = CreateRosterTextPaint(SKColor.Parse("#071A2E"), 23, true))
            using (var footerPaint = CreateRosterTextPaint(new SKColor(218, 245, 233, 185), 20, false))
            {
                var canvas = surface.Canvas;
                canvas.DrawBitmap(templateBitmap, SKPoint.Empty);

                DrawRosterPill(canvas, matchdayLabel, 70, 72, labelPaint, accentPaint, isRtlLayout);
                DrawFittedRosterText(canvas, displayTeamName, new SKRect(70, 130, 1010, 230), 205, titlePaint, SKTextAlign.Center, 42);

                var metadataTop = 260f;
                var metadataBottom = 340f;
                var metadataGap = 18f;
                var metadataWidth = (940f - metadataGap) / 2f;
                var firstMetadataRect = new SKRect(70, metadataTop, 70 + metadataWidth, metadataBottom);
                var secondMetadataRect = new SKRect(firstMetadataRect.Right + metadataGap, metadataTop, 1010, metadataBottom);
                DrawMetadataCard(canvas, firstMetadataRect, displayDate, subtlePaint, borderPaint, metadataPaint, isRtlLayout);
                DrawMetadataCard(canvas, secondMetadataRect, $"{locationLabel}  ·  {displayLocation}", subtlePaint, borderPaint, metadataPaint, isRtlLayout);

                var countText = $"{players.Count} {playersLabel}";
                DrawFittedRosterText(
                    canvas,
                    countText,
                    new SKRect(70, 360, 1010, 410),
                    397,
                    labelPaint,
                    isRtlLayout ? SKTextAlign.Right : SKTextAlign.Left,
                    20);

                DrawRosterPlayers(canvas, players, isRtlLayout, playerPaint, numberPaint, subtlePaint, borderPaint, accentPaint);

                var rights = $"TEAMIFY  ·  © {DateTime.UtcNow.Year} ALL RIGHTS RESERVED";
                DrawFittedRosterText(canvas, rights, new SKRect(70, 1268, 1010, 1296), 1290, footerPaint, SKTextAlign.Center, 15);

                using (var image = surface.Snapshot())
                using (var png = image.Encode(SKEncodedImageFormat.Png, 100))
                using (var ms = new MemoryStream())
                {
                    png.SaveTo(ms);
                    return ms;
                }
            }
        }

        private static void DrawRosterPlayers(
            SKCanvas canvas,
            IReadOnlyList<string> players,
            bool isRtlLayout,
            SKPaint playerPaint,
            SKPaint numberPaint,
            SKPaint cardPaint,
            SKPaint borderPaint,
            SKPaint accentPaint)
        {
            const float areaLeft = 70;
            const float areaRight = 1010;
            const float areaTop = 430;
            const float areaBottom = 1238;
            const float columnGap = 20;

            var columnCount = players.Count > 30 ? 3 : 2;
            var rowsPerColumn = Math.Max(1, (int)Math.Ceiling(players.Count / (double)columnCount));
            var columnWidth = (areaRight - areaLeft - columnGap * (columnCount - 1)) / columnCount;
            var rowGap = rowsPerColumn > 14 ? 7f : 10f;
            var rowHeight = Math.Min(70f, (areaBottom - areaTop - rowGap * (rowsPerColumn - 1)) / rowsPerColumn);
            var textSize = columnCount == 3 ? 28f : rowHeight < 55 ? 29f : 34f;
            playerPaint.TextSize = textSize;

            for (var index = 0; index < players.Count; index++)
            {
                var logicalColumn = index / rowsPerColumn;
                var row = index % rowsPerColumn;
                var visualColumn = isRtlLayout ? columnCount - logicalColumn - 1 : logicalColumn;
                var left = areaLeft + visualColumn * (columnWidth + columnGap);
                var top = areaTop + row * (rowHeight + rowGap);
                var cardRect = new SKRect(left, top, left + columnWidth, top + rowHeight);

                canvas.DrawRoundRect(cardRect, 16, 16, cardPaint);
                canvas.DrawRoundRect(cardRect, 16, 16, borderPaint);

                var numberCenterX = isRtlLayout ? cardRect.Right - 34 : cardRect.Left + 34;
                var numberCenterY = cardRect.MidY;
                canvas.DrawCircle(numberCenterX, numberCenterY, Math.Min(22, rowHeight * 0.34f), accentPaint);
                numberPaint.TextAlign = SKTextAlign.Center;
                canvas.DrawText((index + 1).ToString(CultureInfo.InvariantCulture), numberCenterX, numberCenterY + 8, numberPaint);

                var textRect = isRtlLayout
                    ? new SKRect(cardRect.Left + 18, cardRect.Top, cardRect.Right - 70, cardRect.Bottom)
                    : new SKRect(cardRect.Left + 70, cardRect.Top, cardRect.Right - 18, cardRect.Bottom);
                DrawFittedRosterText(
                    canvas,
                    players[index],
                    textRect,
                    cardRect.MidY + textSize * 0.34f,
                    playerPaint,
                    isRtlLayout ? SKTextAlign.Right : SKTextAlign.Left,
                    20);
            }
        }

        private static void DrawRosterPill(SKCanvas canvas, string text, float x, float y, SKPaint textPaint, SKPaint fillPaint, bool isRtl)
        {
            var preparedText = PrepareTextForCanvas(text);
            var textWidth = textPaint.MeasureText(preparedText);
            var originalColor = textPaint.Color;
            var originalAlignment = textPaint.TextAlign;
            var pillRect = isRtl
                ? new SKRect(1010 - textWidth - 44, y, 1010, y + 45)
                : new SKRect(x, y, x + textWidth + 44, y + 45);
            canvas.DrawRoundRect(pillRect, 22, 22, fillPaint);
            textPaint.Color = SKColor.Parse("#071A2E");
            textPaint.TextAlign = SKTextAlign.Center;
            canvas.DrawText(preparedText, pillRect.MidX, pillRect.MidY + 9, textPaint);
            textPaint.Color = originalColor;
            textPaint.TextAlign = originalAlignment;
        }

        private static void DrawMetadataCard(
            SKCanvas canvas,
            SKRect rect,
            string text,
            SKPaint fillPaint,
            SKPaint borderPaint,
            SKPaint textPaint,
            bool isRtl)
        {
            canvas.DrawRoundRect(rect, 18, 18, fillPaint);
            canvas.DrawRoundRect(rect, 18, 18, borderPaint);
            DrawFittedRosterText(
                canvas,
                text,
                new SKRect(rect.Left + 24, rect.Top, rect.Right - 24, rect.Bottom),
                rect.MidY + 10,
                textPaint,
                isRtl ? SKTextAlign.Right : SKTextAlign.Left,
                20);
        }

        private static SKPaint CreateRosterTextPaint(SKColor color, float size, bool bold)
        {
            return new SKPaint
            {
                Color = color,
                TextSize = size,
                IsAntialias = true,
                Typeface = SKTypeface.FromFamilyName(
                    "Arial",
                    bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
                    SKFontStyleWidth.Normal,
                    SKFontStyleSlant.Upright)
            };
        }

        private static void DrawFittedRosterText(
            SKCanvas canvas,
            string text,
            SKRect availableRect,
            float baseline,
            SKPaint paint,
            SKTextAlign alignment,
            float minimumTextSize)
        {
            var originalSize = paint.TextSize;
            var value = string.IsNullOrWhiteSpace(text) ? "—" : text.Trim();
            var preparedText = PrepareTextForCanvas(value);

            while (paint.TextSize > minimumTextSize && paint.MeasureText(preparedText) > availableRect.Width)
            {
                paint.TextSize -= 1;
            }

            while (paint.MeasureText(preparedText) > availableRect.Width && value.Length > 2)
            {
                value = value.Substring(0, value.Length - 2).TrimEnd() + "…";
                preparedText = PrepareTextForCanvas(value);
            }

            paint.TextAlign = alignment;
            var x = alignment == SKTextAlign.Center
                ? availableRect.MidX
                : alignment == SKTextAlign.Right
                    ? availableRect.Right
                    : availableRect.Left;
            canvas.DrawText(preparedText, x, baseline, paint);
            paint.TextSize = originalSize;
        }

        private static DateTime? ParseMatchDate(string date)
        {
            if (DateTime.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var invariantDate))
            {
                return invariantDate;
            }

            if (DateTime.TryParse(date, out var currentDate))
            {
                return currentDate;
            }

            return null;
        }

        private static string FormatRosterDate(DateTime date, CultureInfo culture)
        {
            return culture.TwoLetterISOLanguageName switch
            {
                "he" => date.ToString("ddd, dd'/'MM'/'yyyy · HH:mm", culture),
                "ar" => date.ToString("ddd، dd/MM/yyyy · HH:mm", culture),
                _ => date.ToString("ddd, d MMM · HH:mm", culture)
            };
        }

        private static string GetLocalizedLabel(CultureInfo culture, string english, string hebrew, string arabic)
        {
            return culture.TwoLetterISOLanguageName switch
            {
                "he" => hebrew,
                "ar" => arabic,
                _ => english
            };
        }

        private static string PrepareTextForCanvas(string text)
        {
            if (string.IsNullOrEmpty(text) || !Helpers.IsRightToLeft(text))
            {
                return text;
            }

            var elements = new List<string>();
            var enumerator = StringInfo.GetTextElementEnumerator(text);
            while (enumerator.MoveNext())
            {
                elements.Add(enumerator.GetTextElement());
            }

            elements.Reverse();
            var visualText = string.Concat(elements);

            return Regex.Replace(
                visualText,
                @"[\d]+(?:[\s.,:/-][\d]+)*",
                match => new string(match.Value.Reverse().ToArray()));
        }

        internal static object GeneratePlayersListImageTemplate2(List<string> players, string teamName, string location, string date, string dayInWeek, string currentCulture)
        {
            var playersAreaHeight = 390;
            var playersCount = players.Count;
            var playerNameFontSize = 18;
            for (int i = 0; i < 8; i++)
            {
                var lineHeightExtra = playerNameFontSize / 3;
                var playerNameLineHeight = playerNameFontSize + lineHeightExtra;
                if (playersAreaHeight - ((playersCount / 2) * (playerNameLineHeight)) <= 0)
                {
                    playerNameFontSize--;
                }
                else
                {
                    break;
                }
            }

            var culture = GetCulture(currentCulture);

            var dateTime = DateTime.Parse(date);
            var dateTimeDisplay = dateTime.ToString("g", culture);
            
            var dataInfoPaintStyle = new SKPaint
            {
                Color = SKColors.White,
                TextSize = 12,
                TextAlign = SKTextAlign.Center,
                IsAntialias = true,
                Typeface = SKTypeface.FromFamilyName("Berlin Sans FB Demi", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
            };

            var matchdayNamePaintStyle = new SKPaint
            {
                Color = SKColors.LightGoldenrodYellow,
                TextSize = 32,
                TextAlign = SKTextAlign.Center,
                IsAntialias = true,
                Typeface = SKTypeface.FromFamilyName("Berlin Sans FB Demi", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
            };

            var playerNamePaintStyle = new SKPaint
            {
                Color = SKColors.White,
                TextSize = playerNameFontSize,
                TextAlign = SKTextAlign.Left,
                IsAntialias = true,
                Typeface = SKTypeface.FromFamilyName("Berlin Sans FB Demi", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
            };

            var matchdayDrawObject = new ImageGraphicObjectWrapper(teamName, matchdayNamePaintStyle);
            var locationDrawObject = new ImageGraphicObjectWrapper(location, dataInfoPaintStyle);
            var dateDrawObject = new ImageGraphicObjectWrapper(dateTimeDisplay, dataInfoPaintStyle);
            var dayInWeekDrawObject = new ImageGraphicObjectWrapper(culture.DateTimeFormat.GetDayName(dateTime.DayOfWeek), dataInfoPaintStyle);

            using (var templateStream = System.IO.File.OpenRead($@"templates/playersListTemplate2.png"))
            {
                var canvasWrapper = new ImageGraphicWrapper(templateStream);
                canvasWrapper.DrawCanvas();

                canvasWrapper.Draw(canvasWrapper.HorizontalMiddle, 130, dayInWeekDrawObject);
                canvasWrapper.Draw(183, 130, dateDrawObject);
                canvasWrapper.Draw(440, 130, locationDrawObject);
                canvasWrapper.Draw(canvasWrapper.HorizontalMiddle, 170, matchdayDrawObject);

                int columnsNumber = (int)Math.Ceiling(players.Count / 15.0);

                var startFromHeight = 200;
                var columnsLeftOffset = Helpers.GetSliceCenters(canvasWrapper.Width, columnsNumber, 30);
                var leftOffsetIndex = 0;
                var lineHeight = playerNameFontSize + playerNameFontSize / 3;
                var line = 0;
                var toggle = true;
                foreach (var name in players)
                {
                    var nameWrapper = new ImageGraphicObjectWrapper(name, playerNamePaintStyle);
                    canvasWrapper.Draw(columnsLeftOffset[leftOffsetIndex], startFromHeight + line * lineHeight, nameWrapper, toggle);
                    toggle = !toggle;
                    leftOffsetIndex++;
                    if (leftOffsetIndex >= columnsLeftOffset.Count)
                    {
                        leftOffsetIndex = 0;
                        line++;
                    }
                }

                return canvasWrapper.Save();
            }
                
            
        }

        internal static MemoryStream GenerateTeamsOverviewImage(
            List<TeamShareItem> teams,
            string matchName,
            string location,
            string date,
            string dayInWeek,
            string currentCulture)
        {
            var culture = GetCulture(currentCulture);
            var allPlayers = teams.SelectMany(team => team.Players).ToList();
            var content = string.Join(" ", allPlayers.Prepend(matchName).Prepend(location));
            var hasHebrewContent = content.Any(character => character >= '\u0590' && character <= '\u05FF');
            var hasArabicContent = content.Any(character => character >= '\u0600' && character <= '\u08FF');
            var isRtlLayout = culture.TextInfo.IsRightToLeft || hasHebrewContent || hasArabicContent;
            if (hasHebrewContent && !culture.TextInfo.IsRightToLeft)
            {
                culture = GetCulture("he-IL");
            }
            else if (hasArabicContent && !culture.TextInfo.IsRightToLeft)
            {
                culture = GetCulture("ar");
            }

            var matchDate = ParseMatchDate(date);
            var title = string.IsNullOrWhiteSpace(matchName) ? "TEAMIFY MATCH" : matchName.Trim();
            var shareLabel = GetLocalizedLabel(culture, "MATCHDAY TEAMS", "הקבוצות למשחק", "فرق المباراة");
            var teamsLabel = GetLocalizedLabel(culture, "TEAMS", "קבוצות", "فرق");
            var locationLabel = GetLocalizedLabel(culture, "VENUE", "מיקום", "المكان");
            var displayLocation = string.IsNullOrWhiteSpace(location)
                ? GetLocalizedLabel(culture, "Location to be confirmed", "המיקום יעודכן", "سيتم تحديد المكان")
                : location.Trim();
            var displayDate = matchDate.HasValue ? FormatRosterDate(matchDate.Value, culture) : dayInWeek;

            using (var templateStream = System.IO.File.OpenRead(@"templates/playersListTemplate3.png"))
            using (var templateBitmap = SKBitmap.Decode(templateStream))
            using (var surface = SKSurface.Create(new SKImageInfo(templateBitmap.Width, templateBitmap.Height)))
            using (var accentPaint = new SKPaint { Color = SKColor.Parse("#50F3AA"), IsAntialias = true })
            using (var subtlePaint = new SKPaint { Color = new SKColor(255, 255, 255, 20), IsAntialias = true })
            using (var borderPaint = new SKPaint { Color = new SKColor(133, 255, 203, 46), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2 })
            using (var titlePaint = CreateRosterTextPaint(SKColors.White, 70, true))
            using (var labelPaint = CreateRosterTextPaint(SKColor.Parse("#88F8BF"), 25, true))
            using (var metadataPaint = CreateRosterTextPaint(SKColor.Parse("#D8F5E8"), 28, false))
            using (var teamNamePaint = CreateRosterTextPaint(SKColors.White, 30, true))
            using (var playerPaint = CreateRosterTextPaint(SKColors.White, 25, true))
            using (var footerPaint = CreateRosterTextPaint(new SKColor(218, 245, 233, 185), 20, false))
            {
                var canvas = surface.Canvas;
                canvas.DrawBitmap(templateBitmap, SKPoint.Empty);

                DrawRosterPill(canvas, shareLabel, 70, 72, labelPaint, accentPaint, isRtlLayout);
                DrawFittedRosterText(canvas, title, new SKRect(70, 130, 1010, 230), 205, titlePaint, SKTextAlign.Center, 42);

                var metadataGap = 18f;
                var metadataWidth = (940f - metadataGap) / 2f;
                var firstMetadataRect = new SKRect(70, 260, 70 + metadataWidth, 340);
                var secondMetadataRect = new SKRect(firstMetadataRect.Right + metadataGap, 260, 1010, 340);
                DrawMetadataCard(canvas, firstMetadataRect, displayDate, subtlePaint, borderPaint, metadataPaint, isRtlLayout);
                DrawMetadataCard(canvas, secondMetadataRect, $"{locationLabel}  ·  {displayLocation}", subtlePaint, borderPaint, metadataPaint, isRtlLayout);

                DrawFittedRosterText(
                    canvas,
                    $"{teams.Count} {teamsLabel}",
                    new SKRect(70, 360, 1010, 410),
                    397,
                    labelPaint,
                    isRtlLayout ? SKTextAlign.Right : SKTextAlign.Left,
                    20);

                DrawTeamOverviewCards(
                    canvas,
                    teams,
                    culture,
                    isRtlLayout,
                    teamNamePaint,
                    playerPaint,
                    subtlePaint,
                    borderPaint);

                var rights = $"TEAMIFY  ·  © {DateTime.UtcNow.Year} ALL RIGHTS RESERVED";
                DrawFittedRosterText(canvas, rights, new SKRect(70, 1268, 1010, 1296), 1290, footerPaint, SKTextAlign.Center, 15);

                using (var image = surface.Snapshot())
                using (var png = image.Encode(SKEncodedImageFormat.Png, 100))
                using (var ms = new MemoryStream())
                {
                    png.SaveTo(ms);
                    return ms;
                }
            }
        }

        private static void DrawTeamOverviewCards(
            SKCanvas canvas,
            IReadOnlyList<TeamShareItem> teams,
            CultureInfo culture,
            bool isRtlLayout,
            SKPaint teamNamePaint,
            SKPaint playerPaint,
            SKPaint cardPaint,
            SKPaint borderPaint)
        {
            const float areaLeft = 70;
            const float areaRight = 1010;
            const float areaTop = 430;
            const float areaBottom = 1238;
            const float gap = 18;

            var columnCount = teams.Count <= 2 ? Math.Max(1, teams.Count) : teams.Count <= 4 ? 2 : 3;
            var rowCount = Math.Max(1, (int)Math.Ceiling(teams.Count / (double)columnCount));
            var cardWidth = (areaRight - areaLeft - gap * (columnCount - 1)) / columnCount;
            var cardHeight = (areaBottom - areaTop - gap * (rowCount - 1)) / rowCount;

            for (var index = 0; index < teams.Count; index++)
            {
                var logicalColumn = index % columnCount;
                var row = index / columnCount;
                var visualColumn = isRtlLayout ? columnCount - logicalColumn - 1 : logicalColumn;
                var left = areaLeft + visualColumn * (cardWidth + gap);
                var top = areaTop + row * (cardHeight + gap);
                var cardRect = new SKRect(left, top, left + cardWidth, top + cardHeight);
                var team = teams[index];
                var teamColor = GetTeamAccentColor(team.Color);

                canvas.DrawRoundRect(cardRect, 20, 20, cardPaint);
                canvas.DrawRoundRect(cardRect, 20, 20, borderPaint);
                DrawTeamCardPitch(canvas, cardRect);
                using (var teamAccentPaint = new SKPaint { Color = teamColor, IsAntialias = true })
                {
                    canvas.DrawRoundRect(new SKRect(cardRect.Left, cardRect.Top, cardRect.Right, cardRect.Top + 9), 5, 5, teamAccentPaint);
                    DrawJerseyIcon(
                        canvas,
                        isRtlLayout ? cardRect.Right - 36 : cardRect.Left + 36,
                        cardRect.Top + 49,
                        teamColor,
                        0.9f);
                }

                var teamTitle = GetLocalizedTeamName(team, culture, index);
                var titleRect = isRtlLayout
                    ? new SKRect(cardRect.Left + 20, cardRect.Top + 18, cardRect.Right - 72, cardRect.Top + 78)
                    : new SKRect(cardRect.Left + 72, cardRect.Top + 18, cardRect.Right - 20, cardRect.Top + 78);
                DrawFittedRosterText(
                    canvas,
                    teamTitle,
                    titleRect,
                    cardRect.Top + 58,
                    teamNamePaint,
                    isRtlLayout ? SKTextAlign.Right : SKTextAlign.Left,
                    20);

                DrawTeamLineupPlayers(canvas, cardRect, team, teamColor, isRtlLayout, playerPaint);
            }
        }

        private static void DrawTeamLineupPlayers(
            SKCanvas canvas,
            SKRect cardRect,
            TeamShareItem team,
            SKColor teamColor,
            bool isRtlLayout,
            SKPaint playerPaint)
        {
            if (team.Players.Count == 0)
            {
                return;
            }

            var pitchRect = new SKRect(
                cardRect.Left + 22,
                cardRect.Top + 98,
                cardRect.Right - 22,
                cardRect.Bottom - 22);
            var formationRows = GetFormationRows(team.Players.Count);
            var formationTop = pitchRect.Top + 34;
            var formationBottom = pitchRect.Bottom - 43;
            var compactCard = cardRect.Height < 500 || cardRect.Width < 380;
            var (jerseyScale, textSize, maximumNameWidth, minimumTextSize) =
                GetLineupSizing(team.Players.Count, compactCard);
            var standardSlotWidth = pitchRect.Width / formationRows.Max();
            playerPaint.TextSize = textSize;

            var playerIndex = 0;
            for (var rowIndex = 0; rowIndex < formationRows.Length; rowIndex++)
            {
                var playersInRow = formationRows[rowIndex];
                var y = formationRows.Length == 1
                    ? pitchRect.MidY - 12
                    : formationBottom - rowIndex * ((formationBottom - formationTop) / (formationRows.Length - 1));
                var slotWidth = pitchRect.Width / playersInRow;

                for (var slotIndex = 0; slotIndex < playersInRow && playerIndex < team.Players.Count; slotIndex++)
                {
                    var visualSlot = isRtlLayout ? playersInRow - slotIndex - 1 : slotIndex;
                    var centerX = pitchRect.Left + slotWidth * (visualSlot + 0.5f);
                    var playerName = team.Players[playerIndex];

                    DrawJerseyIcon(canvas, centerX, y, teamColor, jerseyScale);

                    var nameTop = y + 22 * jerseyScale;
                    var nameWidth = Math.Max(
                        72,
                        Math.Min(standardSlotWidth - 8, maximumNameWidth));
                    var nameRect = new SKRect(
                        centerX - nameWidth / 2,
                        nameTop,
                        centerX + nameWidth / 2,
                        nameTop + textSize + 12);
                    using (var nameBackgroundPaint = new SKPaint
                    {
                        Color = new SKColor(0, 15, 27, 145),
                        IsAntialias = true
                    })
                    {
                        canvas.DrawRoundRect(nameRect, 8, 8, nameBackgroundPaint);
                    }

                    DrawFittedRosterText(
                        canvas,
                        playerName,
                        new SKRect(nameRect.Left + 5, nameRect.Top, nameRect.Right - 5, nameRect.Bottom),
                        nameRect.Top + textSize,
                        playerPaint,
                        SKTextAlign.Center,
                        minimumTextSize);

                    playerIndex++;
                }
            }
        }

        private static (float JerseyScale, float TextSize, float MaximumNameWidth, float MinimumTextSize)
            GetLineupSizing(int playerCount, bool compactCard)
        {
            if (compactCard)
            {
                return playerCount switch
                {
                    <= 4 => (0.86f, 25f, 190f, 17f),
                    <= 6 => (0.78f, 23f, 180f, 16f),
                    <= 8 => (0.70f, 21f, 170f, 15f),
                    _ => (0.62f, 19f, 155f, 14f)
                };
            }

            return playerCount switch
            {
                <= 4 => (1.16f, 30f, 230f, 20f),
                <= 7 => (1.02f, 27f, 210f, 18f),
                <= 9 => (0.90f, 24f, 195f, 17f),
                _ => (0.80f, 21f, 180f, 15f)
            };
        }

        private static int[] GetFormationRows(int playerCount)
        {
            return playerCount switch
            {
                1 => new[] { 1 },
                2 => new[] { 1, 1 },
                3 => new[] { 1, 2 },
                4 => new[] { 1, 2, 1 },
                5 => new[] { 1, 2, 2 },
                6 => new[] { 1, 2, 3 },
                7 => new[] { 1, 3, 3 },
                8 => new[] { 1, 3, 3, 1 },
                9 => new[] { 1, 3, 3, 2 },
                10 => new[] { 1, 3, 3, 3 },
                _ => new[] { 1, 4, 3, Math.Max(1, playerCount - 8) }
            };
        }

        private static void DrawTeamCardPitch(SKCanvas canvas, SKRect cardRect)
        {
            using (var pitchPaint = new SKPaint
            {
                Color = new SKColor(180, 255, 221, 15),
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 2
            })
            {
                var inset = 14f;
                var pitchRect = new SKRect(
                    cardRect.Left + inset,
                    cardRect.Top + 86,
                    cardRect.Right - inset,
                    cardRect.Bottom - inset);
                canvas.DrawRoundRect(pitchRect, 12, 12, pitchPaint);
                canvas.DrawLine(pitchRect.Left, pitchRect.MidY, pitchRect.Right, pitchRect.MidY, pitchPaint);
                canvas.DrawCircle(pitchRect.MidX, pitchRect.MidY, Math.Min(36, pitchRect.Width * 0.12f), pitchPaint);
                canvas.DrawCircle(pitchRect.MidX, pitchRect.MidY, 3, pitchPaint);

                var penaltyWidth = pitchRect.Width * 0.46f;
                var penaltyHeight = Math.Min(52, pitchRect.Height * 0.15f);
                canvas.DrawRect(
                    pitchRect.MidX - penaltyWidth / 2,
                    pitchRect.Top,
                    penaltyWidth,
                    penaltyHeight,
                    pitchPaint);
                canvas.DrawRect(
                    pitchRect.MidX - penaltyWidth / 2,
                    pitchRect.Bottom - penaltyHeight,
                    penaltyWidth,
                    penaltyHeight,
                    pitchPaint);
            }
        }

        private static void DrawJerseyIcon(SKCanvas canvas, float centerX, float centerY, SKColor teamColor, float scale = 1)
        {
            using (var jerseyPaint = new SKPaint
            {
                Color = teamColor,
                IsAntialias = true,
                Style = SKPaintStyle.Fill
            })
            using (var jerseyOutlinePaint = new SKPaint
            {
                Color = new SKColor(255, 255, 255, 165),
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1.5f * scale
            })
            using (var collarPaint = new SKPaint
            {
                Color = new SKColor(7, 26, 46, 190),
                IsAntialias = true,
                Style = SKPaintStyle.Fill
            })
            using (var path = new SKPath())
            {
                path.MoveTo(centerX - 10 * scale, centerY - 18 * scale);
                path.LineTo(centerX - 25 * scale, centerY - 10 * scale);
                path.LineTo(centerX - 19 * scale, centerY + 2 * scale);
                path.LineTo(centerX - 12 * scale, centerY - 2 * scale);
                path.LineTo(centerX - 12 * scale, centerY + 18 * scale);
                path.LineTo(centerX + 12 * scale, centerY + 18 * scale);
                path.LineTo(centerX + 12 * scale, centerY - 2 * scale);
                path.LineTo(centerX + 19 * scale, centerY + 2 * scale);
                path.LineTo(centerX + 25 * scale, centerY - 10 * scale);
                path.LineTo(centerX + 10 * scale, centerY - 18 * scale);
                path.QuadTo(centerX, centerY - 9 * scale, centerX - 10 * scale, centerY - 18 * scale);
                path.Close();

                canvas.DrawPath(path, jerseyPaint);
                canvas.DrawPath(path, jerseyOutlinePaint);
                canvas.DrawArc(
                    new SKRect(
                        centerX - 7 * scale,
                        centerY - 21 * scale,
                        centerX + 7 * scale,
                        centerY - 8 * scale),
                    0,
                    180,
                    false,
                    collarPaint);
            }
        }

        private static SKColor GetTeamAccentColor(string color)
        {
            return color?.Trim().ToLowerInvariant() switch
            {
                "black" => SKColor.Parse("#94A3B8"),
                "blue" => SKColor.Parse("#60A5FA"),
                "green" => SKColor.Parse("#4ADE80"),
                "orange" => SKColor.Parse("#FB923C"),
                "purple" => SKColor.Parse("#C084FC"),
                "red" => SKColor.Parse("#F87171"),
                "white" => SKColor.Parse("#F8FAFC"),
                "yellow" => SKColor.Parse("#FACC15"),
                _ => SKColor.Parse("#50F3AA")
            };
        }

        private static string GetLocalizedTeamName(TeamShareItem team, CultureInfo culture, int index)
        {
            if (!string.IsNullOrWhiteSpace(team.Name) &&
                !string.Equals(team.Name, team.Color, StringComparison.OrdinalIgnoreCase))
            {
                return team.Name;
            }

            var color = team.Color?.Trim().ToLowerInvariant();
            if (culture.TwoLetterISOLanguageName == "he")
            {
                return color switch
                {
                    "black" => "הקבוצה השחורה",
                    "blue" => "הקבוצה הכחולה",
                    "green" => "הקבוצה הירוקה",
                    "orange" => "הקבוצה הכתומה",
                    "purple" => "הקבוצה הסגולה",
                    "red" => "הקבוצה האדומה",
                    "white" => "הקבוצה הלבנה",
                    "yellow" => "הקבוצה הצהובה",
                    _ => $"קבוצה {index + 1}"
                };
            }

            if (culture.TwoLetterISOLanguageName == "ar")
            {
                return string.IsNullOrWhiteSpace(team.Color) ? $"الفريق {index + 1}" : $"فريق {team.Color}";
            }

            return string.IsNullOrWhiteSpace(team.Color)
                ? $"TEAM {index + 1}"
                : $"{team.Color.ToUpperInvariant()} TEAM";
        }

        public static MemoryStream GenerateTeamsImage(List<string> playerNames, string color)
        {
            var positions = locations[playerNames.Count];

            using (var templateStream = System.IO.File.OpenRead($@"templates/generatedTeams2.png"))
            {
                var canvasWrapper = new ImageGraphicWrapper(templateStream);
                canvasWrapper.DrawCanvas();

                var paint = new SKPaint
                {
                    Color = SKColors.White,
                    TextSize = 26,
                    IsAntialias = true,
                    TextAlign = SKTextAlign.Center,
                    Typeface = SKTypeface.FromFamilyName("Berlin Sans FB Demi", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
                };

                var index = 0;
                foreach (var name in playerNames)
                {
                    float lineHeight = 0; // add it each line
                    var lines = WrapText(paint, name.ToUpper(), 100);
                    foreach (var line in lines)
                    {
                        using (var shirtIconStream = System.IO.File.OpenRead($@"templates/shirt{color}.png"))
                        using (var shirtIconBitmap = SKBitmap.Decode(shirtIconStream))
                        {
                            var currPoint = positions[index];
                            var graphicObject = new ImageGraphicObjectWrapper(shirtIconBitmap);
                            canvasWrapper.Draw(currPoint.X, currPoint.Y, graphicObject);
                        }

                        var playerName = new ImageGraphicObjectWrapper(line, paint);
                        canvasWrapper.Draw(positions[index].X + 49, positions[index].Y + lineHeight + 120, playerName);
                        lineHeight += paint.TextSize + 4;
                    }
                    index++;
                }

                return canvasWrapper.Save();
            }
        }

        public static List<string> WrapText(SKPaint paint, string text, float maxWidth)
        {
            var words = text.Split(' ');
            var lines = new List<string>();
            string currentLine = "";

            foreach (var word in words)
            {
                string testLine = currentLine + (currentLine.Length > 0 ? " " : "") + word;
                float lineWidth =  paint.MeasureText(testLine);  // Measure the width of the test line

                if (lineWidth <= maxWidth)
                {
                    currentLine = testLine;  // Add word to current line
                }
                else
                {
                    if (currentLine.Length > 0)
                    {
                        lines.Add(currentLine);  // Add current line to lines
                    }
                    currentLine = word;  // Start new line with the current word
                }
            }

            if (currentLine.Length > 0)
            {
                lines.Add(currentLine);  // Add last line
            }

            return lines;
        }


        public record Point(int X, int Y);

    }

  

}
