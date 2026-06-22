using Heimdall.Api.Playground;
using Heimdall.Application;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Heimdall.Tests.Fixtures;

/// <summary>
/// Like <see cref="TestAppFactory"/> but with the demo overlay enabled (Heimdall:EnableDemoApi=true),
/// so the "Acme Demo Services" API is loaded alongside the base sample config. A dedicated subclass
/// (rather than WithWebHostBuilder) keeps a single owned factory whose service provider stays alive
/// for the test's duration.
/// </summary>
public sealed class DemoAppFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Heimdall:ConfigPath", RepoPaths.SamplesDir());
        builder.UseSetting("Heimdall:EnableDemoApi", "true");

        builder.ConfigureTestServices(services =>
            services.AddHttpClient<IGatewayReplayClient, LoopbackReplayClient>()
                .ConfigurePrimaryHttpMessageHandler(() => Server.CreateHandler()));
    }
}
