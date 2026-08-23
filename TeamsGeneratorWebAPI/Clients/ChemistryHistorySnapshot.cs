namespace TeamsGeneratorWebAPI.Clients
{
    internal class ChemistryHistorySnapshot
    {
        internal int MatchdayCount { get; set; }
        internal IReadOnlyDictionary<string, double> Scores { get; set; } =
            new Dictionary<string, double>();
    }
}
