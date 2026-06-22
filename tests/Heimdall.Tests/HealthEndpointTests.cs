using System.Net;
using System.Net.Http.Json;
using Heimdall.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace Heimdall.Tests;

[Collection("gateway-e2e")]
public class HealthEndpointTests(TestAppFactory factory) : IClassFixture<TestAppFactory>
{
    [Fact]
    public async Task Health_returns_200_and_healthy()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<HealthResponse>();
        body.ShouldNotBeNull();
        body!.Status.ShouldBe("healthy");
    }

    private sealed record HealthResponse(string Status);
}
