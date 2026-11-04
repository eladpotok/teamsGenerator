using TeamsGenerator.Orchestration.Contracts;
using TeamsGeneratorWebAPI.Clients;

namespace TeamsGeneratorWebAPI.PlayersBlob;

public class TeamsStorageWriteResponse : IResponse
{
    public bool Success { get; set; }
    public string? Revision { get; set; }
    public int WriteContractVersion => SafeWriteContract.Version;
    public string Error { get; set; } = string.Empty;
    public string? ErrorCode { get; set; }
    public bool IsConflict { get; set; }
    public bool RequiresUpgrade { get; set; }

    internal static TeamsStorageWriteResponse UpgradeRequired() => new()
    {
        RequiresUpgrade = true,
        ErrorCode = "safe_write_upgrade_required",
        Error = "Update Teamify and reload the saved teams before editing."
    };
}
