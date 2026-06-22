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

namespace Heimdall.Tests.E2e;

/// <summary>
/// End-to-end subscription-key enforcement: the sample 'acme' API requires a key, scoped to the
/// 'acme-standard' product (primary key 'acme-standard-primary-key').
/// </summary>
[Collection("gateway-e2e")]
public class SubscriptionAuthE2eTests
{
    private const string ValidKey = "acme-standard-primary-key";

    [Fact]
    public async Task Missing_key_returns_401_with_the_apim_body()
    {
        await using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/catalog/items");

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await response.Content.ReadAsStringAsync()).ShouldBe(
            """{"statusCode":401,"message":"Access denied due to missing subscription key. Make sure to include subscription key when making requests to an API."}""");
    }

    [Fact]
    public async Task Invalid_key_returns_401_with_the_apim_body()
    {
        await using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/catalog/items");
        request.Headers.Add("Ocp-Apim-Subscription-Key", "wrong-key");

        var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await response.Content.ReadAsStringAsync()).ShouldBe(
            """{"statusCode":401,"message":"Access denied due to invalid subscription key. Make sure to provide a valid key for an active subscription."}""");
    }

    [Fact]
    public async Task Valid_key_in_header_is_forwarded()
    {
        using var backend = StartBackend();
        await using var factory = new TestAppFactory();
        var client = RepointAcme(factory, backend);
        var request = new HttpRequestMessage(HttpMethod.Get, "/catalog/items");
        request.Headers.Add("Ocp-Apim-Subscription-Key", ValidKey);

        var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        backend.LogEntries.Count().ShouldBe(1);
    }

    [Fact]
    public async Task Valid_key_in_query_is_forwarded()
    {
        using var backend = StartBackend();
        await using var factory = new TestAppFactory();
        var client = RepointAcme(factory, backend);

        var response = await client.GetAsync($"/catalog/items?subscription-key={ValidKey}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        backend.LogEntries.Count().ShouldBe(1);
    }

    [Fact]
    public async Task Header_key_takes_precedence_over_an_invalid_query_key()
    {
        using var backend = StartBackend();
        await using var factory = new TestAppFactory();
        var client = RepointAcme(factory, backend);
        var request = new HttpRequestMessage(HttpMethod.Get, "/catalog/items?subscription-key=wrong-key");
        request.Headers.Add("Ocp-Apim-Subscription-Key", ValidKey);

        var response = await client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Open_api_forwards_without_a_key()
    {
        using var backend = StartBackend();
        await using var factory = new TestAppFactory();
        var client = RepointAcme(factory, backend, open: true);

        var response = await client.GetAsync("/catalog/items");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        backend.LogEntries.Count().ShouldBe(1);
    }

    [Fact]
    public async Task Subscription_key_is_stripped_before_forwarding()
    {
        using var backend = StartBackend();
        await using var factory = new TestAppFactory();
        var client = RepointAcme(factory, backend);
        var request = new HttpRequestMessage(HttpMethod.Get, $"/catalog/items?subscription-key={ValidKey}");
        request.Headers.Add("Ocp-Apim-Subscription-Key", ValidKey);

        await client.SendAsync(request);

        var received = backend.LogEntries.Single().RequestMessage!;
        received.Headers!.ContainsKey("Ocp-Apim-Subscription-Key").ShouldBeFalse();
        (received.Query is null || !received.Query.ContainsKey("subscription-key")).ShouldBeTrue();
    }

    [Fact]
    public async Task Product_scoped_subscription_applies_product_policy()
    {
        using var backend = StartBackend();
        await using var factory = new TestAppFactory();
        var client = RepointAcme(factory, backend);
        var request = new HttpRequestMessage(HttpMethod.Get, "/catalog/items");
        request.Headers.Add("Ocp-Apim-Subscription-Key", ValidKey);

        await client.SendAsync(request);

        // acme-standard.product.xml adds X-Acme-Product on the inbound; product-scoped access pulls it in.
        backend.LogEntries.Single().RequestMessage!.Headers!["X-Acme-Product"].ShouldContain("acme-standard");
    }

    [Fact]
    public async Task All_apis_scoped_subscription_bypasses_product_policy()
    {
        using var backend = StartBackend();
        await using var factory = new TestAppFactory();
        var client = RepointAcme(factory, backend, addAllApisSub: true);
        var request = new HttpRequestMessage(HttpMethod.Get, "/catalog/items");
        request.Headers.Add("Ocp-Apim-Subscription-Key", "all-apis-key");

        await client.SendAsync(request);

        backend.LogEntries.Single().RequestMessage!.Headers!.ContainsKey("X-Acme-Product").ShouldBeFalse();
    }

    private static WireMockServer StartBackend()
    {
        var backend = WireMockServer.Start();
        backend
            .Given(Request.Create().WithPath("/catalog/items").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("""{"items":["widget"]}"""));
        return backend;
    }

    private static HttpClient RepointAcme(
        TestAppFactory factory, WireMockServer backend, bool open = false, bool addAllApisSub = false)
    {
        var client = factory.CreateClient();
        var holder = factory.Services.GetRequiredService<GatewayConfigHolder>();
        var api = holder.Current.Apis[0] with
        {
            ServiceUrl = new Uri(backend.Url!),
            SubscriptionRequired = !open,
        };

        var subscriptions = holder.Current.Subscriptions;
        if (addAllApisSub)
        {
            subscriptions =
            [
                .. subscriptions,
                new Subscription("all-apis-sub", "all-apis-key", "all-apis-secondary",
                    SubscriptionScope.AllApis, null, null, SubscriptionState.Active),
            ];
        }

        holder.Current = holder.Current with { Apis = [api], Subscriptions = subscriptions };
        return client;
    }
}
