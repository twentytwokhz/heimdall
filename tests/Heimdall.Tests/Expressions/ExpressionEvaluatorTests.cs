using Heimdall.Application;
using Heimdall.Infrastructure.Context;
using Heimdall.Infrastructure.Expressions;
using Shouldly;
using Xunit;

namespace Heimdall.Tests.Expressions;

public class ExpressionEvaluatorTests
{
    private static IPolicyContext Context(string body = "{}", params (string Name, string Value)[] headers)
    {
        var bag = headers.ToDictionary(h => h.Name, h => new[] { h.Value }, StringComparer.OrdinalIgnoreCase);
        return new PolicyContext
        {
            Request = new EmuRequest
            {
                Method = "GET",
                Url = new Uri("http://localhost/catalog/items"),
                Headers = bag,
                Body = new HttpEmuBody(body),
            },
            Api = new ApiInfo("acme", "Acme Platform API", ""),
            Operation = new OperationInfo("listCatalogItems", "GET", "/catalog/items"),
        };
    }

    [Fact]
    public void Evaluates_request_header_access()
    {
        var evaluator = new RoslynExpressionEvaluator();

        var value = evaluator.Evaluate<string>(
            "@(context.Request.Headers[\"X-Test\"][0])", Context(headers: ("X-Test", "hello")));

        value.ShouldBe("hello");
    }

    [Fact]
    public void Evaluates_jobject_body_indexing_via_newtonsoft()
    {
        var evaluator = new RoslynExpressionEvaluator();

        var value = evaluator.Evaluate<string>(
            "@(context.Request.Body.As<JObject>()[\"name\"].Value<string>())",
            Context(body: "{\"name\":\"widget\"}"));

        value.ShouldBe("widget");
    }

    [Fact]
    public void Evaluates_statement_block()
    {
        var evaluator = new RoslynExpressionEvaluator();

        var value = evaluator.Evaluate<int>("@{ var a = 2; var b = 3; return a * b; }", Context());

        value.ShouldBe(6);
    }

    [Fact]
    public void Reuses_compiled_delegate_on_repeated_evaluation()
    {
        var evaluator = new RoslynExpressionEvaluator();

        evaluator.Evaluate<int>("@(1 + 1)", Context());
        evaluator.Evaluate<int>("@(1 + 1)", Context());

        evaluator.CompilationCount.ShouldBe(1);
    }

    [Fact]
    public void Body_as_string_returns_raw_text()
    {
        var evaluator = new RoslynExpressionEvaluator();

        var value = evaluator.Evaluate<string>("@(context.Request.Body.As<string>())", Context(body: "raw-body"));

        value.ShouldBe("raw-body");
    }

    [Fact]
    public void Interpolate_returns_pure_literal_unchanged()
    {
        var evaluator = new RoslynExpressionEvaluator();

        evaluator.Interpolate("no expressions here", Context()).ShouldBe("no expressions here");
    }

    [Fact]
    public void Interpolate_evaluates_a_single_expression()
    {
        var evaluator = new RoslynExpressionEvaluator();

        evaluator.Interpolate("@(1 + 1)", Context()).ShouldBe("2");
    }

    [Fact]
    public void Interpolate_mixes_literals_and_expressions()
    {
        var evaluator = new RoslynExpressionEvaluator();

        var result = evaluator.Interpolate(
            "Hello @(context.Request.Headers[\"name\"][0])!", Context(headers: ("name", "world")));

        result.ShouldBe("Hello world!");
    }

    [Fact]
    public void Interpolate_evaluates_a_statement_block_segment()
    {
        var evaluator = new RoslynExpressionEvaluator();

        evaluator.Interpolate("x=@{ return 21 * 2; }", Context()).ShouldBe("x=42");
    }

    [Fact]
    public void Evaluate_handles_delimiter_inside_string_literal()
    {
        var evaluator = new RoslynExpressionEvaluator();

        // The ')' is inside a string literal and must not terminate the @(...) early.
        evaluator.Evaluate<string>("@(\"a)b\")", Context()).ShouldBe("a)b");
    }

    [Fact]
    public void Interpolate_handles_delimiter_inside_string_literal()
    {
        var evaluator = new RoslynExpressionEvaluator();

        evaluator.Interpolate("v=@(\"x)y\")", Context()).ShouldBe("v=x)y");
    }

    [Fact]
    public void Interpolate_unescapes_double_at()
    {
        var evaluator = new RoslynExpressionEvaluator();

        evaluator.Interpolate("user@@domain.com", Context()).ShouldBe("user@domain.com");
    }

    [Fact]
    public void Evaluate_throws_on_unbalanced_sigil()
    {
        var evaluator = new RoslynExpressionEvaluator();

        Should.Throw<ArgumentException>(() => evaluator.Evaluate<int>("@(1)(2)", Context()));
    }

    [Fact]
    public void Evaluates_request_id()
    {
        var evaluator = new RoslynExpressionEvaluator();
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var ctx = new PolicyContext
        {
            Request = new EmuRequest { Method = "GET", Url = new Uri("http://localhost/"), Headers = new Dictionary<string, string[]>(), Body = new HttpEmuBody("") },
            Api = new ApiInfo("a", "A", ""),
            Operation = new OperationInfo("o", "GET", "/"),
            RequestId = id,
        };

        evaluator.Evaluate<string>("@(context.RequestId.ToString())", ctx).ShouldBe("11111111-1111-1111-1111-111111111111");
    }

    [Fact]
    public void Evaluates_timestamp()
    {
        var evaluator = new RoslynExpressionEvaluator();
        var ts = new DateTimeOffset(2026, 6, 21, 10, 0, 0, TimeSpan.Zero);
        var ctx = new PolicyContext
        {
            Request = new EmuRequest { Method = "GET", Url = new Uri("http://localhost/"), Headers = new Dictionary<string, string[]>(), Body = new HttpEmuBody("") },
            Api = new ApiInfo("a", "A", ""),
            Operation = new OperationInfo("o", "GET", "/"),
            Timestamp = ts,
        };

        evaluator.Evaluate<int>("@(context.Timestamp.Year)", ctx).ShouldBe(2026);
    }
}
