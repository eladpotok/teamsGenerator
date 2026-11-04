using Microsoft.Extensions.Options;
using TeamsGeneratorWebAPI.Capacity;

namespace TeamsGeneratorWebAPI.Premium;

public sealed class AccountEntitlements
{
    public bool IsPremium { get; init; }
    internal CapacityOptions Capacity { get; init; } = new();

    public bool CanUseAiSummary => IsPremium;

    public bool CanUseChemistry => IsPremium;

    public bool CanUseAiAlgorithm => IsPremium;

    public bool CanUseCustomGroupLogo => IsPremium;

    public bool CanUsePremiumShareTemplates => IsPremium;

    public bool CanUseMatchInvitations => true;

    public int MaximumPlayers => IsPremium ? 0 : 25;

    public int MaximumOwnedGroups => IsPremium ? 0 : 2;

    public int MaximumGenerationPlayers => IsPremium
        ? Capacity.PremiumGenerationPlayers : Capacity.FreeGenerationPlayers;

    public int GenerationRequestsPerMinute => Capacity.GenerationRequestsPerMinute;

    public int MaximumConcurrentGenerations => Capacity.ConcurrentGenerationsPerCaller;
}

public interface IAccountEntitlementService
{
    Task<AccountEntitlements> GetAsync(
        string userId,
        string? verifiedEmail = null,
        CancellationToken cancellationToken = default,
        bool? paidActive = null);
}

public sealed class AccountEntitlementService : IAccountEntitlementService
{
    private readonly IConfiguration _configuration;
    private readonly IPremiumAccountStore _premiumAccounts;
    private readonly CapacityOptions _capacity;
    private readonly BillingService? _billing;

    public AccountEntitlementService(
        IConfiguration configuration,
        IPremiumAccountStore premiumAccounts,
        IOptions<CapacityOptions>? capacity = null,
        BillingService? billing = null)
    {
        _configuration = configuration;
        _premiumAccounts = premiumAccounts;
        _billing = billing;
        _capacity = capacity?.Value
            ?? configuration.GetSection("Capacity").Get<CapacityOptions>()
            ?? new CapacityOptions();
    }

    public async Task<AccountEntitlements> GetAsync(
        string userId,
        string? verifiedEmail = null,
        CancellationToken cancellationToken = default,
        bool? paidActive = null)
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

        if (!isPremium)
            isPremium = paidActive ?? (_billing?.Enabled == true
                && _billing.IsActive(await _billing.ReadAsync(userId, cancellationToken), DateTimeOffset.UtcNow));
        return new AccountEntitlements { IsPremium = isPremium, Capacity = _capacity };
    }
}
