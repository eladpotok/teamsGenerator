using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TeamsDesignCreator;

namespace TeamsGeneratorWebAPI.DesignCreator
{
    public class ImageCreator
    {
        public static MemoryStream Create(List<string> players, string color)
        {
            return SkiaImageCreator.GenerateImage(players, color);
        }
    }
}
