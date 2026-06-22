using Heimdall.Application;
using Heimdall.Domain;
using Heimdall.Infrastructure.Policies.Auth;
using Heimdall.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace Heimdall.Tests.Policies;

public class AuthPolicyTests
{
    private static PolicyNode Element(string name, (string, string)[] attrs, params PolicyNode[] children) =>
        new(name, attrs.ToDictionary(a => a.Item1, a => a.Item2), children, null);

    private static PolicyNode Child(string name, string text) =>
        new(name, new Dictionary<string, string>(), [], text);

    // --- check-header ---

    [Fact]
    public async Task Check_header_passes_when_a_matching_value_is_present()
    {
        var policy = new CheckHeaderPolicy();
        var ctx = PolicyContexts.For(PolicySection.Inbound);
        ctx.Request.Headers["X-Key"] = ["secret"];

        await policy.ApplyAsync(ctx, Element("check-header", [("name", "X-Key")], Child("value", "secret")));

        ctx.ShortCircuited.ShouldBeFalse();
    }

    [Fact]
    public async Task Check_header_fails_with_the_configured_code_when_missing()
    {
        var policy = new CheckHeaderPolicy();
        var ctx = PolicyContexts.For(PolicySection.Inbound);

        await policy.ApplyAsync(ctx, Element("check-header",
            [("name", "X-Key"), ("failed-check-httpcode", "401")], Child("value", "secret")));

        ctx.ShortCircuited.ShouldBeTrue();
        ctx.Response.StatusCode.ShouldBe(401);
    }

    // --- ip-filter ---

    [Fact]
    public async Task Ip_filter_allow_blocks_an_unlisted_address()
    {
        var policy = new IpFilterPolicy();
        var ctx = PolicyContexts.For(PolicySection.Inbound, ip: "10.0.0.9");

        await policy.ApplyAsync(ctx, Element("ip-filter", [("action", "allow")], Child("address", "10.0.0.1")));

        ctx.ShortCircuited.ShouldBeTrue();
        ctx.Response.StatusCode.ShouldBe(403);
    }

    [Fact]
    public async Task Ip_filter_allow_passes_a_listed_address()
    {
        var policy = new IpFilterPolicy();
        var ctx = PolicyContexts.For(PolicySection.Inbound, ip: "10.0.0.1");

        await policy.ApplyAsync(ctx, Element("ip-filter", [("action", "allow")], Child("address", "10.0.0.1")));

        ctx.ShortCircuited.ShouldBeFalse();
    }

    [Fact]
    public async Task Ip_filter_forbid_blocks_a_listed_address()
    {
        var policy = new IpFilterPolicy();
        var ctx = PolicyContexts.For(PolicySection.Inbound, ip: "10.0.0.1");

        await policy.ApplyAsync(ctx, Element("ip-filter", [("action", "forbid")], Child("address", "10.0.0.1")));

        ctx.ShortCircuited.ShouldBeTrue();
        ctx.Response.StatusCode.ShouldBe(403);
    }

    // --- cors (preflight) ---

    [Fact]
    public async Task Cors_preflight_short_circuits_with_allow_headers()
    {
        var policy = new CorsPolicy();
        var ctx = PolicyContexts.For(PolicySection.Inbound, method: "OPTIONS");
        ctx.Request.Headers["Origin"] = ["https://app.example"];
        ctx.Request.Headers["Access-Control-Request-Method"] = ["GET"];

        var node = Element("cors", [],
            Element("allowed-origins", [], Child("origin", "*")),
            Element("allowed-methods", [], Child("method", "GET"), Child("method", "POST")));

        await policy.ApplyAsync(ctx, node);

        ctx.ShortCircuited.ShouldBeTrue();
        ctx.Response.StatusCode.ShouldBe(200);
        ctx.Response.Headers["Access-Control-Allow-Origin"].ShouldContain("*");
        ctx.Response.Headers["Access-Control-Allow-Methods"][0].ShouldContain("GET");
    }

    [Fact]
    public async Task Cors_preflight_emits_max_age_from_allowed_methods()
    {
        var policy = new CorsPolicy();
        var ctx = PolicyContexts.For(PolicySection.Inbound, method: "OPTIONS");
        ctx.Request.Headers["Origin"] = ["https://app.example"];
        ctx.Request.Headers["Access-Control-Request-Method"] = ["GET"];

        var node = Element("cors", [],
            Element("allowed-origins", [], Child("origin", "*")),
            Element("allowed-methods", [("preflight-result-max-age", "600")], Child("method", "GET")));

        await policy.ApplyAsync(ctx, node);

        ctx.Response.Headers["Access-Control-Max-Age"][0].ShouldBe("600");
    }

    [Fact]
    public async Task Ip_filter_allow_passes_an_address_inside_a_range()
    {
        var policy = new IpFilterPolicy();
        var ctx = PolicyContexts.For(PolicySection.Inbound, ip: "10.0.0.50");

        await policy.ApplyAsync(ctx, Element("ip-filter", [("action", "allow")],
            Element("address-range", [("from", "10.0.0.1"), ("to", "10.0.0.100")])));

        ctx.ShortCircuited.ShouldBeFalse();
    }

    [Fact]
    public async Task Cors_does_not_short_circuit_a_non_preflight_request()
    {
        var policy = new CorsPolicy();
        var ctx = PolicyContexts.For(PolicySection.Inbound, method: "GET");

        await policy.ApplyAsync(ctx, Element("cors", [], Element("allowed-origins", [], Child("origin", "*"))));

        ctx.ShortCircuited.ShouldBeFalse();
    }
}
