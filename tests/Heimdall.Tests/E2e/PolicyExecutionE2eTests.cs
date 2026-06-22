using System.Net;
using Heimdall.Api.Configuration;
using Heimdall.Domain;
using Heimdall.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace Heimdall.Tests.E2e;

/// <summary>
/// End-to-end proof that the pipeline executes the effective policy (Batch 0 exit criterion):
/// an inbound set-header mutation reaches the backend, and the captured response is written back.
/// </summary>
[Collection("gateway-e2e")]
public class PolicyExecutionE2eTests
{
    [Fact]
    public async Task Inbound_set_header_mutation_reaches_the_backend()
    {
        using var backend = WireMockServer.Start();
        backend
            .Given(Request.Create().WithPath("/catalog/items").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("""{"items":["widget"]}"""));

        await using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        PointAndInjectPolicy(factory, backend, new PolicyDocument([SetHeader("X-Heimdall", "injected")], [], [], []));

        var response = await client.GetAsync("/catalog/items");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldContain("widget");
        var received = backend.LogEntries.Single().RequestMessage!.Headers!;
        received["X-Heimdall"].ShouldContain("injected");
    }

    [Fact]
    public async Task Set_method_and_rewrite_uri_change_what_the_backend_receives()
    {
        using var backend = WireMockServer.Start();
        backend
            .Given(Request.Create().WithPath("/v2/products").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("created"));

        await using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        var inbound = new PolicyDocument(
            [
                new PolicyNode("set-method", new Dictionary<string, string>(), [], "POST"),
                new PolicyNode("rewrite-uri", new Dictionary<string, string> { ["template"] = "/v2/products" }, [], null),
            ],
            [], [], []);
        PointAndInjectPolicy(factory, backend, inbound);

        var response = await client.GetAsync("/catalog/items");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).ShouldBe("created");
        var received = backend.LogEntries.Single().RequestMessage!;
        received.Method.ShouldBe("POST");
        received.Path.ShouldBe("/v2/products");
    }

    [Fact]
    public async Task Validate_jwt_rejects_a_request_with_no_token()
    {
        await using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        var key = new PolicyNode("key", new Dictionary<string, string>(), [],
            Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("heimdall-test-signing-secret-of-32+bytes!!")));
        var validateJwt = new PolicyNode("validate-jwt",
            new Dictionary<string, string> { ["failed-validation-httpcode"] = "401" },
            [new PolicyNode("issuer-signing-keys", new Dictionary<string, string>(), [key], null)], null);
        InjectGlobalWithDeadBackend(factory, new PolicyDocument([validateJwt], [], [], []));

        var response = await client.GetAsync("/catalog/items");

        ((int)response.StatusCode).ShouldBe(401);
    }

    [Fact]
    public async Task Cache_hit_is_served_without_calling_the_backend()
    {
        using var backend = WireMockServer.Start();
        backend
            .Given(Request.Create().WithPath("/catalog/items").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("from-backend"));

        await using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        var policy = new PolicyDocument(
            [new PolicyNode("cache-lookup", new Dictionary<string, string>(), [], null)],
            [],
            [new PolicyNode("cache-store", new Dictionary<string, string> { ["duration"] = "60" }, [], null)],
            []);
        PointAndInjectPolicy(factory, backend, policy);

        var first = await client.GetAsync("/catalog/items");
        var second = await client.GetAsync("/catalog/items");

        (await first.Content.ReadAsStringAsync()).ShouldBe("from-backend");
        (await second.Content.ReadAsStringAsync()).ShouldBe("from-backend");
        backend.LogEntries.Count().ShouldBe(1); // the second request was served from cache
    }

    [Fact]
    public async Task Choose_branch_selection_reaches_the_backend()
    {
        using var backend = WireMockServer.Start();
        backend
            .Given(Request.Create().WithPath("/catalog/items").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("ok"));

        await using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        var when = new PolicyNode("when",
            new Dictionary<string, string> { ["condition"] = "@(context.Request.Headers.ContainsKey(\"X-Tier\"))" },
            [SetHeader("X-Route", "premium")], null);
        var otherwise = new PolicyNode("otherwise", new Dictionary<string, string>(), [SetHeader("X-Route", "standard")], null);
        var choose = new PolicyNode("choose", new Dictionary<string, string>(), [when, otherwise], null);
        PointAndInjectPolicy(factory, backend, new PolicyDocument([choose], [], [], []));

        var request = new HttpRequestMessage(HttpMethod.Get, "/catalog/items");
        request.Headers.Add("X-Tier", "gold");
        await client.SendAsync(request);

        backend.LogEntries.Single().RequestMessage!.Headers!["X-Route"].ShouldContain("premium");
    }

    [Fact]
    public async Task Rate_limit_exceeded_returns_429_with_retry_after()
    {
        using var backend = WireMockServer.Start();
        backend
            .Given(Request.Create().WithPath("/catalog/items").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("ok"));

        await using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        var rateLimit = new PolicyNode("rate-limit",
            new Dictionary<string, string> { ["calls"] = "1", ["renewal-period"] = "60" }, [], null);
        PointAndInjectPolicy(factory, backend, new PolicyDocument([rateLimit], [], [], []));

        var first = await client.GetAsync("/catalog/items");
        var second = await client.GetAsync("/catalog/items");

        first.StatusCode.ShouldBe(HttpStatusCode.OK);
        ((int)second.StatusCode).ShouldBe(429);
        // Epoch-aligned window: Retry-After is the seconds left until the window boundary (<= the period).
        int.Parse(second.Headers.GetValues("Retry-After").Single()).ShouldBeInRange(1, 60);
        backend.LogEntries.Count().ShouldBe(1); // the throttled request never reached the backend
    }

    [Fact]
    public async Task Unsupported_policy_element_fails_loud_with_501()
    {
        await using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        InjectGlobalWithDeadBackend(factory,
            new PolicyDocument([new PolicyNode("not-a-real-policy", new Dictionary<string, string>(), [], null)], [], [], []));

        var response = await client.GetAsync("/catalog/items");

        ((int)response.StatusCode).ShouldBe(501);
    }

    [Fact]
    public async Task Return_response_short_circuits_without_a_backend()
    {
        await using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        var returnResponse = new PolicyNode("return-response", new Dictionary<string, string>(),
            [
                new PolicyNode("set-status", new Dictionary<string, string> { ["code"] = "418" }, [], null),
                new PolicyNode("set-body", new Dictionary<string, string>(), [], "teapot"),
            ], null);
        InjectGlobalWithDeadBackend(factory, new PolicyDocument([returnResponse], [], [], []));

        var response = await client.GetAsync("/catalog/items");

        ((int)response.StatusCode).ShouldBe(418);
        (await response.Content.ReadAsStringAsync()).ShouldBe("teapot");
    }

    [Fact]
    public async Task Mock_response_short_circuits_without_a_backend()
    {
        await using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        var mock = new PolicyNode("mock-response", new Dictionary<string, string> { ["status-code"] = "503" }, [], null);
        InjectGlobalWithDeadBackend(factory, new PolicyDocument([mock], [], [], []));

        var response = await client.GetAsync("/catalog/items");

        ((int)response.StatusCode).ShouldBe(503);
    }

    // A deliberately unreachable backend: if the pipeline forwarded, the test would fail (502/timeout),
    // so a short-circuit response proves no forward happened.
    private static void InjectGlobalWithDeadBackend(TestAppFactory factory, PolicyDocument global)
    {
        var holder = factory.Services.GetRequiredService<GatewayConfigHolder>();
        var api = holder.Current.Apis[0] with { ServiceUrl = new Uri("http://127.0.0.1:1"), SubscriptionRequired = false };
        holder.Current = holder.Current with { Apis = [api], GlobalPolicy = global };
    }

    [Fact]
    public async Task Named_value_token_is_substituted_before_reaching_the_backend()
    {
        using var backend = WireMockServer.Start();
        backend
            .Given(Request.Create().WithPath("/catalog/items").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("ok"));

        await using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        var holder = factory.Services.GetRequiredService<GatewayConfigHolder>();
        var api = holder.Current.Apis[0] with { ServiceUrl = new Uri(backend.Url!), SubscriptionRequired = false };
        holder.Current = holder.Current with
        {
            Apis = [api],
            NamedValues = [new NamedValue("backend-tag", "acme-prod", Secret: false)],
            GlobalPolicy = new PolicyDocument([SetHeader("X-Backend-Tag", "{{backend-tag}}")], [], [], []),
        };

        await client.GetAsync("/catalog/items");

        backend.LogEntries.Single().RequestMessage!.Headers!["X-Backend-Tag"].ShouldContain("acme-prod");
    }

    [Fact]
    public async Task Post_body_streams_to_the_backend_when_no_policy_reads_it()
    {
        using var backend = WireMockServer.Start();
        backend
            .Given(Request.Create().WithPath("/catalog/items").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(201).WithBody("created"));

        await using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        // No body-reading policy: the request body must stream straight through, unbuffered.
        PointAndInjectPolicy(factory, backend, new PolicyDocument([SetHeader("X-Marker", "m")], [], [], []));

        var response = await client.PostAsync("/catalog/items", new StringContent("hello-streamed-body"));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        backend.LogEntries.Single().RequestMessage!.Body.ShouldBe("hello-streamed-body");
    }

    [Fact]
    public async Task A_policy_re_reading_the_body_sees_it_buffered()
    {
        using var backend = WireMockServer.Start();
        backend
            .Given(Request.Create().WithPath("/catalog/items").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("ok"));

        await using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        // An expression re-reads the request body and echoes it into a header: forces buffering.
        var echo = SetHeader("X-Echo-Body", "@(context.Request.Body.As<string>())");
        PointAndInjectPolicy(factory, backend, new PolicyDocument([echo], [], [], []));

        await client.PostAsync("/catalog/items", new StringContent("echo-me"));

        backend.LogEntries.Single().RequestMessage!.Headers!["X-Echo-Body"].ShouldContain("echo-me");
    }

    private static PolicyNode SetHeader(string name, string value) =>
        new("set-header", new Dictionary<string, string> { ["name"] = name },
            [new PolicyNode("value", new Dictionary<string, string>(), [], value)], null);

    private static void PointAndInjectPolicy(TestAppFactory factory, WireMockServer backend, PolicyDocument global)
    {
        var holder = factory.Services.GetRequiredService<GatewayConfigHolder>();
        var api = holder.Current.Apis[0] with { ServiceUrl = new Uri(backend.Url!), SubscriptionRequired = false };
        holder.Current = holder.Current with { Apis = [api], GlobalPolicy = global };
    }
}
