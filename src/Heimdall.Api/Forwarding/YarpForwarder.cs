using Heimdall.Application;
using Microsoft.AspNetCore.Http;
using Yarp.ReverseProxy.Forwarder;

namespace Heimdall.Api.Forwarding;

/// <summary>
/// Forwards via YARP's Direct Forwarding API (IHttpForwarder), applying request mutations and
/// capturing the response into the policy context through <see cref="HeimdallHttpTransformer"/>.
/// </summary>
public sealed class YarpForwarder(
    IHttpForwarder forwarder,
    HttpMessageInvoker invoker,
    ILogger<YarpForwarder> logger) : IForwarder
{
    private static readonly ForwarderRequestConfig RequestConfig =
        new() { ActivityTimeout = TimeSpan.FromSeconds(100) };

    public async ValueTask ForwardAsync(
        HttpContext httpContext, IPolicyContext context, Uri destination, CancellationToken ct = default)
    {
        // HttpTransformer.Default appends the incoming path + query to this prefix, so the prefix must
        // include any base path on the destination (e.g. http://backend/v2), not just the authority.
        var prefix = destination.GetLeftPart(UriPartial.Path).TrimEnd('/');

        // Sync the (possibly policy-mutated) buffered body onto the request so YARP streams it. An empty
        // context body means the body was never buffered, so we leave the original request stream intact.
        var bufferedBody = context.Request.Body.As<string>();
        if (!string.IsNullOrEmpty(bufferedBody))
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(bufferedBody);
            httpContext.Request.Body = new MemoryStream(bytes);
            httpContext.Request.ContentLength = bytes.Length;
        }

        var transformer = new HeimdallHttpTransformer(context);

        var error = await forwarder.SendAsync(httpContext, prefix, invoker, RequestConfig, transformer);

        if (error != ForwarderError.None)
        {
            var feature = httpContext.Features.Get<IForwarderErrorFeature>();
            logger.LogError(feature?.Exception, "Forwarding to {Destination} failed: {Error}", destination, error);
            // The transformer captured nothing (no backend response); surface a gateway error for the caller to write.
            context.Response.StatusCode = StatusCodes.Status502BadGateway;
        }
    }
}
