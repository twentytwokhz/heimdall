using Heimdall.Application;
using Heimdall.Infrastructure.Context;
using Heimdall.Infrastructure.Expressions;
using Heimdall.Infrastructure.Policies.Transforms;
using Heimdall.Domain;
using Shouldly;
using Xunit;

namespace Heimdall.Tests.Policies;

public class SetHeaderPolicyTests
{
    private static PolicyContext Context(PolicySection section, params (string Name, string[] Values)[] requestHeaders)
    {
        var headers = requestHeaders.ToDictionary(h => h.Name, h => h.Values, StringComparer.OrdinalIgnoreCase);
        return new PolicyContext
        {
            Request = new EmuRequest
            {
                Method = "GET",
                Url = new Uri("http://localhost/"),
                Headers = headers,
                Body = new HttpEmuBody(""),
            },
            Api = new ApiInfo("a", "a", ""),
            Operation = new OperationInfo("o", "GET", "/"),
            Expressions = new RoslynExpressionEvaluator(),
            CurrentSection = section,
        };
    }

    private static PolicyNode SetHeader(string name, string? existsAction, params string[] values)
    {
        var attrs = new Dictionary<string, string> { ["name"] = name };
        if (existsAction is not null)
        {
            attrs["exists-action"] = existsAction;
        }
        var children = values.Select(v => new PolicyNode("value", new Dictionary<string, string>(), [], v)).ToArray();
        return new PolicyNode("set-header", attrs, children, null);
    }

    private static readonly SetHeaderPolicy Policy = new(new RoslynExpressionEvaluator());

    [Fact]
    public async Task Sets_request_header_in_inbound()
    {
        var ctx = Context(PolicySection.Inbound);

        await Policy.ApplyAsync(ctx, SetHeader("X-Trace", null, "abc"));

        ctx.Request.Headers["X-Trace"].ShouldBe(["abc"]);
    }

    [Fact]
    public async Task Sets_response_header_in_outbound()
    {
        var ctx = Context(PolicySection.Outbound);

        await Policy.ApplyAsync(ctx, SetHeader("X-Powered-By", null, "heimdall"));

        ctx.Response.Headers["X-Powered-By"].ShouldBe(["heimdall"]);
    }

    [Fact]
    public async Task Override_replaces_an_existing_header()
    {
        var ctx = Context(PolicySection.Inbound, ("X-Trace", ["old"]));

        await Policy.ApplyAsync(ctx, SetHeader("X-Trace", "override", "new"));

        ctx.Request.Headers["X-Trace"].ShouldBe(["new"]);
    }

    [Fact]
    public async Task Skip_keeps_an_existing_header()
    {
        var ctx = Context(PolicySection.Inbound, ("X-Trace", ["keep"]));

        await Policy.ApplyAsync(ctx, SetHeader("X-Trace", "skip", "ignored"));

        ctx.Request.Headers["X-Trace"].ShouldBe(["keep"]);
    }

    [Fact]
    public async Task Append_adds_to_an_existing_header()
    {
        var ctx = Context(PolicySection.Inbound, ("X-Trace", ["one"]));

        await Policy.ApplyAsync(ctx, SetHeader("X-Trace", "append", "two"));

        ctx.Request.Headers["X-Trace"].ShouldBe(["one", "two"]);
    }

    [Fact]
    public async Task Delete_removes_a_header()
    {
        var ctx = Context(PolicySection.Inbound, ("X-Trace", ["gone"]));

        await Policy.ApplyAsync(ctx, SetHeader("X-Trace", "delete"));

        ctx.Request.Headers.ContainsKey("X-Trace").ShouldBeFalse();
    }

    [Fact]
    public async Task Header_value_is_expression_interpolated()
    {
        var ctx = Context(PolicySection.Inbound);

        await Policy.ApplyAsync(ctx, SetHeader("X-Method", null, "method=@(context.Request.Method)"));

        ctx.Request.Headers["X-Method"].ShouldBe(["method=GET"]);
    }

    [Fact]
    public async Task Missing_name_attribute_fails_loud()
    {
        var ctx = Context(PolicySection.Inbound);
        var node = new PolicyNode("set-header", new Dictionary<string, string>(), [], null);

        await Should.ThrowAsync<PolicyException>(async () => await Policy.ApplyAsync(ctx, node));
    }
}
