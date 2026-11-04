using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;

namespace TeamsGeneratorWebAPI.OwnerEmail;

public sealed record OwnerEmailTransportResult(string Status);
public interface IOwnerEmailTransport
{
    Task<OwnerEmailTransportResult> PollAsync(string operationId, CancellationToken ct);
    Task<OwnerEmailTransportResult> SendAsync(OwnerEmailItem item, CancellationToken ct);
}

public sealed class AcsOwnerEmailTransport(IHttpClientFactory clients, TokenCredential credential,
    IOptions<OwnerEmailOptions> options) : IOwnerEmailTransport
{
    public Task<OwnerEmailTransportResult> PollAsync(string operationId, CancellationToken ct)
        => RequestAsync(HttpMethod.Get, "emails/operations/" + Guid.Parse(operationId).ToString(), null, ct);

    public Task<OwnerEmailTransportResult> SendAsync(OwnerEmailItem item, CancellationToken ct)
        => RequestAsync(HttpMethod.Post, "emails:send", item, ct);

    private async Task<OwnerEmailTransportResult> RequestAsync(HttpMethod method, string path, OwnerEmailItem? item, CancellationToken ct)
    {
        try
        {
            var token = await credential.GetTokenAsync(new TokenRequestContext(["https://communication.azure.com/.default"]), ct);
            // Root the action path so the colon in emails:send cannot become a URI scheme.
            using var request = new HttpRequestMessage(method, new Uri(new Uri(options.Value.Endpoint), "/" + path + "?api-version=2023-03-31"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
            if (item != null)
            {
                request.Headers.Add("Operation-Id", item.OperationId);
                request.Content = JsonContent.Create(new
                {
                    senderAddress = options.Value.SenderAddress,
                    recipients = new { to = new[] { new { address = options.Value.RecipientAddress, displayName = OwnerEmailOptions.OwnerName } } },
                    content = new { subject = item.Subject, plainText = item.Body },
                    userEngagementTrackingDisabled = true
                });
            }
            using var response = await clients.CreateClient("OwnerEmail").SendAsync(request, ct);
            if (method == HttpMethod.Get && response.StatusCode == HttpStatusCode.NotFound) return new("missing");
            if (response.StatusCode == HttpStatusCode.TooManyRequests) return new("throttled");
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.BadRequest)
                throw new OwnerEmailException(503, "email_provider_rejected", safeToRetry: true);
            if (!response.IsSuccessStatusCode) throw new OwnerEmailException(503, "email_provider_unavailable");
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var status = json.RootElement.GetProperty("status").GetString();
            return status switch
            {
                "Succeeded" => new("succeeded"),
                "Running" or "NotStarted" => new("running"),
                "Failed" or "Canceled" => new("failed"),
                _ => throw new OwnerEmailException(503, "email_provider_response")
            };
        }
        catch (AuthenticationFailedException)
        {
            throw new OwnerEmailException(503, "email_credentials_unavailable", safeToRetry: true);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or KeyNotFoundException)
        {
            // Provider errors can contain recipient addresses; never retain or log them.
            throw new OwnerEmailException(503, "email_provider_unavailable");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new OwnerEmailException(503, "email_provider_timeout");
        }
    }
}
