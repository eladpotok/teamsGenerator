using Microsoft.Extensions.Options;

namespace TeamsGeneratorWebAPI.Premium;

public sealed class BillingOptions
{
    public bool Enabled { get; set; }
    public bool CheckoutEnabled { get; set; }
    public bool RecoveryEnabled { get; set; }
    public string AndroidPublicApiKey { get; set; } = "";
    public string GooglePushAudience { get; set; } = "";
    public string GooglePushServiceAccount { get; set; } = "";
    public string SecretApiKey { get; set; } = "";
    public string WebhookAuthorization { get; set; } = "";
    public string AppId { get; set; } = "";
    public string Environment { get; set; } = "PRODUCTION";
    public string TableName { get; set; } = "PremiumSubscriptions";
    public bool KeepOriginalAppUserIdConfirmed { get; set; }
    public int ReconcileMinutes { get; set; } = 15;
    public int MaximumStaleHours { get; set; } = 24;
    public bool Sandbox => Environment == "SANDBOX";
}

public static class BillingRegistration
{
    public static IServiceCollection AddPremiumBilling(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<BillingOptions>().Bind(configuration.GetSection("Billing"))
            .Validate(o => !o.Enabled || (o.WebhookAuthorization.Length >= 32 && !o.WebhookAuthorization.Contains('\n')
                && !o.WebhookAuthorization.Contains('\r') && !string.IsNullOrWhiteSpace(o.AppId)
                && o.KeepOriginalAppUserIdConfirmed), "Enabled billing requires server credentials, AppId and confirmed keep-original restore policy.")
            .Validate(o => o.Environment is "PRODUCTION" or "SANDBOX", "Billing environment must be PRODUCTION or SANDBOX.")
            .Validate(o => !o.CheckoutEnabled || o.RecoveryEnabled, "Checkout requires purchase recovery.")
            .Validate(o => !o.RecoveryEnabled || (o.Enabled && o.AndroidPublicApiKey.StartsWith("goog_", StringComparison.Ordinal)
                && Uri.TryCreate(o.GooglePushAudience, UriKind.Absolute, out var audience) && audience.Scheme == "https"
                && o.GooglePushServiceAccount.EndsWith(".iam.gserviceaccount.com", StringComparison.Ordinal)),
                "Checkout requires billing, the Android public API key and authenticated Google Pub/Sub push configuration.")
            .Validate(o => o.ReconcileMinutes is >= 1 and <= 60 && o.MaximumStaleHours is >= 1 and <= 48,
                "Billing reconciliation and freshness limits are invalid.")
            .ValidateOnStart();
        services.AddHttpClient("RevenueCat", client =>
        {
            client.BaseAddress = new Uri("https://api.revenuecat.com/v1/");
            client.Timeout = TimeSpan.FromSeconds(12);
            client.MaxResponseContentBufferSize = 512 * 1024;
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddHttpClient("GooglePlay", client =>
        {
            client.BaseAddress = new Uri("https://androidpublisher.googleapis.com/androidpublisher/v3/applications/teams.generator.new/");
            client.Timeout = TimeSpan.FromSeconds(12);
            client.MaxResponseContentBufferSize = 128 * 1024;
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddSingleton<TableBillingStore>();
        services.AddSingleton<IBillingStore>(sp => sp.GetRequiredService<TableBillingStore>());
        services.AddSingleton<IPlayPurchaseStore>(sp => sp.GetRequiredService<TableBillingStore>());
        services.AddSingleton<IPlayPurchaseClient, PlayPurchaseClient>();
        services.AddSingleton<PlayPurchaseRecovery>();
        services.AddSingleton<IRevenueCatClient, RevenueCatClient>();
        services.AddSingleton<BillingService>();
        services.AddHostedService<BillingWorker>();
        return services;
    }
}

public sealed class BillingException : Exception
{
    public int Status { get; }
    public string Code { get; }
    public BillingException(int status, string code, string message) : base(message)
        => (Status, Code) = (status, code);
}
