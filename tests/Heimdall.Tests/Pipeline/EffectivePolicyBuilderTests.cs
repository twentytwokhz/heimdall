using Heimdall.Application;
using Heimdall.Domain;
using Shouldly;
using Xunit;

namespace Heimdall.Tests.Pipeline;

public class EffectivePolicyBuilderTests
{
    private static PolicyNode Base() => new("base", new Dictionary<string, string>(), [], null);

    private static PolicyNode Marker(string name) => new(name, new Dictionary<string, string>(), [], null);

    private static PolicyDocument Inbound(params PolicyNode[] nodes) => new(nodes, [], [], []);

    private static PolicyDocument Outbound(params PolicyNode[] nodes) => new([], [], nodes, []);

    private static IReadOnlyList<string> Names(IReadOnlyList<PolicyNode> nodes) => nodes.Select(n => n.Name).ToList();

    private static readonly EffectivePolicyBuilder Builder = new();

    [Fact]
    public void All_null_scopes_yield_empty_sections()
    {
        var result = Builder.Build([null, null]);

        result.Inbound.ShouldBeEmpty();
        result.Backend.ShouldBeEmpty();
        result.Outbound.ShouldBeEmpty();
        result.OnError.ShouldBeEmpty();
    }

    [Fact]
    public void Base_only_inner_scope_inherits_parent()
    {
        var result = Builder.Build([
            Inbound(Marker("A")),
            Inbound(Base())
        ]);

        Names(result.Inbound).ShouldBe(["A"]);
    }

    [Fact]
    public void Base_is_spliced_in_place_with_surrounding_nodes()
    {
        var result = Builder.Build([
            Inbound(Marker("A")),
            Inbound(Marker("B"), Base(), Marker("C"))
        ]);

        Names(result.Inbound).ShouldBe(["B", "A", "C"]);
    }

    [Fact]
    public void Omitting_base_drops_parent_policies()
    {
        var result = Builder.Build([
            Inbound(Marker("A")),
            Inbound(Marker("B"))
        ]);

        Names(result.Inbound).ShouldBe(["B"]);
    }

    [Fact]
    public void Three_scopes_flatten_through_the_chain()
    {
        var result = Builder.Build([
            Inbound(Marker("A")),
            Inbound(Base()),
            Inbound(Marker("X"), Base())
        ]);

        Names(result.Inbound).ShouldBe(["X", "A"]);
    }

    [Fact]
    public void Base_at_outermost_scope_resolves_to_empty_and_does_not_throw()
    {
        var result = Builder.Build([Inbound(Base())]);

        result.Inbound.ShouldBeEmpty();
    }

    [Fact]
    public void Null_middle_scope_passes_parent_through()
    {
        var result = Builder.Build([
            Inbound(Marker("A")),
            null,
            Inbound(Base())
        ]);

        Names(result.Inbound).ShouldBe(["A"]);
    }

    [Fact]
    public void Outbound_section_flattens_with_the_same_rule()
    {
        var result = Builder.Build([
            Outbound(Marker("A")),
            Outbound(Marker("B"), Base())
        ]);

        Names(result.Outbound).ShouldBe(["B", "A"]);
    }

    private static PolicyNode IncludeFragment(string id) =>
        new("include-fragment", new Dictionary<string, string> { ["fragment-id"] = id }, [], null);

    [Fact]
    public void Include_fragment_is_inlined_at_flatten_time()
    {
        var fragments = new Dictionary<string, IReadOnlyList<PolicyNode>>
        {
            ["auth"] = [Marker("check-header"), Marker("set-header")],
        };

        var result = Builder.Build([Inbound(Marker("A"), IncludeFragment("auth"), Marker("B"))], fragments);

        Names(result.Inbound).ShouldBe(["A", "check-header", "set-header", "B"]);
    }

    [Fact]
    public void Include_fragment_inside_a_branch_is_inlined()
    {
        var fragments = new Dictionary<string, IReadOnlyList<PolicyNode>>
        {
            ["frag"] = [Marker("set-variable")],
        };
        var when = new PolicyNode("when", new Dictionary<string, string>(), [IncludeFragment("frag")], null);
        var choose = new PolicyNode("choose", new Dictionary<string, string>(), [when], null);

        var result = Builder.Build([Inbound(choose)], fragments);

        Names(result.Inbound[0].Children[0].Children).ShouldBe(["set-variable"]);
    }

    [Fact]
    public void Unknown_fragment_fails_loud()
    {
        Should.Throw<InvalidOperationException>(() =>
            Builder.Build([Inbound(IncludeFragment("missing"))], new Dictionary<string, IReadOnlyList<PolicyNode>>()));
    }
}
