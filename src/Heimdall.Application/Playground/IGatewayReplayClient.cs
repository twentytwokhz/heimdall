namespace Heimdall.Application;

/// <summary>
/// Replays one <see cref="PlaygroundRequest"/> against the running gateway. The default implementation
/// does this over loopback HTTP so replay runs the identical pipeline as real client traffic; the seam
/// lets tests drive the in-memory test server instead. Implementations tag the request so the resulting
/// trace can be correlated by <see cref="PlaygroundResponse.RequestId"/>.
/// </summary>
public interface IGatewayReplayClient
{
    Task<PlaygroundResponse> ReplayAsync(PlaygroundRequest request, Uri gatewayOrigin, CancellationToken ct);
}
