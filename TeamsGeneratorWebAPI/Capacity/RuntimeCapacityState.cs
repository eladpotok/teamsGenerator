using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.Data.Tables;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;

namespace TeamsGeneratorWebAPI.Capacity;

public interface IRuntimeClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class RuntimeClock : IRuntimeClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public interface IRuntimeLease : IAsyncDisposable
{
    CancellationToken LeaseLost { get; }
}

public interface IRuntimeCapacityState
{
    Task ConsumeAsync(string key, int limit, string code, CancellationToken cancellationToken);
    Task<IRuntimeLease> AcquireAsync(string key, int slots, string code, CancellationToken cancellationToken);
}

public sealed class RuntimeRateEntity : ITableEntity
{
    public string PartitionKey { get; set; } = "runtime-v1";
    public string RowKey { get; set; } = "";
    public string Requests { get; set; } = "[]";
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
}

// All workers must use the same storage account. There is deliberately no local-only fallback.
public sealed class RuntimeCapacityState : IRuntimeCapacityState
{
    private readonly TableClient _table;
    private readonly BlobContainerClient _container;
    private readonly IRuntimeClock _clock;
    private readonly ILogger<RuntimeCapacityState> _logger;
    private volatile bool _tableReady;
    private volatile bool _containerReady;

    public RuntimeCapacityState(TableClient table, BlobContainerClient container,
        IRuntimeClock clock, ILogger<RuntimeCapacityState> logger)
    {
        _table = table;
        _container = container;
        _clock = clock;
        _logger = logger;
    }

    public async Task ConsumeAsync(string key, int limit, string code, CancellationToken cancellationToken)
    {
        if (!_tableReady)
        {
            await _table.CreateIfNotExistsAsync(cancellationToken);
            _tableReady = true;
        }
        var rowKey = Hash(key);
        for (var attempt = 0; attempt < 12; attempt++)
        {
            var existing = await _table.GetEntityIfExistsAsync<RuntimeRateEntity>(
                "runtime-v1", rowKey, cancellationToken: cancellationToken);
            var entity = existing.HasValue ? existing.Value : new RuntimeRateEntity { RowKey = rowKey };
            var now = _clock.UtcNow.ToUnixTimeMilliseconds();
            var requests = JsonSerializer.Deserialize<List<long>>(entity.Requests)!
                .Where(time => time > now - 60_000).OrderBy(time => time).ToList();
            if (requests.Count >= limit)
                throw new CapacityException(code, "Too many requests. Please wait and try again.",
                    limit: limit, current: requests.Count,
                    retryAfterSeconds: Math.Max(1, (int)Math.Ceiling((requests[0] + 60_000 - now) / 1000d)));
            requests.Add(now);
            entity.Requests = JsonSerializer.Serialize(requests);
            try
            {
                if (existing.HasValue)
                    await _table.UpdateEntityAsync(entity, entity.ETag, TableUpdateMode.Replace, cancellationToken);
                else
                    await _table.AddEntityAsync(entity, cancellationToken);
                return;
            }
            catch (RequestFailedException exception) when (exception.Status is 409 or 412)
            {
                // A competing worker consumed this revision. Re-read before admitting anything.
                await Task.Delay(10 + Random.Shared.Next(20), cancellationToken);
            }
        }
        throw new CapacityException("capacity_busy", "Capacity accounting is busy. Please retry.",
            retryAfterSeconds: 1);
    }

    public async Task<IRuntimeLease> AcquireAsync(
        string key, int slots, string code, CancellationToken cancellationToken)
    {
        if (!_containerReady)
        {
            await _container.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
            _containerReady = true;
        }
        for (var slot = 0; slot < slots; slot++)
        {
            var blob = _container.GetBlobClient($"{Hash(key)}-{slot}");
            try
            {
                await blob.UploadAsync(BinaryData.FromString("capacity-v1"),
                    new BlobUploadOptions { Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All } },
                    cancellationToken);
            }
            catch (RequestFailedException exception) when (exception.ErrorCode is "BlobAlreadyExists"
                or "ConditionNotMet" or "LeaseIdMissing")
            {
                // An existing (possibly leased) slot must never be overwritten.
            }
            var lease = blob.GetBlobLeaseClient();
            try
            {
                await lease.AcquireAsync(TimeSpan.FromSeconds(60), cancellationToken: cancellationToken);
                return new RenewingLease(lease, _logger);
            }
            catch (RequestFailedException exception) when (exception.ErrorCode == "LeaseAlreadyPresent")
            {
                // Try the next distributed slot, not a process-local semaphore.
            }
        }
        throw new CapacityException(code, "An operation is already running. Please wait and try again.",
            limit: slots, current: slots, retryAfterSeconds: 2);
    }

    public static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed class RenewingLease : IRuntimeLease
    {
        private readonly BlobLeaseClient _lease;
        private readonly ILogger _logger;
        private readonly CancellationTokenSource _stop = new();
        private readonly CancellationTokenSource _lost = new();
        private readonly Task _renewal;
        public CancellationToken LeaseLost => _lost.Token;

        public RenewingLease(BlobLeaseClient lease, ILogger logger)
        {
            _lease = lease;
            _logger = logger;
            _renewal = RenewAsync();
        }

        private async Task RenewAsync()
        {
            try
            {
                while (true)
                {
                    await Task.Delay(TimeSpan.FromSeconds(10), _stop.Token);
                    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                    deadline.CancelAfter(TimeSpan.FromSeconds(10));
                    await _lease.RenewAsync(cancellationToken: deadline.Token);
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
            catch (Exception exception) when (exception is RequestFailedException or OperationCanceledException)
            {
                _logger.LogError(exception, "Runtime capacity lease renewal failed; canceling protected work.");
                _lost.Cancel();
            }
        }

        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            await _renewal;
            try
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await _lease.ReleaseAsync(cancellationToken: deadline.Token);
            }
            catch (RequestFailedException exception) when (exception.ErrorCode is "LeaseNotPresentWithLeaseOperation"
                or "LeaseIdMismatchWithLeaseOperation")
            {
                _logger.LogWarning(exception, "Runtime capacity lease was already lost when work completed.");
            }
            finally
            {
                _stop.Dispose();
                _lost.Dispose();
            }
        }
    }
}
