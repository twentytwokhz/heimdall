using Heimdall.Application;
using Heimdall.Domain;
using Shouldly;
using Xunit;

namespace Heimdall.Tests.Loader;

public class PolicyValidationTests
{
    // A registry stub: only the listed elements are "supported"; Resolve is unused by validation.
    private sealed class FakeRegistry(params string[] supported) : IPolicyRegistry
    {
        private readonly HashSet<string> _supported = new(supported, StringComparer.Ordinal);
        public bool IsSupported(string elementName) => _supported.Contains(elementName);
        public IPolicy Resolve(string elementName) => throw new NotSupportedException();
    }

    private static PolicyDocument Inbound(params PolicyNode[] nodes) => new(nodes, [], [], []);
    private static PolicyNode Node(string name, params PolicyNode[] children) =>
        new(name, new Dictionary<string, string>(), children, null);

    [Fact]
    public void Accepts_supported_policies_and_structural_base()
    {
        var doc = Inbound(Node("base"), Node("set-header"));

        Should.NotThrow(() => PolicyValidation.Validate(doc, new FakeRegistry("set-header")));
    }

    [Fact]
    public void Allows_include_fragment_without_a_registry_entry()
    {
        var doc = Inbound(Node("include-fragment"));

        Should.NotThrow(() => PolicyValidation.Validate(doc, new FakeRegistry()));
    }

    [Fact]
    public void Rejects_an_unsupported_policy_naming_it()
    {
        var doc = Inbound(Node("frobnicate"));

        var ex = Should.Throw<UnsupportedPolicyException>(
            () => PolicyValidation.Validate(doc, new FakeRegistry("set-header")));
        ex.ElementName.ShouldBe("frobnicate");
    }

    [Fact]
    public void Recurses_into_choose_branches_to_catch_nested_unsupported_policies()
    {
        var doc = Inbound(Node("choose", Node("when", Node("frobnicate")), Node("otherwise", Node("set-status"))));

        var ex = Should.Throw<UnsupportedPolicyException>(
            () => PolicyValidation.Validate(doc, new FakeRegistry("choose", "set-status")));
        ex.ElementName.ShouldBe("frobnicate");
    }

    [Fact]
    public void Accepts_a_well_formed_choose()
    {
        var doc = Inbound(Node("choose", Node("when", Node("set-header")), Node("otherwise", Node("set-status"))));

        Should.NotThrow(() =>
            PolicyValidation.Validate(doc, new FakeRegistry("choose", "set-header", "set-status")));
    }
}
