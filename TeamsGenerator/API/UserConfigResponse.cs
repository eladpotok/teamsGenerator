using System;
using System.Collections.Generic;
using TeamsGenerator.Orchestration;

namespace TeamsGenerator.API
{
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

        public UserConfigResponse()
        {
            ShowWhoBegins = true;
            ShowFirstGoalKeeper = true;
            ShirtsColors = new List<PlayerShirt>();
            EventDate = DateTime.UtcNow;
            EventTime = DateTime.UtcNow;
            SelectedAlgoKey = 0;
            NumberOfTeams = 3;
            TeamName = "Your Team Name";
            Location = "Your Location";
        }
    }
}
