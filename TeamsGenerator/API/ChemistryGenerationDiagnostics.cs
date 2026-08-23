using System.Collections.Generic;

namespace TeamsGenerator.API
{
    public class ChemistryGenerationDiagnostics
    {
        public bool Requested { get; set; }
        public bool Applied { get; set; }
        public int HistoryMatchdayCount { get; set; }
        public int PartnershipCount { get; set; }
        public int SwapCount { get; set; }
        public double BalanceImprovementPercent { get; set; }
        public List<ChemistryPartnership> NotablePartnerships { get; set; } =
            new List<ChemistryPartnership>();
        public List<ChemistryTeamRating> TeamRatings { get; set; } =
            new List<ChemistryTeamRating>();
    }

    public class ChemistryPartnership
    {
        public string FirstPlayerName { get; set; }
        public string SecondPlayerName { get; set; }
    }

    public class ChemistryTeamRating
    {
        public int TeamIndex { get; set; }
        public int Score { get; set; }
        public int EvidenceCount { get; set; }
        public List<ChemistryPlayerLink> PlayerLinks { get; set; } =
            new List<ChemistryPlayerLink>();
    }

    public class ChemistryPlayerLink
    {
        public string PlayerKey { get; set; }
        public List<string> PartnerNames { get; set; } =
            new List<string>();
    }
}
