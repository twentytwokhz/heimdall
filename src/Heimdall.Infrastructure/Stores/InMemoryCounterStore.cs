using System.Collections.Concurrent;
using Heimdall.Application;

namespace Heimdall.Infrastructure.Stores;

/// <summary>
/// Thread-safe in-memory <see cref="ICounterStore"/> with epoch-aligned fixed windows, driven by
/// <see cref="IClock"/>. A counter resets when the clock crosses into a new window.
/// </summary>
public sealed class InMemoryCounterStore(IClock clock) : ICounterStore
{
    private sealed record Entry(long WindowStartTicks, long Count);

    private readonly ConcurrentDictionary<string, Entry> _counters = new(StringComparer.Ordinal);

    public CounterState Increment(string key, TimeSpan window)
    {
        var windowTicks = Math.Max(1, window.Ticks);
        var windowStartTicks = clock.UtcNow.UtcTicks / windowTicks * windowTicks;

        var entry = _counters.AddOrUpdate(
            key,
            _ => new Entry(windowStartTicks, 1),
            (_, existing) => existing.WindowStartTicks == windowStartTicks
                ? existing with { Count = existing.Count + 1 }
                : new Entry(windowStartTicks, 1));

        return new CounterState(entry.Count, new DateTimeOffset(windowStartTicks, TimeSpan.Zero) + window);
    }
}
