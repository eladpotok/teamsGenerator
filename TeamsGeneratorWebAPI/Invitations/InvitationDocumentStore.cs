using System.Security.Cryptography;
using System.Text;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Newtonsoft.Json;

namespace TeamsGeneratorWebAPI.Invitations;

public record StoredDocument<T>(T Value, string ETag);
public record DocumentPage(IReadOnlyList<string> Keys, string? Continuation);
public interface IInvitationDocumentStore
{
    Task<StoredDocument<T>?> ReadAsync<T>(string key, CancellationToken cancellationToken);
    Task<bool> WriteAsync<T>(string key, T value, string? etag, CancellationToken cancellationToken);
    Task<DocumentPage> ListAsync(string prefix, string? continuation, int pageSize, CancellationToken cancellationToken);
}

public sealed class BlobInvitationDocumentStore : IInvitationDocumentStore
{
    private readonly BlobContainerClient _container;
    public BlobInvitationDocumentStore(IConfiguration configuration)
    {
        _container = new BlobContainerClient(configuration["BlobConnectionString"],
            configuration["PlayersBlobContainerName"]);
    }

    public async Task<StoredDocument<T>?> ReadAsync<T>(string key, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _container.GetBlobClient(key).DownloadContentAsync(cancellationToken);
            var value = JsonConvert.DeserializeObject<T>(result.Value.Content.ToString())
                ?? throw new InvalidDataException("Invalid invitation storage document.");
            return new(value, result.Value.Details.ETag.ToString());
        }
        catch (RequestFailedException ex) when (ex.Status == 404 && ex.ErrorCode == "BlobNotFound")
        {
            return null;
        }
    }

    public async Task<bool> WriteAsync<T>(string key, T value, string? etag, CancellationToken cancellationToken)
    {
        try
        {
            await _container.GetBlobClient(key).UploadAsync(
                BinaryData.FromString(JsonConvert.SerializeObject(value)),
                new BlobUploadOptions
                {
                    Conditions = etag == null
                        ? new BlobRequestConditions { IfNoneMatch = ETag.All }
                        : new BlobRequestConditions { IfMatch = new ETag(etag) }
                }, cancellationToken);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status is 409 or 412)
        {
            return false;
        }
    }

    public async Task<DocumentPage> ListAsync(string prefix, string? continuation, int pageSize,
        CancellationToken cancellationToken)
    {
        await foreach (var page in _container.GetBlobsAsync(prefix: prefix, cancellationToken: cancellationToken)
            .AsPages(continuation, pageSize))
            return new(page.Values.Select(item => item.Name).ToArray(), page.ContinuationToken);
        return new(Array.Empty<string>(), null);
    }
}

public static class InvitationKeys
{
    public const string StatePrefix = "match-invitations/v1/state/";
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    public static string Id(string owner, string group, DateTimeOffset at) =>
        Hash(JsonConvert.SerializeObject(new[] { owner, group, at.UtcDateTime.ToString("O") }));
    public static string State(string id) => StatePrefix + id;
    public static string Token(string token) => "match-invitations/v1/capabilities/" + Hash(token);
    public static string Account(string owner) => "match-invitations/v1/accounts/" + Hash(owner);
    public static string DeviceOwner(string tokenHash) => "match-invitations/v1/device-owners/" + tokenHash;
}
