using System;
using System.Collections.Generic;
using System.Linq;

namespace TeamsGenerator.Orchestration
{
    public static class ShirtColorManager
    {
        public static List<ShirtColor> GetAllShirtColors()
        {
            return Enum.GetValues(typeof(ShirtColor)).Cast<ShirtColor>().ToList();
        }
    }
}
