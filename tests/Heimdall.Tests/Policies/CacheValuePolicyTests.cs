using Heimdall.Application;
using Heimdall.Domain;
using Heimdall.Infrastructure.Expressions;
using Heimdall.Infrastructure.Policies.Caching;
using Heimdall.Infrastructure.Stores;
using Heimdall.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace Heimdall.Tests.Policies;

public class CacheValuePolicyTests
{
    private static readonly IExpressionEvaluator Expr = new RoslynExpressionEvaluator();

    private static PolicyNode StoreValue(string key, string value, int duration) =>
        new("cache-store-value", new Dictionary<string, string>
        {
            ["key"] = key, ["value"] = value, ["duration"] = duration.ToString(),
        }, [], null);

    private static PolicyNode LookupValue(string key, string variable, string? defaultValue = null)
    {
        var attrs = new Dictionary<string, string> { ["key"] = key, ["variable-name"] = variable };
        if (defaultValue is not null)
        {
            attrs["default-value"] = defaultValue;
        }
        return new PolicyNode("cache-lookup-value", attrs, [], null);
    }

    [Fact]
    public async Task Stored_value_is_read_back_into_a_variable()
    {
        var cache = new InMemoryCacheStore(new FakeClock(DateTimeOffset.UnixEpoch));
        var store = new CacheStoreValuePolicy(cache, Expr);
        var lookup = new CacheLookupValuePolicy(cache, Expr);
        var ctx = PolicyContexts.For(PolicySection.Inbound);

        await store.ApplyAsync(ctx, StoreValue("token", "@(1 + 1)", 60));
        await lookup.ApplyAsync(ctx, LookupValue("token", "cached"));

        ctx.Variables["cached"].ShouldBe(2);
    }

    [Fact]
    public async Task Lookup_miss_uses_the_default_value()
    {
        var cache = new InMemoryCacheStore(new FakeClock(DateTimeOffset.UnixEpoch));
        var lookup = new CacheLookupValuePolicy(cache, Expr);
        var ctx = PolicyContexts.For(PolicySection.Inbound);

        await lookup.ApplyAsync(ctx, LookupValue("absent", "v", defaultValue: "fallback"));

        ctx.Variables["v"].ShouldBe("fallback");
    }
}
