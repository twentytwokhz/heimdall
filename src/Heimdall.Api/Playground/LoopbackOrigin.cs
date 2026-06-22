using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace Heimdall.Api.Playground;

/// <summary>
/// Resolves the origin a playground replay loops back to: THIS process's own bound address, taken from
/// the server's addresses. Never the inbound Host header - that is caller-controlled, and using it would
/// let a replay be aimed at an arbitrary host (an SSRF that reflects the response back to the caller).
/// Wildcard binds (0.0.0.0, [::], *, +) map to loopback so the call reaches this process; with no address
/// available (the in-memory test server) it falls back to localhost.
/// </summary>
public static class LoopbackOrigin
{
    public static Uri Resolve(IServer server)
    {
        var address = server.Features.Get<IServerAddressesFeature>()?.Addresses.FirstOrDefault();
        if (string.IsNullOrEmpty(address))
        {
            return new Uri("http://localhost");
        }

        var binding = BindingAddress.Parse(address);
        var host = binding.Host switch
        {
            "0.0.0.0" or "::" or "[::]" or "*" or "+" or "" => "localhost",
            var bound => bound,
        };

        var builder = new UriBuilder(binding.Scheme, host);
        if (binding.Port != 0)
        {
            builder.Port = binding.Port;
        }

        return builder.Uri;
    }
}
