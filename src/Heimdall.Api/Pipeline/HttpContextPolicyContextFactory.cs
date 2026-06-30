using Heimdall.Api.Configuration;
using Heimdall.Api.Playground;
using Heimdall.Api.Routing;
using Heimdall.Application;
using Heimdall.Infrastructure.Context;
using Microsoft.AspNetCore.Http.Extensions;

namespace Heimdall.Api.Pipeline;

/// <summary>
/// Bridges between ASP.NET's <see cref="HttpContext"/> and the policy <see cref="IPolicyContext"/>:
/// builds the context from the incoming request, and writes the final context.Response back to the
/// client after the pipeline has run.
/// </summary>
public sealed class HttpContextPolicyContextFactory(
    IExpressionEvaluator expressions, INamedValues namedValues, IClock clock, GatewayConfigHolder config)
{
    public async Task<IPolicyContext> CreateAsync(
        HttpContext http,
        RouteMatch match,
        SubscriptionInfo? subscription,
        ProductInfo? product,
        bool bufferBody,
        CancellationToken ct)
    {
        var headers = http.Request.Headers
            .ToDictionary(
                h => h.Key,
                h => h.Value.Select(v => v ?? string.Empty).ToArray(),
                StringComparer.OrdinalIgnoreCase);

        // Only spool the body when a policy will re-read it; otherwise it streams to the backend
        // untouched (YARP), and the context body stays empty.
        var body = bufferBody ? await ReadBufferedBodyAsync(http.Request, ct) : string.Empty;

        return new PolicyContext
        {
            Request = new EmuRequest
            {
                Method = http.Request.Method,
                Url = new Uri(http.Request.GetDisplayUrl()),
                Headers = headers,
                Body = new HttpEmuBody(body),
                IpAddress = http.Connection.RemoteIpAddress?.ToString(),
            },
            // Named backends (post-override) so set-backend-service backend-id can resolve them.
            Backends = config.Current.Backends.ToDictionary(b => b.Id, b => b.Url),
            Api = new ApiInfo(match.Api.Id, match.Api.DisplayName, match.Api.Path),
            Operation = new OperationInfo(match.Operation.Id, match.Operation.Method, match.Operation.UriTemplate),
            Subscription = subscription,
            Product = product,
            // A playground replay supplies its own id (captured from the internal header) so the trace
            // correlates; ordinary traffic gets a fresh id.
            RequestId = ReplayCorrelation.RequestId(http) ?? Guid.NewGuid(),
            Timestamp = clock.UtcNow,
            Expressions = expressions,
            NamedValues = namedValues,
        };
    }

    public async Task WriteResponseAsync(HttpContext http, IPolicyContext context, CancellationToken ct)
    {
        if (http.Response.HasStarted)
        {
            return;
        }

        var response = context.Response;
        http.Response.StatusCode = response.StatusCode == 0 ? StatusCodes.Status200OK : response.StatusCode;

        foreach (var (name, values) in response.Headers)
        {
            // Let Kestrel compute framing headers for the body we are about to write.
            if (string.Equals(name, "Content-Length", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, "Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            http.Response.Headers[name] = values;
        }

        if (response.Body is not null)
        {
            await http.Response.WriteAsync(response.Body.As<string>(), ct);
        }
    }

    // EnableBuffering lets us read the body for policies, then rewind so YARP can forward it.
    // Decoded as UTF-8 (tier-1 boundary): non-UTF-8 or binary bodies are not faithfully represented.
    private static async Task<string> ReadBufferedBodyAsync(HttpRequest request, CancellationToken ct)
    {
        request.EnableBuffering();
        using var reader = new StreamReader(request.Body, leaveOpen: true);
        var body = await reader.ReadToEndAsync(ct);
        request.Body.Position = 0;
        return body;
    }
}
