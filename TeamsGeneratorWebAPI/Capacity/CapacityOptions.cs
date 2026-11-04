namespace TeamsGeneratorWebAPI.Capacity;

public sealed class CapacityOptions
{
    public int MaximumRenderingEntries { get; set; } = 100;
    public int FreeGenerationPlayers { get; set; } = 25;
    public int PremiumGenerationPlayers { get; set; } = 50;
    public int GenerationRequestsPerMinute { get; set; } = 20;
    public int ConcurrentGenerationsPerCaller { get; set; } = 1;
    public int GlobalExpensiveConcurrency { get; set; } = 2;
    public int GlobalGenerationRequestsPerMinute { get; set; } = 120;
    public int WritesPerMinute { get; set; } = 60;
    public int MaximumRequestBytes { get; set; } = 2 * 1024 * 1024;
    public int MaximumPlayerNameLength { get; set; } = 100;
    public int MaximumPlayerKeyLength { get; set; } = 256;
    public int MaximumGenerationTeams { get; set; } = 10;
    public int OperationTimeoutSeconds { get; set; } = 30;
}
