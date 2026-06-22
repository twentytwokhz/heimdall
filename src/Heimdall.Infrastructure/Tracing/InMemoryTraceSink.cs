using Heimdall.Application;

namespace Heimdall.Infrastructure.Tracing;

/// <summary>
/// Thread-safe, capacity-bounded ring buffer of recent request traces. The newest trace evicts the
/// oldest once the buffer is full, so memory stays bounded for a long-running local gateway.
/// A single lock is enough: traces are small, writes are one-per-request, and reads are console-rate.
/// </summary>
public sealed class InMemoryTraceSink : ITraceSink
{
    private readonly int _capacity;
    private readonly Queue<RequestTrace> _traces;
    private readonly Lock _gate = new();

    public InMemoryTraceSink(int capacity = 256)
    {
        if (capacity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Trace buffer capacity must be at least 1.");
        }

        _capacity = capacity;
        _traces = new Queue<RequestTrace>(capacity);
    }

    public event Action<RequestTrace>? TraceAdded;

    public void Add(RequestTrace trace)
    {
        lock (_gate)
        {
            _traces.Enqueue(trace);
            if (_traces.Count > _capacity)
            {
                _traces.Dequeue();
            }
        }

        // Raised outside the lock: handlers (the SignalR bridge) run without holding it and cannot deadlock.
        TraceAdded?.Invoke(trace);
    }

    public IReadOnlyList<RequestTrace> Recent(int limit)
    {
        lock (_gate)
        {
            // The queue is oldest-first; reverse for newest-first, then take the requested count.
            return _traces.Reverse().Take(Math.Max(0, limit)).ToList();
        }
    }

    public RequestTrace? Get(Guid requestId)
    {
        lock (_gate)
        {
            return _traces.FirstOrDefault(t => t.RequestId == requestId);
        }
    }
}
