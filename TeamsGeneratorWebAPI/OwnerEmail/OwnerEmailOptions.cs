using System.Net.Mail;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;

namespace TeamsGeneratorWebAPI.OwnerEmail;

public sealed class OwnerEmailOptions
{
    public bool Enabled { get; set; }
    public bool FeedbackEnabled { get; set; }
    public bool PaidAlertsEnabled { get; set; }
    public DateTimeOffset? PaidAlertsNotBefore { get; set; }
    public string Endpoint { get; set; } = "";
    public string SenderAddress { get; set; } = "";
    public string RecipientAddress { get; set; } = "";
    public string TableName { get; set; } = "TeamifyOwnerEmail";
    public const string OwnerName = "Teamify";

    public static bool SafeAddress(string? value) => value is { Length: > 3 and <= 254 }
        && !value.Any(char.IsControl) && MailAddress.TryCreate(value, out var address)
        && address.Address == value && address.DisplayName.Length == 0;
}

public static class OwnerEmailRegistration
{
    public static IServiceCollection AddOwnerEmail(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<OwnerEmailOptions>().Bind(configuration.GetSection("OwnerEmail"))
            .Validate(o => !o.Enabled || (Uri.TryCreate(o.Endpoint, UriKind.Absolute, out var endpoint)
                && endpoint.Scheme == "https" && endpoint.Host.EndsWith(".communication.azure.com", StringComparison.Ordinal)
                && endpoint.AbsolutePath == "/" && endpoint.Query.Length == 0 && endpoint.UserInfo.Length == 0
                && OwnerEmailOptions.SafeAddress(o.SenderAddress) && OwnerEmailOptions.SafeAddress(o.RecipientAddress)
                && o.SenderAddress != o.RecipientAddress), "Owner email requires a service endpoint and distinct valid server-only addresses.")
            .Validate(o => !o.PaidAlertsEnabled || o.PaidAlertsNotBefore.HasValue,
                "Paid alerts require an explicit activation cutoff; historical purchases are not backfilled.")
            .Validate(o => System.Text.RegularExpressions.Regex.IsMatch(o.TableName, "^[A-Za-z][A-Za-z0-9]{2,62}$"),
                "Owner email table name is invalid.")
            .ValidateOnStart();
        services.AddSingleton<IOwnerEmailStore, TableOwnerEmailStore>();
        services.AddSingleton<OwnerEmailService>();
        services.AddSingleton<OwnerPaidAlertService>();
        services.AddSingleton<TokenCredential>(_ => new ManagedIdentityCredential());
        services.AddSingleton<IOwnerEmailTransport, AcsOwnerEmailTransport>();
        services.AddSingleton<OwnerEmailProcessor>();
        services.AddHostedService<OwnerEmailWorker>();
        services.AddHttpClient("OwnerEmail", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(20);
            client.MaxResponseContentBufferSize = 16 * 1024;
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        return services;
    }
}

public sealed class OwnerEmailException(int status, string code, int? retryAfter = null, bool safeToRetry = false) : Exception(code)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
    public int? RetryAfter { get; } = retryAfter;
    public bool SafeToRetry { get; } = safeToRetry;
}
