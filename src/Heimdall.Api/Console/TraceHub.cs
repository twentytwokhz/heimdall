using Microsoft.AspNetCore.SignalR;

namespace Heimdall.Api.Console;

/// <summary>
/// The live trace feed hub. Server-to-client only: the gateway pushes a <c>"trace"</c> message per
/// recorded request (via <see cref="TraceBroadcaster"/>); clients invoke nothing.
/// </summary>
public sealed class TraceHub : Hub;
