using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;

namespace TeamsGeneratorWebAPI.Telemetry;

public sealed record AccountCreationLookup(DateTimeOffset? CreatedAt, string Status);

public interface IFirebaseAccountLookup
{
    Task<AccountCreationLookup> LookupAsync(string userId, string idToken,
        DateTimeOffset now, CancellationToken cancellationToken);
}

public sealed class FirebaseAccountLookup : IFirebaseAccountLookup
{
    public const string ClientName = "FirebaseAccountLookup";
    public const string Origin = "https://identitytoolkit.googleapis.com";
    private readonly IHttpClientFactory _clients;
    private readonly IConfiguration _configuration;

    public FirebaseAccountLookup(IHttpClientFactory clients, IConfiguration configuration)
    {
        _clients = clients;
        _configuration = configuration;
    }

    public async Task<AccountCreationLookup> LookupAsync(string userId, string idToken,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        var key = _configuration["Firebase:WebApiKey"];
        if (string.IsNullOrWhiteSpace(key)) return new(null, "not_configured");
        var configuredOrigin = _configuration["Firebase:AccountsOrigin"] ?? Origin;
        if (!Uri.TryCreate(configuredOrigin, UriKind.Absolute, out var origin)
            || origin.Scheme != "https" || origin.Host != "identitytoolkit.googleapis.com"
            || !origin.IsDefaultPort || origin.AbsolutePath != "/" || origin.Query != ""
            || origin.Fragment != "" || origin.UserInfo != "")
            return new(null, "invalid_origin");
        if (string.IsNullOrWhiteSpace(idToken)) return new(null, "missing_token");

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(1));
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"{Origin}/v1/accounts:lookup?key={Uri.EscapeDataString(key)}")
        {
            Content = JsonContent.Create(new { idToken })
        };
        using var client = _clients.CreateClient(ClientName);
        using var response = await client.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, budget.Token);
        if (!response.IsSuccessStatusCode)
            return new(null, response.StatusCode switch
            {
                System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden => "provider_denied",
                System.Net.HttpStatusCode.TooManyRequests => "provider_throttled",
                _ => "provider_error"
            });
        // Bound the response before parsing; profile fields are intentionally never retained.
        await response.Content.LoadIntoBufferAsync(64 * 1024, budget.Token);
        using var document = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(budget.Token));
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("users", out var users)
            || users.ValueKind != JsonValueKind.Array || users.GetArrayLength() != 1
            || users[0].ValueKind != JsonValueKind.Object
            || !users[0].TryGetProperty("localId", out var localId)
            || localId.ValueKind != JsonValueKind.String || localId.GetString() != userId)
            return new(null, "identity_mismatch");
        if (!users[0].TryGetProperty("createdAt", out var createdAt)
            || createdAt.ValueKind != JsonValueKind.String
            || !long.TryParse(createdAt.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var milliseconds)
            || milliseconds < 0 || milliseconds > now.ToUnixTimeMilliseconds())
            return new(null, "invalid_creation_metadata");
        return new(DateTimeOffset.FromUnixTimeMilliseconds(milliseconds), "verified");
    }
}

public sealed class FirebaseAccountTelemetryRedactor : ITelemetryInitializer
{
    public void Initialize(ITelemetry telemetry)
    {
        if (telemetry is DependencyTelemetry dependency
            && (dependency.Target?.Contains("identitytoolkit.googleapis.com", StringComparison.OrdinalIgnoreCase) == true
                || dependency.Data?.Contains("identitytoolkit.googleapis.com", StringComparison.OrdinalIgnoreCase) == true))
        {
            dependency.Data = FirebaseAccountLookup.Origin + "/v1/accounts:lookup";
            dependency.Name = "POST /v1/accounts:lookup";
            dependency.Target = "identitytoolkit.googleapis.com";
            dependency.Properties.Clear();
        }
    }
}
