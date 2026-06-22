using Heimdall.Application;
using Heimdall.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Heimdall.Tests.Configuration;

/// <summary>
/// The config-gated demo overlay (Heimdall:EnableDemoApi). Off by default: the demo API is absent.
/// On: the "Acme Demo Services" API is appended and every operation responds without a real backend
/// (mock-response / return-response), so the showcase works in any environment.
/// </summary>
[Collection("gateway-e2e")]
public class DemoApiTests
{
    [Fact]
    public async Task Demo_api_is_absent_when_the_flag_is_unset()
    {
        await using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/demo/health");

        ((int)response.StatusCode).ShouldBe(404);
    }

    [Fact]
    public async Task Health_is_served_via_mock_response_when_enabled()
    {
        await using var factory = new DemoAppFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/demo/health");

        ((int)response.StatusCode).ShouldBe(200);
    }

    [Fact]
    public async Task Product_search_is_rate_limited_and_returns_429_on_repeat()
    {
        await using var factory = new DemoAppFactory();
        var client = factory.CreateClient();

        // rate-limit calls="3": the fourth call in the window is throttled.
        int status = 0;
        for (var i = 0; i < 4; i++)
        {
            status = (int)(await client.GetAsync("/demo/products")).StatusCode;
        }

        status.ShouldBe(429);
    }

    [Fact]
    public async Task Create_user_without_a_jwt_is_rejected()
    {
        await using var factory = new DemoAppFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsync("/demo/users", new StringContent("{}"));

        ((int)response.StatusCode).ShouldBe(401);
    }

    [Fact]
    public async Task List_users_serves_a_cache_hit_on_repeat()
    {
        await using var factory = new DemoAppFactory();
        var client = factory.CreateClient();
        var sink = factory.Services.GetRequiredService<ITraceSink>();

        var first = await client.GetAsync("/demo/users");
        var second = await client.GetAsync("/demo/users");

        ((int)first.StatusCode).ShouldBe(200);
        ((int)second.StatusCode).ShouldBe(200);
        (await second.Content.ReadAsStringAsync()).ShouldContain("Ada Lovelace");

        // Engine-exposed signal: on a cache MISS the inbound stage runs cache-lookup then return-response;
        // on a HIT cache-lookup short-circuits, so return-response never runs. Newest trace is the hit.
        var traces = sink.Recent(10);
        var hit = traces[0];
        var miss = traces[1];
        InboundPolicies(miss).ShouldContain("return-response");
        InboundPolicies(hit).ShouldContain("cache-lookup");
        InboundPolicies(hit).ShouldNotContain("return-response");
    }

    private static IReadOnlyList<string> InboundPolicies(RequestTrace trace) =>
        trace.Stages.Single(s => s.Section == "Inbound").Policies.Select(p => p.Name).ToArray();
}
