using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TeamsDesignCreator;

namespace TeamsGeneratorWebAPI.DesignCreator
{
    public sealed class TeamShareItem
    {
        public string Name { get; set; } = string.Empty;
        public string Color { get; set; } = string.Empty;
        public List<string> Players { get; set; } = new List<string>();
    }

    public sealed class PlayerShareItem
    {
        public string Name { get; set; } = string.Empty;
        public bool IsWaiting { get; set; }
        public int? WaitingListOrder { get; set; }
    }

    public class ImageCreator
    {
        public static MemoryStream CreateTeams(List<string> players, string color)
        {
            return SkiaImageCreator.GenerateTeamsImage(players, color);
        }

        internal static MemoryStream CreatePlayersList(List<PlayerShareItem> players, string teamName, string location, string date, string dayInWeek, string currentCulture)
        {
            return SkiaImageCreator.GeneratePlayersListImageTemplate3(players, teamName, location, date, dayInWeek, currentCulture);
        }

        internal static MemoryStream CreateTeamsOverview(
            List<TeamShareItem> teams,
            string matchName,
            string location,
            string date,
            string dayInWeek,
            string currentCulture)
        {
            return SkiaImageCreator.GenerateTeamsOverviewImage(
                teams,
                matchName,
                location,
                date,
                dayInWeek,
                currentCulture);
        }

        internal static MemoryStream CreateTable(dynamic stats, dynamic topScorers, string ver)
        {
            return SkiaImageCreator.GenerateTable(stats, topScorers, ver);
        }

        internal static MemoryStream CreateNormalizedTable(dynamic stats, string ver)
        {
            return SkiaImageCreator.GenerateNormalizedTable(stats, ver);
        }
    }
}
