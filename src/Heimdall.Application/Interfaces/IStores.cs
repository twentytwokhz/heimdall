namespace Heimdall.Application;

/// <summary>Injectable clock so time-based policies are deterministic in tests.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

/// <summary>Resolves named values referenced as <c>{{name}}</c> in policies.</summary>
public interface INamedValues
{
    string Resolve(string name);
    bool TryResolve(string name, out string value);
}

/// <summary>The state of a rate/quota counter after an increment.</summary>
public readonly record struct CounterState(long Count, DateTimeOffset ResetsAt);

/// <summary>
/// Epoch-aligned fixed-window counters backing the rate-limit and quota policies. Windows align to
/// the clock (floor(now / window) * window): a documented divergence from APIM's first-call anchor,
/// chosen so windows are deterministic under a fake clock.
/// </summary>
public interface ICounterStore
{
    /// <summary>Increments the counter for <paramref name="key"/> in the current window; returns the post-increment count and reset time.</summary>
    CounterState Increment(string key, TimeSpan window);
}

/// <summary>A cached HTTP response snapshot for cache-lookup / cache-store.</summary>
public sealed record CachedResponse(int StatusCode, IReadOnlyDictionary<string, string[]> Headers, string? Body);

/// <summary>
/// In-memory cache backing the cache-lookup/cache-store (responses) and cache-lookup-value/
/// cache-store-value (arbitrary values) policies. Entries expire after their stored duration.
/// </summary>
public interface ICacheStore
{
    bool TryGet(string key, out object? value);
    void Set(string key, object? value, TimeSpan duration);
}
