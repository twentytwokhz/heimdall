using Heimdall.Application;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Heimdall.Api.Console;

/// <summary>
/// Bridges the trace sink to the SignalR hub: subscribes to <see cref="ITraceSink.TraceAdded"/> and
/// pushes each trace to connected clients. Fire-and-forget and exception-swallowing, because the
/// event is raised on the gateway request thread - a slow or failed broadcast must never block or
/// surface into a real request. Keeping SignalR here keeps the sink (Infrastructure) free of it.
/// </summary>
public sealed class TraceBroadcaster(
    ITraceSink sink, IHubContext<TraceHub> hub, ILogger<TraceBroadcaster> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        sink.TraceAdded += OnTraceAdded;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        sink.TraceAdded -= OnTraceAdded;
        return Task.CompletedTask;
    }

    private void OnTraceAdded(RequestTrace trace) => _ = BroadcastAsync(trace);

    private async Task BroadcastAsync(RequestTrace trace)
    {
        try
        {
            await hub.Clients.All.SendAsync("trace", trace);
        }
        catch (Exception ex)
        {
            // A broadcast failure is non-fatal: the trace is still buffered and served by /_apim/traces.
            logger.LogDebug(ex, "Failed to broadcast trace {RequestId} to the live feed.", trace.RequestId);
        }
    }
}
