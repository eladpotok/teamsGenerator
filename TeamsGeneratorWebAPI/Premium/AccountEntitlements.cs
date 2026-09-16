namespace TeamsGeneratorWebAPI.Premium;

public sealed class AccountEntitlements
{
    public bool IsPremium { get; init; }

    public bool CanUseAiSummary => IsPremium;

    public bool CanUseChemistry => IsPremium;

    public bool CanUseAiAlgorithm => IsPremium;

    public bool CanUseCustomGroupLogo => IsPremium;

    public bool CanUsePremiumShareTemplates => IsPremium;

    public int MaximumPlayers => IsPremium ? 0 : 25;

    public int MaximumOwnedGroups => IsPremium ? 0 : 2;
}

public interface IAccountEntitlementService
{
    Task<AccountEntitlements> GetAsync(
        string userId,
        string? verifiedEmail = null,
        CancellationToken cancellationToken = default);
}

public sealed class AccountEntitlementService : IAccountEntitlementService
{
    private readonly IConfiguration _configuration;
    private readonly IPremiumAccountStore _premiumAccounts;

    public AccountEntitlementService(
        IConfiguration configuration,
        IPremiumAccountStore premiumAccounts)
    {
        _configuration = configuration;
        _premiumAccounts = premiumAccounts;
    }

    public async Task<AccountEntitlements> GetAsync(
        string userId,
        string? verifiedEmail = null,
        CancellationToken cancellationToken = default)
    {
        var enforcementEnabled = _configuration.GetValue(
            "Premium:EnforcementEnabled",
            true);
        var isPremium = enforcementEnabled
            ? await _premiumAccounts.IsPremiumAsync(
                userId,
                verifiedEmail,
                cancellationToken)
            : _configuration.GetValue(
                "Premium:DefaultEntitled",
                true);

        return new AccountEntitlements { IsPremium = isPremium };
    }
}
