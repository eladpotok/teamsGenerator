using Microsoft.Extensions.Caching.Memory;

namespace TeamsGeneratorWebAPI.Telemetry;

public sealed class WebsiteVisitRateLimiter : IDisposable
{
    private sealed class Counter { public int Count; }
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 20000 });
    private readonly object _gate = new();

    public bool Allow(string? address)
    {
        lock (_gate)
        {
            return Increment("global", 6000)
                && Increment(UsageMetrics.ResourceKey("website-rate", address ?? "unknown"), 120);
        }
    }

    private bool Increment(string key, int limit)
    {
        if (!_cache.TryGetValue(key, out Counter? counter))
        {
            counter = new Counter();
            _cache.Set(key, counter, new MemoryCacheEntryOptions
                { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1), Size = 1 });
        }
        return ++counter!.Count <= limit;
    }

    public static bool IsAutomated(string? agent) =>
        string.IsNullOrWhiteSpace(agent)
        || new[] { "bot", "crawler", "spider", "headless", "curl/", "python-", "powershell" }
            .Any(value => agent.Contains(value, StringComparison.OrdinalIgnoreCase));

    public void Dispose() => _cache.Dispose();
}
