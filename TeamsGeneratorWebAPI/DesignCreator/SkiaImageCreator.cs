using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using TeamsGenerator.Utilities;
using TeamsGeneratorWebAPI.DesignCreator;

namespace TeamsDesignCreator
{
    public static class SkiaImageCreator
    {
        public const int firstLineY = 130;
        public const int secondLineY = 340;


        public static readonly Dictionary<int, List<Point>> locations = new Dictionary<int, List<Point>>() 
        {
           { 1, new List<Point>() {  new Point(287, firstLineY) } },
           { 2, new List<Point>() { new Point(200, firstLineY), new Point(360, firstLineY) } },
           { 3, new List<Point>() { new Point(200, firstLineY), new Point(360, firstLineY), new Point(287, secondLineY) } },
           { 4, new List<Point>() { new Point(142, firstLineY), new Point(432, firstLineY), new Point(142, secondLineY), new Point(432, secondLineY)  } },
           { 5, new List<Point>() { new Point(200, firstLineY), new Point(360, firstLineY), new Point(142, secondLineY), new Point(287, secondLineY), new Point(432, secondLineY)  } },
           { 6, new List<Point>() { new Point(142, firstLineY), new Point(287, firstLineY), new Point(432, firstLineY), new Point(142, secondLineY), new Point(287, secondLineY), new Point(432, secondLineY)  } },
           { 7, new List<Point>() { new Point(287, firstLineY - 100), new Point(142, firstLineY + 100), new Point(287, firstLineY + 100), new Point(432, firstLineY + 100), new Point(142, secondLineY + 100), new Point(287, secondLineY + 100), new Point(432, secondLineY + 100)  } },
           { 8, new List<Point>() { new Point(142, firstLineY - 100), new Point(432, firstLineY - 100), new Point(142, firstLineY + 100), new Point(287, firstLineY + 100), new Point(432, firstLineY + 100), new Point(142, secondLineY + 100), new Point(287, secondLineY + 100), new Point(432, secondLineY + 100)  } },
           { 9, new List<Point>() { new Point(142, firstLineY - 100), new Point(287, firstLineY - 100), new Point(432, firstLineY - 100), new Point(142, firstLineY + 100), new Point(287, firstLineY + 100), new Point(432, firstLineY + 100), new Point(142, secondLineY + 100), new Point(287, secondLineY + 100), new Point(432, secondLineY + 100)  } },
           { 10, new List<Point>() { new Point(142, firstLineY - 100), new Point(287, firstLineY - 100), new Point(432, firstLineY - 100), new Point(82, firstLineY + 100), new Point(197, firstLineY + 100), new Point(382, firstLineY + 100), new Point(492, firstLineY + 100), new Point(142, secondLineY + 100), new Point(287, secondLineY + 100), new Point(432, secondLineY + 100)  } },
           { 11, new List<Point>() { new Point(142, firstLineY - 100), new Point(287, firstLineY - 100), new Point(432, firstLineY - 100), new Point(82, firstLineY + 100), new Point(177, firstLineY + 100), new Point(280, firstLineY + 100), new Point(382, firstLineY + 100), new Point(492, firstLineY + 100), new Point(142, secondLineY + 100), new Point(287, secondLineY + 100), new Point(432, secondLineY + 100)  } },

        };

        internal static MemoryStream GenerateTable(dynamic stats, dynamic topScorers)
        {
            var orderedTable = TableCalculator.Create(stats);
            return DrawTable(orderedTable, topScorers);
        }

        internal static MemoryStream GenerateNormalizedTable(dynamic stats)
        {
            Dictionary<string, Score> scores = JsonSerializer.Deserialize<Dictionary<string, Score>>(stats.stats.stats.ToString(), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            return DrawTable(scores, stats.stats.scorers);
        }


        public static MemoryStream DrawTable(Dictionary<string, Score> table, dynamic topScorers)
        {
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
                Color = SKColors.White,
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

        public static MemoryStream GenerateTeamsImage(List<string> playerNames, string color)
        {
            var positions = locations[playerNames.Count];

            using (var templateStream = System.IO.File.OpenRead($@"templates/field.png"))
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
                        Color = SKColors.Black,
                        TextSize = 26,
                        IsAntialias = true,
                        TextAlign = SKTextAlign.Center,
                    };

                    
                    //float yOffset = 477; // Adjust as necessary for your design
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
                                canvas.DrawBitmap(shirtIconBitmap, currPoint.X, currPoint.Y);
                            }

                            string bidiLine = Helpers.ReverseIfNeeded(line);
                            canvas.DrawText(bidiLine, positions[index].X + 49, positions[index].Y + lineHeight + 120, paint); // Adjust position as necessary
                            lineHeight += paint.TextSize + 4;
                        }
                        index++;
                        //yOffset += paint.TextSize + 10; // Add some space between names
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
