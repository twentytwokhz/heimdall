using System.Net;
using System.Net.Http.Json;
using Heimdall.Api.Configuration;
using Heimdall.Domain;
using Heimdall.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Heimdall.Tests.E2e;

/// <summary>
/// The policy-authoring API under <c>/_apim/authoring/policy</c>: read a scope's current source XML,
/// and save (validate + in-memory hot-swap). Saves never write to disk and fail loud on bad input.
/// </summary>
[Collection("gateway-e2e")]
public class AuthoringApiE2eTests
{
    private const string GlobalCors =
        "<policies><inbound><cors /></inbound><backend><base /></backend><outbound><base /></outbound><on-error><base /></on-error></policies>";

    [Fact]
    public async Task Get_returns_source_xml_for_the_global_scope()
    {
        await using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        var holder = factory.Services.GetRequiredService<GatewayConfigHolder>();
        holder.Current = holder.Current with
        {
            GlobalPolicy = new PolicyDocument([new PolicyNode("cors", new Dictionary<string, string>(), [], null)], [], [], []),
        };

        var response = await client.GetAsync("/_apim/authoring/policy?scope=global");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        body.ShouldContain("cors");
        body.ShouldContain("inbound");
    }

    [Fact]
    public async Task Get_is_404_for_an_unknown_api()
    {
        await using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        (await client.GetAsync("/_apim/authoring/policy?scope=api&apiId=nope")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Get_is_400_for_a_missing_scope()
    {
        await using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        (await client.GetAsync("/_apim/authoring/policy?scope=bogus")).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Post_swaps_the_live_operation_policy()
    {
        await using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        var holder = factory.Services.GetRequiredService<GatewayConfigHolder>();
        var api = holder.Current.Apis[0];
        var op = api.Operations[0];
        const string xml =
            "<policies><inbound><base /><set-header name=\"X-Authored\" exists-action=\"override\"><value>1</value></set-header></inbound>"
            + "<backend><base /></backend><outbound><base /></outbound><on-error><base /></on-error></policies>";

        var response = await client.PostAsJsonAsync("/_apim/authoring/policy",
            new { scope = "operation", apiId = api.Id, operationId = op.Id, xml });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var saved = holder.Current.Apis.First(a => a.Id == api.Id).Operations.First(o => o.Id == op.Id);
        saved.Policy.ShouldNotBeNull();
        saved.Policy!.Inbound.ShouldContain(n => n.Name == "set-header" && n.Attributes["name"] == "X-Authored");
    }

    [Fact]
    public async Task Post_rejects_malformed_xml_without_swapping()
    {
        await using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        var holder = factory.Services.GetRequiredService<GatewayConfigHolder>();
        var before = holder.Current;

        var response = await client.PostAsJsonAsync("/_apim/authoring/policy",
            new { scope = "global", xml = "<policies><inbound>" });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        holder.Current.ShouldBeSameAs(before);
    }

    [Fact]
    public async Task Post_rejects_empty_xml_without_swapping()
    {
        await using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        var holder = factory.Services.GetRequiredService<GatewayConfigHolder>();
        var before = holder.Current;

        var response = await client.PostAsJsonAsync("/_apim/authoring/policy",
            new { scope = "global", xml = "   " });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        holder.Current.ShouldBeSameAs(before);
    }

    [Fact]
    public async Task Post_rejects_an_unsupported_policy_without_swapping()
    {
        await using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        var holder = factory.Services.GetRequiredService<GatewayConfigHolder>();
        var before = holder.Current;

        var response = await client.PostAsJsonAsync("/_apim/authoring/policy",
            new { scope = "global", xml = "<policies><inbound><frobnicate /></inbound></policies>" });
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        body.ShouldContain("frobnicate");
        holder.Current.ShouldBeSameAs(before);
    }

    [Fact]
    public async Task Post_is_404_for_an_unknown_operation()
    {
        await using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        var holder = factory.Services.GetRequiredService<GatewayConfigHolder>();
        var apiId = holder.Current.Apis[0].Id;

        var response = await client.PostAsJsonAsync("/_apim/authoring/policy",
            new { scope = "operation", apiId, operationId = "nope", xml = GlobalCors });

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
