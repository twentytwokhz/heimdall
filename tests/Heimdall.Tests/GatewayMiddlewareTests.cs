using System.Net;
using Heimdall.Api.Configuration;
using Heimdall.Domain;
using Heimdall.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace Heimdall.Tests;

/// <summary>End-to-end: a routed request flows through the gateway and is forwarded to the backend.</summary>
[Collection("gateway-e2e")]
public class GatewayMiddlewareTests
{
    [Fact]
    public async Task Matched_collection_route_is_forwarded_to_backend()
    {
        using var backend = WireMockServer.Start();
        backend
            .Given(Request.Create().WithPath("/catalog/items").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("""{"items":["widget"]}"""));

        await using var factory = new TestAppFactory();
        var client = PointGatewayAt(factory, backend);

        var response = await client.GetAsync("/catalog/items");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldContain("widget");
        backend.LogEntries.Count().ShouldBe(1);
    }

    [Fact]
    public async Task Matched_item_route_with_template_is_forwarded_to_backend()
    {
        using var backend = WireMockServer.Start();
        backend
            .Given(Request.Create().WithPath("/catalog/items/42").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("""{"sku":"ACME-42"}"""));

        await using var factory = new TestAppFactory();
        var client = PointGatewayAt(factory, backend);

        var response = await client.GetAsync("/catalog/items/42");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldContain("ACME-42");
    }

    [Fact]
    public async Task Service_url_base_path_is_preserved_when_forwarding()
    {
        using var backend = WireMockServer.Start();
        backend
            .Given(Request.Create().WithPath("/v2/catalog/items").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("""{"items":["v2"]}"""));

        await using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        var holder = factory.Services.GetRequiredService<GatewayConfigHolder>();
        var api = holder.Current.Apis[0] with { ServiceUrl = new Uri($"{backend.Url}/v2"), SubscriptionRequired = false };
        holder.Current = holder.Current with { Apis = [api] };

        var response = await client.GetAsync("/catalog/items");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldContain("v2");
        backend.LogEntries.Count().ShouldBe(1);
    }

    [Fact]
    public async Task Unmatched_route_returns_404()
    {
        await using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/nonexistent");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Health_is_not_proxied()
    {
        await using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldContain("healthy");
    }

    // Loads the sample config (via TestAppFactory), then repoints the API at the WireMock backend's dynamic port.
    private static HttpClient PointGatewayAt(TestAppFactory factory, WireMockServer backend)
    {
        var client = factory.CreateClient();
        var holder = factory.Services.GetRequiredService<GatewayConfigHolder>();
        var api = holder.Current.Apis[0] with { ServiceUrl = new Uri(backend.Url!), SubscriptionRequired = false };
        holder.Current = holder.Current with { Apis = [api] };
        return client;
    }
}
