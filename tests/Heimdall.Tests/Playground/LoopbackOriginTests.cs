using Heimdall.Api.Playground;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Shouldly;
using Xunit;

namespace Heimdall.Tests.Playground;

/// <summary>
/// The replay origin is taken from the server's own bound address, never the inbound Host header, so a
/// caller cannot aim a replay at an arbitrary host (SSRF). Wildcard binds normalise to loopback.
/// </summary>
public class LoopbackOriginTests
{
    [Theory]
    [InlineData("http://0.0.0.0:8080", "http://localhost:8080/")]
    [InlineData("http://+:5000", "http://localhost:5000/")]
    [InlineData("http://[::]:8080", "http://localhost:8080/")]
    [InlineData("http://localhost:5234", "http://localhost:5234/")]
    public void Resolves_to_loopback_from_the_servers_bound_address(string bound, string expected) =>
        LoopbackOrigin.Resolve(new StubServer(bound)).ToString().ShouldBe(expected);

    [Fact]
    public void Falls_back_to_localhost_when_no_address_is_bound() =>
        LoopbackOrigin.Resolve(new StubServer()).ToString().ShouldBe("http://localhost/");

    private sealed class StubServer : IServer
    {
        public StubServer(params string[] addresses)
        {
            var feature = new StubAddressesFeature();
            foreach (var address in addresses)
            {
                feature.Addresses.Add(address);
            }

            Features = new FeatureCollection();
            Features.Set<IServerAddressesFeature>(feature);
        }

        public IFeatureCollection Features { get; }

        public void Dispose() { }

        public Task StartAsync<TContext>(IHttpApplication<TContext> application, CancellationToken ct)
            where TContext : notnull => Task.CompletedTask;

        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

        private sealed class StubAddressesFeature : IServerAddressesFeature
        {
            public ICollection<string> Addresses { get; } = new List<string>();

            public bool PreferHostingUrls { get; set; }
        }
    }
}
