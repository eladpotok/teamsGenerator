using SkiaSharp;
using System;
using System.Collections.Generic;
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

        public static MemoryStream GenerateImage(List<string> playerNames, string color)
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

        public static List<string> WrapText(SKPaint paint, string text, float maxWidth)
        {
            var words = text.Split(' ');
            var lines = new List<string>();
            string currentLine = "";

            foreach (var word in words)
            {
                string testLine = currentLine + (currentLine.Length > 0 ? " " : "") + word;
                float lineWidth = paint.MeasureText(testLine);  // Measure the width of the test line

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
