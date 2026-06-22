using Heimdall.Api.Configuration;
using Heimdall.Application;
using Heimdall.Domain;
using Heimdall.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace Heimdall.Tests.Tracing;

/// <summary>
/// End-to-end proof that a request through the gateway produces a faithful <see cref="RequestTrace"/>
/// in the sink: the four-stage canvas for a forwarded request, a stage-less Rejected trace when auth
/// fails, and no Backend stage when a policy short-circuits.
/// </summary>
[Collection("gateway-e2e")]
public class TraceIntegrationTests
{
    [Fact]
    public async Task A_forwarded_request_records_a_completed_trace_with_all_four_stages()
    {
        using var backend = WireMockServer.Start();
        backend
            .Given(Request.Create().WithPath("/catalog/items").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("ok"));

        await using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        PointBackend(factory, backend, new PolicyDocument([SetHeader("X-Trace", "1")], [], [], []));

        await client.GetAsync("/catalog/items");

        var trace = factory.Services.GetRequiredService<ITraceSink>().Recent(10).ShouldHaveSingleItem();
        trace.Method.ShouldBe("GET");
        trace.Path.ShouldBe("/catalog/items");
        trace.ApiId.ShouldBe("acme");
        trace.OperationMethod.ShouldBe("GET");
        trace.StatusCode.ShouldBe(200);
        trace.Outcome.ShouldBe(TraceOutcome.Completed);
        trace.Stages.Select(s => s.Section).ShouldBe(["Frontend", "Inbound", "Backend", "Outbound"]);
        trace.Stages.Single(s => s.Section == "Inbound").Policies.Select(p => p.Name).ShouldContain("set-header");
    }

    [Fact]
    public async Task A_request_with_no_subscription_key_records_a_rejected_trace()
    {
        await using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        // The sample 'acme' API requires a key, so this is rejected before the pipeline.
        await client.GetAsync("/catalog/items");

        var trace = factory.Services.GetRequiredService<ITraceSink>().Recent(10).ShouldHaveSingleItem();
        trace.StatusCode.ShouldBe(401);
        trace.Outcome.ShouldBe(TraceOutcome.Rejected);
        trace.ApiId.ShouldBe("acme");
        trace.Stages.Select(s => s.Section).ShouldBe(["Frontend"]);
    }

    [Fact]
    public async Task A_short_circuited_request_records_no_backend_stage()
    {
        await using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        var returnResponse = new PolicyNode("return-response", new Dictionary<string, string>(),
            [new PolicyNode("set-status", new Dictionary<string, string> { ["code"] = "418" }, [], null)], null);
        PointDeadBackend(factory, new PolicyDocument([returnResponse], [], [], []));

        await client.GetAsync("/catalog/items");

        var trace = factory.Services.GetRequiredService<ITraceSink>().Recent(10).ShouldHaveSingleItem();
        trace.Outcome.ShouldBe(TraceOutcome.ShortCircuited);
        trace.StatusCode.ShouldBe(418);
        trace.Stages.Select(s => s.Section).ShouldBe(["Frontend", "Inbound", "Outbound"]);
    }

    [Fact]
    public async Task An_unsupported_policy_records_an_error_trace_naming_the_faulting_policy()
    {
        await using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        PointDeadBackend(factory, new PolicyDocument(
            [new PolicyNode("not-a-real-policy", new Dictionary<string, string>(), [], null)], [], [], []));

        await client.GetAsync("/catalog/items");

        var trace = factory.Services.GetRequiredService<ITraceSink>().Recent(10).ShouldHaveSingleItem();
        trace.StatusCode.ShouldBe(501);
        trace.Outcome.ShouldBe(TraceOutcome.Error);
        trace.Stages.SelectMany(s => s.Policies).Select(p => p.Name).ShouldContain("not-a-real-policy");
    }

    private static PolicyNode SetHeader(string name, string value) =>
        new("set-header", new Dictionary<string, string> { ["name"] = name },
            [new PolicyNode("value", new Dictionary<string, string>(), [], value)], null);

    private static void PointBackend(TestAppFactory factory, WireMockServer backend, PolicyDocument global)
    {
        var holder = factory.Services.GetRequiredService<GatewayConfigHolder>();
        var api = holder.Current.Apis[0] with { ServiceUrl = new Uri(backend.Url!), SubscriptionRequired = false };
        holder.Current = holder.Current with { Apis = [api], GlobalPolicy = global };
    }

    private static void PointDeadBackend(TestAppFactory factory, PolicyDocument global)
    {
        var holder = factory.Services.GetRequiredService<GatewayConfigHolder>();
        var api = holder.Current.Apis[0] with { ServiceUrl = new Uri("http://127.0.0.1:1"), SubscriptionRequired = false };
        holder.Current = holder.Current with { Apis = [api], GlobalPolicy = global };
    }
}
