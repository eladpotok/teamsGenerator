using System;
using System.Collections.Generic;
using TeamsGenerator.Orchestration;

namespace TeamsGenerator.API
{
    public class Lang
    {
        public string Value { get; set; }
        public string Label { get; set; }
    }

    public class UserConfigResponse
    {
        public int NumberOfTeams { get; set; }
        public List<PlayerShirt> ShirtsColors { get; set; }
        public bool ShowWhoBegins { get; set; }
        public bool ShowFirstGoalKeeper { get; set; }
        public DateTime EventDate { get; set; }
        public DateTime EventTime { get; set; }
        public int SelectedAlgoKey { get; set; }
        public string TeamName { get; set; }
        public string Location { get; set; }
        public bool AllowOnlineScoreboard { get; set; }
        public string CurrentVersion { get; set; }
        public int MatchTimeMinutes { get; set; }
        public int ExtraTimeMinutes { get; set; }
        public bool EnableTimer { get; set; }
        public string ScheduleType { get; set; }
        public string RepeatDay { get; set; }
        public string RepeatTime { get; set; }
        public string Language { get; set; }
        public List<Lang> AvailableLanguages { get; set; }


        public UserConfigResponse()
        {
            ShowWhoBegins = true;
            ShowFirstGoalKeeper = true;
            ShirtsColors = new List<PlayerShirt>();
            EventDate = DateTime.UtcNow;
            EventTime = DateTime.UtcNow;
            SelectedAlgoKey = 0;
            NumberOfTeams = 3;
            TeamName = "";
            Location = "";
            MatchTimeMinutes = 8;
            ExtraTimeMinutes = 2;
            EnableTimer = false;
            ScheduleType = "repeating";
            RepeatDay = "sunday";
            RepeatTime = "12:00";
            AvailableLanguages = new List<Lang>()
            {
                new Lang() { Value ="en", Label="English" },
                new Lang() { Value ="he", Label="עברית (Hebrew)" }
            };
            Language = AvailableLanguages[0].Value;
        }
    }
}
