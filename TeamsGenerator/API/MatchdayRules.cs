namespace TeamsGenerator.API
{
    public class MatchdayRules
    {
        public string Language { get; set; } = "en";
        public bool TimeLimitEnabled { get; set; }
        public int Minutes { get; set; } = 8;
        public bool GoalLimitEnabled { get; set; }
        public int Goals { get; set; } = 2;
        public bool ExtraTimeEnabled { get; set; }
        public int ExtraMinutes { get; set; } = 2;
        public bool GoldenGoal { get; set; }
        public string EndMode { get; set; } = "on-time";
        public bool EndOnCorner { get; set; }
        public bool EndOnBallOut { get; set; }
        public bool EndOnGoalkeeperHold { get; set; }
        public bool EndOnOther { get; set; }
        public string EndOtherText { get; set; } = "";
        public string TieResolution { get; set; } = "none";
        public bool NoSlideTackles { get; set; }
        public bool NoDangerousPlay { get; set; }
        public bool RotateGoalkeepers { get; set; }
        public bool RespectDecisions { get; set; }
        public string CustomText { get; set; } = "";

        public bool TryNormalize(out string error)
        {
            error = null;
            var extraTimeEnabled = TimeLimitEnabled && ExtraTimeEnabled;
            if (Language != "en" && Language != "he")
            {
                error = "Matchday rules language must be en or he.";
            }
            else if (TieResolution != "none"
                && TieResolution != "penalties"
                && TieResolution != "longest-playing-off")
            {
                error = "Choose a supported matchday rules tie resolution.";
            }
            else if (TimeLimitEnabled && (Minutes < 1 || Minutes > 120))
            {
                error = "Matchday rules minutes must be between 1 and 120.";
            }
            else if (EndMode != "on-time" && EndMode != "stoppage")
            {
                error = "Choose a supported end-of-time rule.";
            }
            else if (EndOtherText == null || EndOtherText.Length > 300
                || (TimeLimitEnabled && EndMode == "stoppage" && EndOnOther
                    && string.IsNullOrWhiteSpace(EndOtherText)))
            {
                error = "Describe the other end condition using up to 300 characters.";
            }
            else if (TimeLimitEnabled && EndMode == "stoppage"
                && !EndOnCorner && !EndOnBallOut && !EndOnGoalkeeperHold && !EndOnOther)
            {
                error = "Select at least one end condition.";
            }
            else if (GoalLimitEnabled && (Goals < 1 || Goals > 50))
            {
                error = "Matchday rules goals must be between 1 and 50.";
            }
            else if (extraTimeEnabled
                && (ExtraMinutes < 1 || ExtraMinutes > 30))
            {
                error = "Matchday rules extra minutes must be between 1 and 30.";
            }
            else if (CustomText == null || CustomText.Length > 1200
                || CustomText.Split(new[] { '\r', '\n' })
                    .Count(line => !string.IsNullOrWhiteSpace(line)) > 20)
            {
                error = "Matchday rules custom text must contain at most 1200 characters and 20 nonempty lines.";
            }

            if (error != null)
            {
                return false;
            }

            // These describe the rules only; operational timer settings are separate.
            ExtraTimeEnabled = extraTimeEnabled;
            GoldenGoal = extraTimeEnabled && GoldenGoal;
            if (!TimeLimitEnabled) EndMode = "on-time";
            return true;
        }

        public MatchdayRules Clone() => (MatchdayRules)MemberwiseClone();
    }
}
