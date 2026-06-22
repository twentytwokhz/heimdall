using Microsoft.AspNetCore.Http;

namespace Heimdall.Api.Playground;

/// <summary>
/// Carries a playground replay's request id from the replay client through the gateway. The gateway
/// adopts it as the request's id (so the recorded trace can be correlated by
/// <c>PlaygroundResponse.RequestId</c>) and removes the header before forwarding, so it never reaches
/// the backend. The header is internal: real client traffic never sends it, so real responses are
/// unchanged.
/// </summary>
public static class ReplayCorrelation
{
    public const string HeaderName = "X-Heimdall-Replay-Id";
    private const string ItemsKey = "heimdall.replayId";

    /// <summary>Reads and removes the replay-id header, stashing a parsed id in <see cref="HttpContext.Items"/>.</summary>
    public static void Capture(HttpContext http)
    {
        var value = http.Request.Headers[HeaderName].ToString();
        http.Request.Headers.Remove(HeaderName);

        if (Guid.TryParse(value, out var id))
        {
            http.Items[ItemsKey] = id;
        }
    }

    /// <summary>The captured replay id for this request, or null if it was not a replay.</summary>
    public static Guid? RequestId(HttpContext http) =>
        http.Items.TryGetValue(ItemsKey, out var value) && value is Guid id ? id : null;
}
