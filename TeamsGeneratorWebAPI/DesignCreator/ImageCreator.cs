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
        public static MemoryStream CreateTeams(List<string> players, string color)
        {
            return SkiaImageCreator.GenerateTeamsImage(players, color);
        }

        internal static object CreatePlayersList(List<string> players, string teamName, string location, string date, string dayInWeek, string currentCulture)
        {
            return SkiaImageCreator.GeneratePlayersListImage(players, teamName.ToUpper(), location.ToUpper(), date, dayInWeek.ToUpper(), currentCulture);
        }

        internal static MemoryStream CreateTable(dynamic stats, dynamic topScorers)
        {
            return SkiaImageCreator.GenerateTable(stats, topScorers);
        }

        internal static MemoryStream CreateNormalizedTable(dynamic stats)
        {
            return SkiaImageCreator.GenerateNormalizedTable(stats);
        }
    }
}
