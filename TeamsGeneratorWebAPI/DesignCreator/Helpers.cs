using System.Globalization;

namespace TeamsGeneratorWebAPI.DesignCreator
{
    public class Helpers
    {
        public static string ReverseIfNeeded(string text)
        {
            // Check if the text is RTL using a simple check for Hebrew Unicode range
            if (text.Any(c => c >= '\u0590' && c <= '\u05FF')) // Hebrew Unicode range
            {
                return new string(text.Reverse().ToArray()); // Reverse the text
            }
            return text;
        }

        public static bool IsRightToLeft(string input)
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

        public static List<float> GetSliceCenters(float width, int slicer, float margin)
        {
            if (slicer <= 0)
                throw new ArgumentException("Slicer must be greater than 0.");

            if(slicer == 1 || slicer == 2)
            {
                var middle = width / 2;
                return new List<float>() { middle - margin, middle + margin };
            }

            if(slicer == 3)
            {
                var middle = width / 2;
                return new List<float>() { middle - margin, middle + margin };
            }

            if (slicer == 4)
            {
                var middle = width / 2;
                return new List<float>() { middle - margin, middle + margin };
            }

            return null;
        }
    }
}
