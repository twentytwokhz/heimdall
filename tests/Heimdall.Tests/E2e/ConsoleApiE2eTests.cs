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
/// The read-only console admin API under <c>/_apim/*</c>: config (secrets masked), effective policy,
/// and the trace feed, plus the gateway seam guard that reserves the <c>/_apim</c> namespace.
/// </summary>
[Collection("gateway-e2e")]
public class ConsoleApiE2eTests
{
    [Fact]
    public async Task Config_exposes_resources_but_masks_secrets()
    {
        await using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        var holder = factory.Services.GetRequiredService<GatewayConfigHolder>();
        holder.Current = holder.Current with
        {
            NamedValues =
            [
                new NamedValue("api-secret", "supersecret-value", Secret: true),
                new NamedValue("public-tag", "acme-prod", Secret: false),
            ],
        };

        var response = await client.GetAsync("/_apim/config");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        body.ShouldContain("acme");           // the api is exposed
        body.ShouldContain("public-tag");     // a non-secret named value, name + value
        body.ShouldContain("acme-prod");
        body.ShouldContain("api-secret");     // a secret named value: name kept, value masked
        body.ShouldContain("***");
        body.ShouldNotContain("supersecret-value");        // secret value never leaks
        body.ShouldNotContain("acme-standard-primary-key"); // subscription keys never leak
    }

    [Fact]
    public async Task Effective_policy_flattens_global_and_operation_scopes()
    {
        await using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        var holder = factory.Services.GetRequiredService<GatewayConfigHolder>();
        // <base/> first so the operation inherits the parent (global) scope, then its own set-header.
        var op = new Operation("listCatalogItems", "GET", "/items",
            new PolicyDocument(
                [
                    new PolicyNode("base", new Dictionary<string, string>(), [], null),
                    new PolicyNode("set-header", new Dictionary<string, string> { ["name"] = "X-Op" }, [], null),
                ],
                [], [], []));
        var api = holder.Current.Apis[0] with { Id = "acme", Operations = [op] };
        holder.Current = holder.Current with
        {
            Apis = [api],
            GlobalPolicy = new PolicyDocument(
                [new PolicyNode("cors", new Dictionary<string, string>(), [], null)], [], [], []),
        };

        var response = await client.GetAsync("/_apim/policies/acme/listCatalogItems");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        body.ShouldContain("inbound");      // the four sections are present
        body.ShouldContain("cors");         // from the global scope
        body.ShouldContain("set-header");   // from the operation scope
    }

    [Fact]
    public async Task Effective_policy_is_404_for_an_unknown_api_or_operation()
    {
        await using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        (await client.GetAsync("/_apim/policies/nope/listCatalogItems")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.GetAsync("/_apim/policies/acme/nope")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Traces_feed_lists_recorded_requests_and_serves_them_by_id()
    {
        using var backend = WireMockServer.Start();
        backend
            .Given(Request.Create().WithPath("/catalog/items").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("ok"));

        await using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        var holder = factory.Services.GetRequiredService<GatewayConfigHolder>();
        var api = holder.Current.Apis[0] with { ServiceUrl = new Uri(backend.Url!), SubscriptionRequired = false };
        holder.Current = holder.Current with { Apis = [api] };

        await client.GetAsync("/catalog/items");

        var listBody = await (await client.GetAsync("/_apim/traces")).Content.ReadAsStringAsync();
        listBody.ShouldContain("/catalog/items");
        listBody.ShouldContain("Completed"); // the outcome enum serialized as a string

        var id = factory.Services.GetRequiredService<Heimdall.Application.ITraceSink>().Recent(1).Single().RequestId;
        var detail = await client.GetAsync($"/_apim/traces/{id}");

        detail.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await detail.Content.ReadAsStringAsync()).ShouldContain(id.ToString());
    }

    [Fact]
    public async Task Traces_feed_honours_the_limit_query()
    {
        using var backend = WireMockServer.Start();
        backend
            .Given(Request.Create().WithPath("/catalog/items").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("ok"));

        await using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        var holder = factory.Services.GetRequiredService<GatewayConfigHolder>();
        var api = holder.Current.Apis[0] with { ServiceUrl = new Uri(backend.Url!), SubscriptionRequired = false };
        holder.Current = holder.Current with { Apis = [api] };

        await client.GetAsync("/catalog/items");
        await client.GetAsync("/catalog/items");

        var body = await (await client.GetAsync("/_apim/traces?limit=1")).Content.ReadAsStringAsync();

        using var doc = System.Text.Json.JsonDocument.Parse(body);
        doc.RootElement.GetArrayLength().ShouldBe(1);
    }

    [Fact]
    public async Task Trace_by_id_is_404_for_an_unknown_or_non_guid_id()
    {
        await using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        (await client.GetAsync($"/_apim/traces/{Guid.NewGuid()}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await client.GetAsync("/_apim/traces/not-a-guid")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Console_is_off_when_disabled()
    {
        // The headless/CI switch: EnableConsole=false must take the WHOLE console surface offline -
        // the admin API, the SignalR hub, the SPA shell, and the authoring API - so a request to any
        // of them falls to the gateway's /_apim guard (404). The gateway data plane is unaffected.
        await using var factory = new TestAppFactory().WithWebHostBuilder(b =>
            b.UseSetting("Heimdall:EnableConsole", "false"));
        var client = factory.CreateClient();

        // The config admin API.
        (await client.GetAsync("/_apim/config")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        // The live trace hub.
        (await client.GetAsync("/_apim/hub/traces")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        // The authoring API.
        (await client.GetAsync("/_apim/authoring/policy?scope=global")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // A SPA client route: even a browser navigation gets a clean 404, never the app shell.
        using var spaRequest = new HttpRequestMessage(HttpMethod.Get, "/_apim/apis");
        spaRequest.Headers.Add("Accept", "text/html");
        (await client.SendAsync(spaRequest)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_apim_namespace_is_reserved_and_never_proxied()
    {
        using var backend = WireMockServer.Start();
        backend
            .Given(Request.Create().WithPath("/proxytest").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("leaked"));

        await using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        // An API deliberately configured under /_apim must not be reachable: the gateway reserves the namespace.
        var holder = factory.Services.GetRequiredService<GatewayConfigHolder>();
        var api = holder.Current.Apis[0] with
        {
            Path = "/_apim",
            Operations = [new Operation("proxy", "GET", "/proxytest", null)],
            ServiceUrl = new Uri(backend.Url!),
            SubscriptionRequired = false,
        };
        holder.Current = holder.Current with { Apis = [api] };

        var response = await client.GetAsync("/_apim/proxytest");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).ShouldNotContain("leaked");
        backend.LogEntries.ShouldBeEmpty();
    }
}
