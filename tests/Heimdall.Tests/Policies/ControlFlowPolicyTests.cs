using Heimdall.Application;
using Heimdall.Domain;
using Heimdall.Infrastructure;
using Heimdall.Infrastructure.Expressions;
using Heimdall.Infrastructure.Policies.ControlFlow;
using Heimdall.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Heimdall.Tests.Policies;

public class ControlFlowPolicyTests
{
    private static PolicyNode SetVariable(string name, string value) =>
        new("set-variable", new Dictionary<string, string> { ["name"] = name, ["value"] = value }, [], null);

    // --- set-variable ---

    [Fact]
    public async Task Set_variable_stores_a_typed_expression_result()
    {
        var policy = new SetVariablePolicy(new RoslynExpressionEvaluator());
        var ctx = PolicyContexts.For(PolicySection.Inbound);

        await policy.ApplyAsync(ctx, SetVariable("sum", "@(1 + 1)"));

        ctx.Variables["sum"].ShouldBe(2);
    }

    [Fact]
    public async Task Set_variable_stores_a_literal_as_a_string()
    {
        var policy = new SetVariablePolicy(new RoslynExpressionEvaluator());
        var ctx = PolicyContexts.For(PolicySection.Inbound);

        await policy.ApplyAsync(ctx, SetVariable("tier", "gold"));

        ctx.Variables["tier"].ShouldBe("gold");
    }

    // --- choose / when / otherwise ---

    private static ServiceProvider BuildProvider() => TestServices.Policies();

    private static PolicyNode When(string condition, params PolicyNode[] children) =>
        new("when", new Dictionary<string, string> { ["condition"] = condition }, children, null);

    private static PolicyNode Otherwise(params PolicyNode[] children) =>
        new("otherwise", new Dictionary<string, string>(), children, null);

    [Fact]
    public async Task Choose_runs_the_first_matching_when_branch()
    {
        using var provider = BuildProvider();
        var policy = new ChoosePolicy(provider);
        var ctx = PolicyContexts.For(PolicySection.Inbound);

        var node = new PolicyNode("choose", new Dictionary<string, string>(),
            [
                When("@(false)", SetVariable("branch", "first")),
                When("@(true)", SetVariable("branch", "second")),
                Otherwise(SetVariable("branch", "otherwise")),
            ], null);

        await policy.ApplyAsync(ctx, node);

        ctx.Variables["branch"].ShouldBe("second");
    }

    [Fact]
    public async Task Choose_runs_otherwise_when_no_when_matches()
    {
        using var provider = BuildProvider();
        var policy = new ChoosePolicy(provider);
        var ctx = PolicyContexts.For(PolicySection.Inbound);

        var node = new PolicyNode("choose", new Dictionary<string, string>(),
            [
                When("@(false)", SetVariable("branch", "first")),
                Otherwise(SetVariable("branch", "otherwise")),
            ], null);

        await policy.ApplyAsync(ctx, node);

        ctx.Variables["branch"].ShouldBe("otherwise");
    }
}
