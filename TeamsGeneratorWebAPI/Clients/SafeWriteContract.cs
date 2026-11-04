namespace TeamsGeneratorWebAPI.Clients;

public static class SafeWriteContract
{
    public const int Version = 2;
    public const string AbsentRevision = "absent";

    public static object UpgradeRequired() => new
    {
        Success = false,
        RequiresUpgrade = true,
        ErrorCode = "safe_write_upgrade_required",
        WriteContractVersion = Version,
        Message = "Update Teamify and reload this data before editing. This version cannot safely save shared changes."
    };
}
