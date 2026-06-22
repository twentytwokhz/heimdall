using Heimdall.Api.Playground;
using Heimdall.Application;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Heimdall.Tests.Fixtures;

/// <summary>
/// Hosts the Api in-memory for end-to-end tests, in the "Testing" environment. A single public
/// parameterless ctor keeps it usable as an xUnit IClassFixture; tests that need different settings
/// (e.g. a different loader or the admin API) layer them via <c>WithWebHostBuilder</c>.
/// </summary>
public sealed class TestAppFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Heimdall:ConfigPath", RepoPaths.SamplesDir());

        // Playground replay loops back over HTTP; route that loopback to this in-memory test server so the
        // real LoopbackReplayClient runs the full pipeline without binding a socket.
        builder.ConfigureTestServices(services =>
            services.AddHttpClient<IGatewayReplayClient, LoopbackReplayClient>()
                .ConfigurePrimaryHttpMessageHandler(() => Server.CreateHandler()));
    }
}
