using Heimdall.Application;
using Heimdall.Domain;
using Heimdall.Infrastructure;
using Heimdall.Infrastructure.Policies.Routing;
using Heimdall.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Heimdall.Tests.Policies;

public class RoutingPolicyTests
{
    private static PolicyNode Node(string name, (string, string)[] attrs, params PolicyNode[] children) =>
        new(name, attrs.ToDictionary(a => a.Item1, a => a.Item2), children, null);

    private static PolicyNode Leaf(string name, (string, string)[] attrs, string? text = null) =>
        new(name, attrs.ToDictionary(a => a.Item1, a => a.Item2), [], text);

    // --- set-backend-service ---

    [Fact]
    public async Task Set_backend_service_sets_the_backend_url()
    {
        var policy = new SetBackendServicePolicy(new Heimdall.Infrastructure.Expressions.RoslynExpressionEvaluator());
        var ctx = PolicyContexts.For(PolicySection.Backend);

        await policy.ApplyAsync(ctx, Node("set-backend-service", [("base-url", "http://other-backend:9000")]));

        ctx.BackendServiceUrl.ShouldBe(new Uri("http://other-backend:9000"));
    }

    // --- mock-response ---

    [Fact]
    public async Task Mock_response_short_circuits_with_the_given_status_and_content_type()
    {
        var policy = new MockResponsePolicy();
        var ctx = PolicyContexts.For(PolicySection.Inbound);

        await policy.ApplyAsync(ctx, Node("mock-response", [("status-code", "201"), ("content-type", "application/json")]));

        ctx.ShortCircuited.ShouldBeTrue();
        ctx.Response.StatusCode.ShouldBe(201);
        ctx.Response.Headers["Content-Type"].ShouldBe(["application/json"]);
    }

    // --- return-response (runs its children against the response, then short-circuits) ---

    [Fact]
    public async Task Return_response_builds_the_response_from_children_and_short_circuits()
    {
        using var provider = TestServices.Policies();
        var policy = new ReturnResponsePolicy(provider);
        var ctx = PolicyContexts.For(PolicySection.Inbound);

        var node = Node("return-response", [],
            Leaf("set-status", [("code", "403")]),
            Leaf("set-header", [("name", "X-Reason")], null) with { Children = [new PolicyNode("value", new Dictionary<string, string>(), [], "denied")] },
            Leaf("set-body", [], "nope"));

        await policy.ApplyAsync(ctx, node);

        ctx.ShortCircuited.ShouldBeTrue();
        ctx.Response.StatusCode.ShouldBe(403);
        ctx.Response.Headers["X-Reason"].ShouldBe(["denied"]);
        ctx.Response.Body!.As<string>().ShouldBe("nope");
        ctx.CurrentSection.ShouldBe(PolicySection.Inbound); // restored after running children
    }
}
