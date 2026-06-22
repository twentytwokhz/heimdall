using Heimdall.Application;
using Heimdall.Infrastructure.Context;
using Microsoft.AspNetCore.Http;
using Yarp.ReverseProxy.Forwarder;

namespace Heimdall.Api.Forwarding;

/// <summary>
/// Bridges the policy context onto YARP's forwarding: applies inbound request mutations onto the
/// proxy request, then captures the backend response into <c>context.Response</c> and returns
/// <c>false</c> so YARP does not auto-copy it - leaving the gateway free to run outbound policies
/// and write the response itself (the buffered model confirmed by the Batch 0 spike).
/// </summary>
public sealed class HeimdallHttpTransformer(IPolicyContext context) : HttpTransformer
{
    // YARP/HttpClient recompute these on the proxy request; re-applying them from the context would clobber it.
    private static readonly HashSet<string> SkippedRequestHeaders =
        new(StringComparer.OrdinalIgnoreCase) { "Host", "Content-Length" };

    // Entity-body headers belong on HttpContent, not the request line: touching them on
    // HttpRequestMessage.Headers throws "Misused header name". Route these to Content.Headers instead.
    private static readonly HashSet<string> ContentHeaders =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Allow", "Content-Disposition", "Content-Encoding", "Content-Language", "Content-Location",
            "Content-MD5", "Content-Range", "Content-Type", "Expires", "Last-Modified",
        };

    public override async ValueTask TransformRequestAsync(
        HttpContext httpContext, HttpRequestMessage proxyRequest, string destinationPrefix, CancellationToken cancellationToken)
    {
        await base.TransformRequestAsync(httpContext, proxyRequest, destinationPrefix, cancellationToken);

        // Apply the (possibly policy-mutated) method and URI onto the proxy request. The body is synced
        // onto HttpContext.Request.Body before forwarding (YARP owns the outgoing HttpContent and forbids
        // replacing it here), so we only touch method/URI/headers in the transformer.
        proxyRequest.Method = new HttpMethod(context.Request.Method);
        proxyRequest.RequestUri = new Uri(destinationPrefix + context.Request.Url.PathAndQuery);

        // Apply the (possibly policy-mutated) request headers from the context onto the proxy request,
        // routing entity-body headers to Content so the request-header collection never sees them.
        foreach (var (name, values) in context.Request.Headers)
        {
            if (SkippedRequestHeaders.Contains(name))
            {
                continue;
            }

            if (ContentHeaders.Contains(name))
            {
                var content = proxyRequest.Content;
                if (content is null)
                {
                    // No body to attach entity headers to (e.g. a Content-Type set on a bodyless GET):
                    // dropped, a documented tier-1 boundary.
                    continue;
                }

                content.Headers.Remove(name);
                foreach (var value in values)
                {
                    content.Headers.TryAddWithoutValidation(name, value);
                }
            }
            else
            {
                proxyRequest.Headers.Remove(name);
                foreach (var value in values)
                {
                    proxyRequest.Headers.TryAddWithoutValidation(name, value);
                }
            }
        }
    }

    public override async ValueTask<bool> TransformResponseAsync(
        HttpContext httpContext, HttpResponseMessage? proxyResponse, CancellationToken cancellationToken)
    {
        // No backend response (e.g. transport error) - leave context.Response for the caller to handle.
        if (proxyResponse is null)
        {
            return false;
        }

        var response = context.Response;
        response.StatusCode = (int)proxyResponse.StatusCode;

        var headers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in proxyResponse.Headers)
        {
            headers[header.Key] = header.Value.ToArray();
        }
        foreach (var header in proxyResponse.Content.Headers)
        {
            headers[header.Key] = header.Value.ToArray();
        }
        response.Headers = headers;
        response.Body = new HttpEmuBody(await proxyResponse.Content.ReadAsStringAsync(cancellationToken));

        // Suppress YARP's auto-copy: the gateway runs outbound policies and writes context.Response itself.
        return false;
    }
}
