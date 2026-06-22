using System.Diagnostics;
using System.Net;
using Heimdall.Api.Configuration;
using Heimdall.Api.Forwarding;
using Heimdall.Api.Middleware;
using Heimdall.Api.Pipeline;
using Heimdall.Api.Playground;
using Heimdall.Application;
using Yarp.ReverseProxy.Forwarder;

namespace Heimdall.Api;

/// <summary>Registers the gateway's host-side services (config, routing, flattening, forwarding).</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddGateway(this IServiceCollection services)
    {
        services.AddHttpForwarder();
        // Factory (not a pre-built instance) so the container owns the invoker and disposes it on shutdown.
        services.AddSingleton<HttpMessageInvoker>(_ => CreateForwardingClient());
        services.AddSingleton<IForwarder, YarpForwarder>();
        services.AddSingleton<GatewayConfigHolder>();
        services.AddSingleton<INamedValues, ConfigNamedValues>();
        services.AddSingleton<ISubscriptionKeyValidator, SubscriptionKeyValidator>();
        services.AddSingleton<EffectivePolicyBuilder>();
        services.AddSingleton<PolicyPipelineExecutor>();
        services.AddSingleton<HttpContextPolicyContextFactory>();
        services.AddSingleton<GatewayMiddleware>();

        // Playground replay: a typed client that loops back to this gateway. Tests override its primary
        // handler to drive the in-memory test server.
        services.AddHttpClient<IGatewayReplayClient, LoopbackReplayClient>();
        return services;
    }

    // One shared, proxy-tuned client for all forwarding (YARP best practice: no auto-redirect,
    // decompression, cookies, or proxy; keeps the gateway transparent). See docs/IMPLEMENTATION.md section 7.
    private static HttpMessageInvoker CreateForwardingClient() =>
        new(new SocketsHttpHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.None,
            UseCookies = false,
            EnableMultipleHttp2Connections = true,
            ActivityHeadersPropagator = new ReverseProxyPropagator(DistributedContextPropagator.Current),
            ConnectTimeout = TimeSpan.FromSeconds(15),
        });
}
