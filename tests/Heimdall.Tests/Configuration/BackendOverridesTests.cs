using Heimdall.Api.Configuration;
using Heimdall.Domain;
using Microsoft.Extensions.Configuration;
using Shouldly;
using Xunit;
using DomainApi = Heimdall.Domain.Api;

namespace Heimdall.Tests.Configuration;

/// <summary>
/// The host-layer per-environment backend override (Heimdall:BackendOverrides): rewrites an API's
/// default forward target (ServiceUrl) after the loader runs, so the same config forwards to
/// localhost on the host and to a compose service name in a container. Loaders stay pure.
/// </summary>
public class BackendOverridesTests
{
    private static GatewayConfig ConfigWith(params DomainApi[] apis) =>
        new(apis, [], [], [], [], null);

    private static DomainApi ApiWith(string id, string url) =>
        new(id, id, id, [], null, [], true, new Uri(url));

    [Fact]
    public void Rewrites_the_named_api_service_url_and_leaves_others_untouched()
    {
        var config = ConfigWith(
            ApiWith("acme", "http://localhost:8081"),
            ApiWith("other", "http://localhost:9000"));

        var result = BackendOverrides.Apply(
            config,
            new Dictionary<string, string> { ["acme"] = "http://backend:8081" });

        result.Apis.Single(a => a.Id == "acme").ServiceUrl.ShouldBe(new Uri("http://backend:8081"));
        result.Apis.Single(a => a.Id == "other").ServiceUrl.ShouldBe(new Uri("http://localhost:9000"));
    }

    [Fact]
    public void Empty_overrides_is_a_no_op_and_returns_the_same_config()
    {
        var config = ConfigWith(ApiWith("acme", "http://localhost:8081"));

        BackendOverrides.Apply(config, new Dictionary<string, string>()).ShouldBeSameAs(config);
    }

    [Fact]
    public void Rewrites_only_the_named_apis_when_several_are_overridden()
    {
        var config = ConfigWith(
            ApiWith("acme", "http://localhost:8081"),
            ApiWith("other", "http://localhost:9000"),
            ApiWith("third", "http://localhost:7000"));

        var result = BackendOverrides.Apply(config, new Dictionary<string, string>
        {
            ["acme"] = "http://a:1",
            ["third"] = "http://c:3",
        });

        result.Apis.Single(a => a.Id == "acme").ServiceUrl.ShouldBe(new Uri("http://a:1"));
        result.Apis.Single(a => a.Id == "other").ServiceUrl.ShouldBe(new Uri("http://localhost:9000"));
        result.Apis.Single(a => a.Id == "third").ServiceUrl.ShouldBe(new Uri("http://c:3"));
    }

    [Fact]
    public void Empty_string_value_fails_loud()
    {
        var config = ConfigWith(ApiWith("acme", "http://localhost:8081"));

        Should.Throw<InvalidOperationException>(() => BackendOverrides.Apply(
            config,
            new Dictionary<string, string> { ["acme"] = "" }));
    }

    [Fact]
    public void Invalid_url_fails_loud()
    {
        var config = ConfigWith(ApiWith("acme", "http://localhost:8081"));

        Should.Throw<InvalidOperationException>(() => BackendOverrides.Apply(
            config,
            new Dictionary<string, string> { ["acme"] = "not-a-url" }));
    }

    [Fact]
    public void Unknown_api_id_fails_loud()
    {
        var config = ConfigWith(ApiWith("acme", "http://localhost:8081"));

        var ex = Should.Throw<InvalidOperationException>(() => BackendOverrides.Apply(
            config,
            new Dictionary<string, string> { ["ghost"] = "http://backend:8081" }));

        ex.Message.ShouldContain("ghost");
    }

    [Fact]
    public void Binds_overrides_from_the_Heimdall_BackendOverrides_configuration_section()
    {
        var config = ConfigWith(ApiWith("acme", "http://localhost:8081"));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Heimdall:BackendOverrides:acme"] = "http://backend:8081",
            })
            .Build();

        var result = BackendOverrides.Apply(config, configuration);

        result.Apis.Single().ServiceUrl.ShouldBe(new Uri("http://backend:8081"));
    }
}
