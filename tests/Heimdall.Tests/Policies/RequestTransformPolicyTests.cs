using Heimdall.Application;
using Heimdall.Domain;
using Heimdall.Infrastructure.Expressions;
using Heimdall.Infrastructure.Policies.Transforms;
using Heimdall.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace Heimdall.Tests.Policies;

public class RequestTransformPolicyTests
{
    private static readonly IExpressionEvaluator Expr = new RoslynExpressionEvaluator();

    private static PolicyNode Node(string name, (string, string)[] attrs, IReadOnlyList<PolicyNode> children, string? text = null) =>
        new(name, attrs.ToDictionary(a => a.Item1, a => a.Item2), children, text);

    private static PolicyNode Value(string text) => new("value", new Dictionary<string, string>(), [], text);

    // --- set-method ---

    [Fact]
    public async Task Set_method_changes_the_request_method()
    {
        var policy = new SetMethodPolicy(Expr);
        var ctx = PolicyContexts.For(PolicySection.Inbound, method: "GET");

        await policy.ApplyAsync(ctx, Node("set-method", [], [], "POST"));

        ctx.Request.Method.ShouldBe("POST");
    }

    // --- rewrite-uri ---

    [Fact]
    public async Task Rewrite_uri_replaces_the_path()
    {
        var policy = new RewriteUriPolicy(Expr);
        var ctx = PolicyContexts.For(PolicySection.Inbound, url: "http://localhost/catalog/items");

        await policy.ApplyAsync(ctx, Node("rewrite-uri", [("template", "/v2/products")], []));

        ctx.Request.Url.PathAndQuery.ShouldBe("/v2/products");
    }

    [Fact]
    public async Task Rewrite_uri_keeps_the_existing_query_by_default()
    {
        var policy = new RewriteUriPolicy(Expr);
        var ctx = PolicyContexts.For(PolicySection.Inbound, url: "http://localhost/catalog/items?page=2");

        await policy.ApplyAsync(ctx, Node("rewrite-uri", [("template", "/v2/products")], []));

        ctx.Request.Url.PathAndQuery.ShouldBe("/v2/products?page=2");
    }

    // --- set-query-parameter ---

    [Fact]
    public async Task Set_query_parameter_adds_a_parameter()
    {
        var policy = new SetQueryParameterPolicy(Expr);
        var ctx = PolicyContexts.For(PolicySection.Inbound, url: "http://localhost/catalog/items");

        await policy.ApplyAsync(ctx, Node("set-query-parameter", [("name", "api-version")], [Value("2024-01")]));

        ctx.Request.Url.Query.ShouldContain("api-version=2024-01");
    }

    [Fact]
    public async Task Set_query_parameter_override_replaces_an_existing_value()
    {
        var policy = new SetQueryParameterPolicy(Expr);
        var ctx = PolicyContexts.For(PolicySection.Inbound, url: "http://localhost/catalog/items?api-version=old");

        await policy.ApplyAsync(ctx, Node("set-query-parameter", [("name", "api-version"), ("exists-action", "override")], [Value("new")]));

        ctx.Request.Url.Query.ShouldBe("?api-version=new");
    }

    // --- set-body ---

    [Fact]
    public async Task Set_body_replaces_the_request_body_in_inbound()
    {
        var policy = new SetBodyPolicy(Expr);
        var ctx = PolicyContexts.For(PolicySection.Inbound, body: "old");

        await policy.ApplyAsync(ctx, Node("set-body", [], [], "new-body"));

        ctx.Request.Body.As<string>().ShouldBe("new-body");
    }

    [Fact]
    public async Task Set_body_replaces_the_response_body_in_outbound()
    {
        var policy = new SetBodyPolicy(Expr);
        var ctx = PolicyContexts.For(PolicySection.Outbound);
        ctx.Response.Body = new Heimdall.Infrastructure.Context.HttpEmuBody("backend");

        await policy.ApplyAsync(ctx, Node("set-body", [], [], "rewritten"));

        ctx.Response.Body!.As<string>().ShouldBe("rewritten");
    }

    [Fact]
    public async Task Set_body_interpolates_expressions()
    {
        var policy = new SetBodyPolicy(Expr);
        var ctx = PolicyContexts.For(PolicySection.Inbound, method: "PUT");

        await policy.ApplyAsync(ctx, Node("set-body", [], [], "@(context.Request.Method)"));

        ctx.Request.Body.As<string>().ShouldBe("PUT");
    }
}
