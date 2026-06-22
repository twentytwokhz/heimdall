using System.Net.Http.Headers;
using System.Text;
using Heimdall.Application;

namespace Heimdall.Api.Playground;

/// <summary>
/// Replays a <see cref="PlaygroundRequest"/> over loopback HTTP so it runs the identical gateway pipeline
/// as real client traffic. Tags the request with an <see cref="ReplayCorrelation.HeaderName"/> id the
/// gateway adopts, so the resulting trace can be fetched by <see cref="PlaygroundResponse.RequestId"/>.
/// </summary>
public sealed class LoopbackReplayClient(HttpClient client) : IGatewayReplayClient
{
    public async Task<PlaygroundResponse> ReplayAsync(PlaygroundRequest request, Uri gatewayOrigin, CancellationToken ct)
    {
        var replayId = Guid.NewGuid();

        // Send the imported path/query to the live gateway origin (ignoring the import-time host), so a
        // replay always hits this server regardless of where the collection was originally pointed.
        var target = Uri.TryCreate(request.Url, UriKind.Absolute, out var absolute)
            ? new Uri(gatewayOrigin, absolute.PathAndQuery)
            : new Uri(gatewayOrigin, request.Url);

        using var message = new HttpRequestMessage(new HttpMethod(request.Method), target);
        message.Headers.TryAddWithoutValidation(ReplayCorrelation.HeaderName, replayId.ToString());

        foreach (var header in request.Headers)
        {
            // Host is recomputed from the target; Content-Type is carried by BodyMediaType onto the body.
            if (string.Equals(header.Name, "Host", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            message.Headers.TryAddWithoutValidation(header.Name, header.Value);
        }

        if (request.Body is not null)
        {
            // StringContent defaults to text/plain; charset=utf-8. When the import carried a media type
            // (raw language, urlencoded, or the multipart boundary) it overrides that; otherwise the
            // text/plain default stands - a reasonable default for a body with no declared content type.
            message.Content = new StringContent(request.Body, Encoding.UTF8);
            if (request.BodyMediaType is { } mediaType && MediaTypeHeaderValue.TryParse(mediaType, out var parsed))
            {
                message.Content.Headers.ContentType = parsed;
            }
        }

        using var response = await client.SendAsync(message, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        var headers = response.Headers
            .Concat(response.Content.Headers)
            .Select(h => new PlaygroundHeader(h.Key, string.Join(", ", h.Value)))
            .ToList();

        return new PlaygroundResponse(replayId, (int)response.StatusCode, headers, body);
    }
}
