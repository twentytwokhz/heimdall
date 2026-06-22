using Heimdall.Application;
using Heimdall.Domain;
using Heimdall.Infrastructure.Context;
using Heimdall.Infrastructure.Expressions;
using Heimdall.Infrastructure.Policies.Transforms;
using Heimdall.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace Heimdall.Tests.Policies;

public class ResponseTransformPolicyTests
{
    private static readonly IExpressionEvaluator Expr = new RoslynExpressionEvaluator();

    private static PolicyNode Node(string name, params (string, string)[] attrs) =>
        new(name, attrs.ToDictionary(a => a.Item1, a => a.Item2), [], null);

    [Fact]
    public async Task Set_status_sets_the_response_code()
    {
        var policy = new SetStatusPolicy();
        var ctx = PolicyContexts.For(PolicySection.Outbound);
        ctx.Response.StatusCode = 200;

        await policy.ApplyAsync(ctx, Node("set-status", ("code", "401"), ("reason", "Unauthorized")));

        ctx.Response.StatusCode.ShouldBe(401);
    }

    [Fact]
    public async Task Set_status_missing_code_fails_loud()
    {
        var policy = new SetStatusPolicy();
        var ctx = PolicyContexts.For(PolicySection.Outbound);

        await Should.ThrowAsync<PolicyException>(async () => await policy.ApplyAsync(ctx, Node("set-status")));
    }

    [Fact]
    public async Task Find_and_replace_rewrites_the_response_body()
    {
        var policy = new FindAndReplacePolicy(Expr);
        var ctx = PolicyContexts.For(PolicySection.Outbound);
        ctx.Response.Body = new HttpEmuBody("hello world");

        await policy.ApplyAsync(ctx, Node("find-and-replace", ("from", "world"), ("to", "heimdall")));

        ctx.Response.Body!.As<string>().ShouldBe("hello heimdall");
    }

    [Fact]
    public async Task Find_and_replace_rewrites_all_occurrences_in_the_request_body()
    {
        var policy = new FindAndReplacePolicy(Expr);
        var ctx = PolicyContexts.For(PolicySection.Inbound, body: "foo bar foo");

        await policy.ApplyAsync(ctx, Node("find-and-replace", ("from", "foo"), ("to", "X")));

        ctx.Request.Body.As<string>().ShouldBe("X bar X");
    }
}
