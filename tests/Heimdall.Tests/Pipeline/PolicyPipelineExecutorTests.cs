using Heimdall.Api.Forwarding;
using Heimdall.Api.Pipeline;
using Heimdall.Application;
using Heimdall.Domain;
using Heimdall.Infrastructure.Context;
using Microsoft.AspNetCore.Http;
using Shouldly;
using Xunit;

namespace Heimdall.Tests.Pipeline;

/// <summary>The four-stage runtime semantics from IMPLEMENTATION.md section 6, driven with fakes.</summary>
public class PolicyPipelineExecutorTests
{
    private sealed class RecordingForwarder : IForwarder
    {
        public bool Called { get; private set; }
        public ValueTask ForwardAsync(HttpContext httpContext, IPolicyContext context, Uri destination, CancellationToken ct = default)
        {
            Called = true;
            context.Response.StatusCode = 200;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeRegistry(IReadOnlyDictionary<string, IPolicy> map) : IPolicyRegistry
    {
        public bool IsSupported(string elementName) => map.ContainsKey(elementName);
        public IPolicy Resolve(string elementName) =>
            map.TryGetValue(elementName, out var p) ? p : throw new UnsupportedPolicyException(elementName);
    }

    private sealed class LambdaPolicy(string name, Func<IPolicyContext, ValueTask> apply) : IPolicy
    {
        public string ElementName => name;
        public PolicySection Sections => PolicySection.All;
        public ValueTask ApplyAsync(IPolicyContext context, PolicyNode node, CancellationToken ct = default) => apply(context);
    }

    private static PolicyNode Node(string name) => new(name, new Dictionary<string, string>(), [], null);

    private static IPolicyContext NewContext() => new PolicyContext
    {
        Request = new EmuRequest
        {
            Method = "GET",
            Url = new Uri("http://localhost/"),
            Headers = new Dictionary<string, string[]>(),
            Body = new HttpEmuBody(""),
        },
        Api = new ApiInfo("a", "a", ""),
        Operation = new OperationInfo("o", "GET", "/"),
    };

    private static readonly Uri Backend = new("http://backend");

    private static PolicyPipelineExecutor Executor(IPolicyRegistry registry, IForwarder forwarder) => new(registry, forwarder);

    [Fact]
    public async Task Happy_path_runs_inbound_then_forwards_then_outbound()
    {
        var order = new List<string>();
        var forwarder = new RecordingForwarder();
        var registry = new FakeRegistry(new Dictionary<string, IPolicy>
        {
            ["mark-in"] = new LambdaPolicy("mark-in", c => { order.Add("inbound"); return ValueTask.CompletedTask; }),
            ["mark-out"] = new LambdaPolicy("mark-out", c => { order.Add("outbound"); return ValueTask.CompletedTask; }),
        });
        var policy = new EffectivePolicy([Node("mark-in")], [], [Node("mark-out")], []);
        var ctx = NewContext();

        await Executor(registry, forwarder).ExecuteAsync(new DefaultHttpContext(), ctx, policy, Backend, default);

        forwarder.Called.ShouldBeTrue();
        order.ShouldBe(["inbound", "outbound"]);
        ctx.Response.StatusCode.ShouldBe(200);
    }

    [Fact]
    public async Task Short_circuit_in_inbound_skips_forward_but_runs_outbound()
    {
        var outboundRan = false;
        var forwarder = new RecordingForwarder();
        var registry = new FakeRegistry(new Dictionary<string, IPolicy>
        {
            ["return"] = new LambdaPolicy("return", c => { c.ShortCircuited = true; c.Response.StatusCode = 418; return ValueTask.CompletedTask; }),
            ["mark-out"] = new LambdaPolicy("mark-out", c => { outboundRan = true; return ValueTask.CompletedTask; }),
        });
        var policy = new EffectivePolicy([Node("return")], [], [Node("mark-out")], []);
        var ctx = NewContext();

        await Executor(registry, forwarder).ExecuteAsync(new DefaultHttpContext(), ctx, policy, Backend, default);

        forwarder.Called.ShouldBeFalse();
        outboundRan.ShouldBeTrue();
        ctx.Response.StatusCode.ShouldBe(418);
    }

    [Fact]
    public async Task Short_circuit_stops_remaining_policies_in_the_same_section()
    {
        var secondRan = false;
        var registry = new FakeRegistry(new Dictionary<string, IPolicy>
        {
            ["return"] = new LambdaPolicy("return", c => { c.ShortCircuited = true; return ValueTask.CompletedTask; }),
            ["never"] = new LambdaPolicy("never", c => { secondRan = true; return ValueTask.CompletedTask; }),
        });
        var policy = new EffectivePolicy([Node("return"), Node("never")], [], [], []);
        var ctx = NewContext();

        await Executor(registry, new RecordingForwarder()).ExecuteAsync(new DefaultHttpContext(), ctx, policy, Backend, default);

        secondRan.ShouldBeFalse();
    }

    [Fact]
    public async Task Policy_error_skips_forward_routes_to_on_error_and_populates_last_error()
    {
        LastErrorInfo? seen = null;
        var forwarder = new RecordingForwarder();
        var registry = new FakeRegistry(new Dictionary<string, IPolicy>
        {
            ["boom"] = new LambdaPolicy("boom", _ => throw new PolicyException("boom", "Failed", "kaboom")),
            ["handler"] = new LambdaPolicy("handler", c => { seen = c.LastError; return ValueTask.CompletedTask; }),
        });
        var policy = new EffectivePolicy([Node("boom")], [], [], [Node("handler")]);
        var ctx = NewContext();

        await Executor(registry, forwarder).ExecuteAsync(new DefaultHttpContext(), ctx, policy, Backend, default);

        forwarder.Called.ShouldBeFalse();
        seen.ShouldNotBeNull();
        seen!.Source.ShouldBe("boom");
        seen.Reason.ShouldBe("Failed");
    }

    [Fact]
    public async Task An_error_inside_on_error_yields_a_clean_500_without_recursing()
    {
        var registry = new FakeRegistry(new Dictionary<string, IPolicy>
        {
            ["boom"] = new LambdaPolicy("boom", _ => throw new PolicyException("boom", "Failed", "kaboom")),
            ["boom-again"] = new LambdaPolicy("boom-again", _ => throw new PolicyException("boom-again", "Failed", "second")),
        });
        var policy = new EffectivePolicy([Node("boom")], [], [], [Node("boom-again")]);
        var ctx = NewContext();

        // Must not propagate out of ExecuteAsync (no infinite re-routing into on-error).
        await Executor(registry, new RecordingForwarder()).ExecuteAsync(new DefaultHttpContext(), ctx, policy, Backend, default);

        ctx.Response.StatusCode.ShouldBe(500);
        ctx.Response.Body.ShouldNotBeNull();
    }

    [Fact]
    public async Task Current_section_is_visible_to_policies()
    {
        PolicySection inboundSeen = default, outboundSeen = default;
        var registry = new FakeRegistry(new Dictionary<string, IPolicy>
        {
            ["in"] = new LambdaPolicy("in", c => { inboundSeen = c.CurrentSection; return ValueTask.CompletedTask; }),
            ["out"] = new LambdaPolicy("out", c => { outboundSeen = c.CurrentSection; return ValueTask.CompletedTask; }),
        });
        var policy = new EffectivePolicy([Node("in")], [], [Node("out")], []);
        var ctx = NewContext();

        await Executor(registry, new RecordingForwarder()).ExecuteAsync(new DefaultHttpContext(), ctx, policy, Backend, default);

        inboundSeen.ShouldBe(PolicySection.Inbound);
        outboundSeen.ShouldBe(PolicySection.Outbound);
    }
}
