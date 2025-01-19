using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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

        private static void DrawText(float x, float y, SKPaint paint, SKCanvas canvas, string text)
        {
            canvas.DrawText(text, x, y, paint);
        }

        internal static object GeneratePlayersListImage(List<string> players, string teamName, string location, string date, string dayInWeek)
        {
            var sidePadding = 10;
            var topPadding = 30;
            var culture = CultureInfo.CurrentCulture;

            var dateTime = DateTime.Parse(date);
            var dateTimeDisplay = dateTime.ToString("g", culture);

            var dayOfWeekCurrentCulture = ReverseIfNeeded(culture.DateTimeFormat.GetDayName(dateTime.DayOfWeek));
            var dayInWeekDisplay = dayOfWeekCurrentCulture.ToUpper() == dayInWeek.ToUpper() ? dayInWeek.ToUpper() : $"{dayInWeek} | {dayOfWeekCurrentCulture}";

            teamName = ReverseIfNeeded(teamName);
            location = ReverseIfNeeded(location);

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

                    var isRtl = IsRightToLeft(teamName);
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
                            string bidiLine = ReverseIfNeeded(firstName.ToUpper());
                            canvas.DrawText(bidiLine, currShirtX + shirtIconBitmap.Width / 2, offsetY + shirtIconBitmap.Height + 15, firstNamePaint);

                            string bidiLine2 = ReverseIfNeeded(lastNames.ToUpper());
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

                            string bidiLine = ReverseIfNeeded(line);
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

        private static string ReverseIfNeeded(string text)
        {
            // Check if the text is RTL using a simple check for Hebrew Unicode range
            if (text.Any(c => c >= '\u0590' && c <= '\u05FF')) // Hebrew Unicode range
            {
                return new string(text.Reverse().ToArray()); // Reverse the text
            }
            return text;
        }

        private static bool IsRightToLeft(string input)
        {
            foreach (char c in input)
            {
                var category = CharUnicodeInfo.GetUnicodeCategory(c);

                // Check if the character is from an RTL script
                if (category == UnicodeCategory.OtherLetter ||
                    category == UnicodeCategory.LetterNumber ||
                    category == UnicodeCategory.NonSpacingMark)
                {
                    // Check specific ranges for RTL languages (Hebrew, Arabic, etc.)
                    if ((c >= '\u0590' && c <= '\u08FF') || // Hebrew, Arabic, Syriac, etc.
                        (c >= '\uFB1D' && c <= '\uFEFC'))   // RTL Presentation Forms
                    {
                        return true;
                    }
                }
            }

            return false;
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
