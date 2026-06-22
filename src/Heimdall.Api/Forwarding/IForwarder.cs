using Heimdall.Application;
using Microsoft.AspNetCore.Http;

namespace Heimdall.Api.Forwarding;

/// <summary>
/// Forwards the (policy-mutated) request to a backend and captures the response into
/// <see cref="IPolicyContext.Response"/> so outbound policies can run before anything is written
/// to the client. This is the buffered, policy-aware backend stage.
/// </summary>
public interface IForwarder
{
    ValueTask ForwardAsync(HttpContext httpContext, IPolicyContext context, Uri destination, CancellationToken ct = default);
}
