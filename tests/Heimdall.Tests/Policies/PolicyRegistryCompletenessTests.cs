using Heimdall.Application;
using Heimdall.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Heimdall.Tests.Policies;

/// <summary>Guards the tier-1 support matrix: every supported element resolves, and unknown elements fail loud.</summary>
public class PolicyRegistryCompletenessTests
{
    // The tier-1 policy elements (Phase 3). include-fragment is resolved at flatten time, and
    // when/otherwise are children of choose, so none of those are standalone IPolicy elements.
    public static readonly string[] Tier1Elements =
    [
        "set-header", "set-method", "rewrite-uri", "set-query-parameter", "set-body", "set-status", "find-and-replace",
        "forward-request", "return-response", "mock-response", "set-backend-service",
        "set-variable", "choose",
        "rate-limit", "rate-limit-by-key", "quota", "quota-by-key",
        "cache-lookup", "cache-store", "cache-lookup-value", "cache-store-value",
        "check-header", "ip-filter", "cors", "validate-jwt",
    ];

    [Theory]
    [MemberData(nameof(ElementNames))]
    public void Every_tier1_element_resolves(string elementName)
    {
        using var provider = TestServices.Policies();
        var registry = provider.GetRequiredService<IPolicyRegistry>();

        registry.IsSupported(elementName).ShouldBeTrue();
        registry.Resolve(elementName).ElementName.ShouldBe(elementName);
    }

    [Fact]
    public void Unknown_element_throws_unsupported_policy_exception()
    {
        using var provider = TestServices.Policies();
        var registry = provider.GetRequiredService<IPolicyRegistry>();

        Should.Throw<UnsupportedPolicyException>(() => registry.Resolve("not-a-real-policy"));
    }

    public static TheoryData<string> ElementNames()
    {
        var data = new TheoryData<string>();
        foreach (var name in Tier1Elements)
        {
            data.Add(name);
        }
        return data;
    }
}
