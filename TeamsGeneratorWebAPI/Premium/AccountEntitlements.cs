namespace TeamsGeneratorWebAPI.Premium;

public sealed class AccountEntitlements
{
    public bool IsPremium { get; init; }

    public bool CanUseAiSummary => IsPremium;

    public bool CanUseChemistry => IsPremium;

    public bool CanUseAiAlgorithm => IsPremium;

    public int MaximumPlayers => IsPremium ? 0 : 25;
}

public interface IAccountEntitlementService
{
    AccountEntitlements Get(string userId);
}

public sealed class AccountEntitlementService : IAccountEntitlementService
{
    private readonly IConfiguration _configuration;

    public AccountEntitlementService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public AccountEntitlements Get(string userId)
    {
        // This default keeps the launch behavior unchanged. A future billing
        // provider only needs to replace this service's entitlement lookup.
        var isPremium = _configuration.GetValue(
            "Premium:DefaultEntitled",
            true);
        return new AccountEntitlements { IsPremium = isPremium };
    }
}
