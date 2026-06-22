using Heimdall.Application;
using Heimdall.Infrastructure.Expressions;
using Heimdall.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace Heimdall.Tests.Expressions;

public class NamedValueSubstitutionTests
{
    private sealed class FakeNamedValues(Dictionary<string, string> values) : INamedValues
    {
        public bool TryResolve(string name, out string value) => values.TryGetValue(name, out value!);
        public string Resolve(string name) =>
            values.TryGetValue(name, out var v) ? v : throw new InvalidOperationException($"Named value '{name}' is not defined.");
    }

    private static IPolicyContext Context(params (string Name, string Value)[] values) =>
        PolicyContexts.For(PolicySection.Inbound,
            namedValues: new FakeNamedValues(values.ToDictionary(v => v.Name, v => v.Value)));

    [Fact]
    public void Interpolate_substitutes_a_named_value()
    {
        var evaluator = new RoslynExpressionEvaluator();

        var result = evaluator.Interpolate("url={{backend-url}}", Context(("backend-url", "http://acme")));

        result.ShouldBe("url=http://acme");
    }

    [Fact]
    public void Named_value_is_substituted_inside_an_expression()
    {
        var evaluator = new RoslynExpressionEvaluator();

        var result = evaluator.Interpolate("@(\"{{greeting}}\".ToUpperInvariant())", Context(("greeting", "hello")));

        result.ShouldBe("HELLO");
    }

    [Fact]
    public void Unknown_named_value_fails_loud()
    {
        var evaluator = new RoslynExpressionEvaluator();

        Should.Throw<InvalidOperationException>(() => evaluator.Interpolate("{{absent}}", Context()));
    }
}
