using System.Collections.Concurrent;
using Heimdall.Application;

namespace Heimdall.Infrastructure.Stores;

/// <summary>Thread-safe in-memory <see cref="ICacheStore"/>; entries expire after their duration (IClock-driven).</summary>
public sealed class InMemoryCacheStore(IClock clock) : ICacheStore
{
    private sealed record Entry(object? Value, DateTimeOffset ExpiresAt);

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public bool TryGet(string key, out object? value)
    {
        if (_entries.TryGetValue(key, out var entry) && entry.ExpiresAt > clock.UtcNow)
        {
            value = entry.Value;
            return true;
        }

        // Drop the stale entry so the cache does not grow unbounded. The value-matching overload avoids
        // deleting a fresher entry written concurrently by another thread after we read this one.
        if (entry is not null)
        {
            _entries.TryRemove(new KeyValuePair<string, Entry>(key, entry));
        }

        value = null;
        return false;
    }

    public void Set(string key, object? value, TimeSpan duration) =>
        _entries[key] = new Entry(value, clock.UtcNow + duration);
}
