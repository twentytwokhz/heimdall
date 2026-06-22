namespace Heimdall.Application;

/// <summary>
/// Stores recent <see cref="RequestTrace"/>s for the observability console. Implementations are a
/// bounded, thread-safe buffer: the gateway adds one trace per request, the admin API / live feed read.
/// </summary>
public interface ITraceSink
{
    /// <summary>Records a completed request trace, evicting the oldest if the buffer is full.</summary>
    void Add(RequestTrace trace);

    /// <summary>
    /// Raised after a trace is added, so a live feed (SignalR) can push it. Handlers must not block or
    /// throw: <see cref="Add"/> runs on the gateway request thread. Raised outside any internal lock.
    /// </summary>
    event Action<RequestTrace>? TraceAdded;

    /// <summary>
    /// A newest-first snapshot of up to <paramref name="limit"/> recent traces; a non-positive
    /// <paramref name="limit"/> yields an empty list.
    /// </summary>
    IReadOnlyList<RequestTrace> Recent(int limit);

    /// <summary>The trace with this id, or null if it is not (or no longer) buffered.</summary>
    RequestTrace? Get(Guid requestId);
}
