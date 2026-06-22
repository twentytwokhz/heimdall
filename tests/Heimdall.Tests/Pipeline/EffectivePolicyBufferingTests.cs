using Heimdall.Application;
using Heimdall.Domain;
using Shouldly;
using Xunit;

namespace Heimdall.Tests.Pipeline;

/// <summary>
/// The opt-in body-buffering decision: an effective policy only needs the request body spooled when
/// a policy actually re-reads it (find-and-replace on the request, or an expression touching Request.Body).
/// </summary>
public class EffectivePolicyBufferingTests
{
    private static PolicyNode Node(string name, string? rawText = null, params PolicyNode[] children) =>
        new(name, new Dictionary<string, string>(), children, rawText);

    [Fact]
    public void No_body_policy_does_not_require_buffering()
    {
        var policy = new EffectivePolicy([Node("set-header")], [], [], []);

        policy.RequiresBodyBuffering.ShouldBeFalse();
    }

    [Fact]
    public void An_expression_re_reading_the_request_body_requires_buffering()
    {
        var value = new PolicyNode("value", new Dictionary<string, string>(), [], "@(context.Request.Body.As<string>())");
        var setHeader = new PolicyNode("set-header", new Dictionary<string, string> { ["name"] = "X-Echo" }, [value], null);
        var policy = new EffectivePolicy([setHeader], [], [], []);

        policy.RequiresBodyBuffering.ShouldBeTrue();
    }

    [Fact]
    public void Find_and_replace_on_the_request_requires_buffering()
    {
        var policy = new EffectivePolicy([Node("find-and-replace")], [], [], []);

        policy.RequiresBodyBuffering.ShouldBeTrue();
    }

    [Fact]
    public void Find_and_replace_only_in_outbound_does_not_require_request_buffering()
    {
        var policy = new EffectivePolicy([], [], [Node("find-and-replace")], []);

        policy.RequiresBodyBuffering.ShouldBeFalse();
    }

    [Fact]
    public void A_literal_set_body_does_not_require_buffering()
    {
        var policy = new EffectivePolicy([Node("set-body", "a literal replacement")], [], [], []);

        policy.RequiresBodyBuffering.ShouldBeFalse();
    }
}
