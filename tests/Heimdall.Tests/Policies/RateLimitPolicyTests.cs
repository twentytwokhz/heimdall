using Heimdall.Application;
using Heimdall.Domain;
using Heimdall.Infrastructure.Expressions;
using Heimdall.Infrastructure.Policies.RateLimiting;
using Heimdall.Infrastructure.Stores;
using Heimdall.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace Heimdall.Tests.Policies;

public class RateLimitPolicyTests
{
    private static readonly IExpressionEvaluator Expr = new RoslynExpressionEvaluator();

    private static (InMemoryCounterStore Store, FakeClock Clock) NewStore()
    {
        var clock = new FakeClock(DateTimeOffset.UnixEpoch);
        return (new InMemoryCounterStore(clock), clock);
    }

    private static PolicyNode RateLimit(int calls, int period) =>
        new("rate-limit", new Dictionary<string, string>
        {
            ["calls"] = calls.ToString(),
            ["renewal-period"] = period.ToString(),
        }, [], null);

    private static PolicyNode RateLimitByKey(int calls, int period, string counterKey) =>
        new("rate-limit-by-key", new Dictionary<string, string>
        {
            ["calls"] = calls.ToString(),
            ["renewal-period"] = period.ToString(),
            ["counter-key"] = counterKey,
        }, [], null);

    [Fact]
    public async Task Within_the_limit_does_not_short_circuit()
    {
        var (store, clock) = NewStore();
        var policy = new RateLimitPolicy(store, clock, Expr);
        var ctx = PolicyContexts.For(PolicySection.Inbound);

        await policy.ApplyAsync(ctx, RateLimit(2, 60));

        ctx.ShortCircuited.ShouldBeFalse();
    }

    [Fact]
    public async Task Exceeding_the_limit_returns_429_with_retry_after_and_short_circuits()
    {
        var (store, clock) = NewStore();
        var policy = new RateLimitPolicy(store, clock, Expr);
        var ctx = PolicyContexts.For(PolicySection.Inbound);

        await policy.ApplyAsync(ctx, RateLimit(1, 60)); // 1st: allowed
        await policy.ApplyAsync(ctx, RateLimit(1, 60)); // 2nd: over the limit

        ctx.ShortCircuited.ShouldBeTrue();
        ctx.Response.StatusCode.ShouldBe(429);
        ctx.Response.Headers["Retry-After"][0].ShouldBe("60");
    }

    [Fact]
    public async Task The_window_resets_after_the_renewal_period()
    {
        var (store, clock) = NewStore();
        var policy = new RateLimitPolicy(store, clock, Expr);

        await policy.ApplyAsync(PolicyContexts.For(PolicySection.Inbound), RateLimit(1, 60));
        clock.Advance(TimeSpan.FromSeconds(60));
        var ctx = PolicyContexts.For(PolicySection.Inbound);

        await policy.ApplyAsync(ctx, RateLimit(1, 60));

        ctx.ShortCircuited.ShouldBeFalse();
    }

    [Fact]
    public async Task Retry_after_reflects_the_remaining_seconds_mid_window()
    {
        var (store, clock) = NewStore();
        var policy = new RateLimitPolicy(store, clock, Expr);

        await policy.ApplyAsync(PolicyContexts.For(PolicySection.Inbound), RateLimit(1, 60)); // t=0, allowed
        clock.Advance(TimeSpan.FromSeconds(45));
        var ctx = PolicyContexts.For(PolicySection.Inbound);

        await policy.ApplyAsync(ctx, RateLimit(1, 60)); // t=45, over

        ctx.ShortCircuited.ShouldBeTrue();
        ctx.Response.Headers["Retry-After"][0].ShouldBe("15");
    }

    [Fact]
    public async Task One_second_before_the_boundary_is_still_limited_with_retry_after_one()
    {
        var (store, clock) = NewStore();
        var policy = new RateLimitPolicy(store, clock, Expr);

        await policy.ApplyAsync(PolicyContexts.For(PolicySection.Inbound), RateLimit(1, 60)); // t=0
        clock.Advance(TimeSpan.FromSeconds(59));
        var ctx = PolicyContexts.For(PolicySection.Inbound);

        await policy.ApplyAsync(ctx, RateLimit(1, 60)); // t=59, still same window

        ctx.ShortCircuited.ShouldBeTrue();
        ctx.Response.Headers["Retry-After"][0].ShouldBe("1");
    }

    [Fact]
    public async Task Exactly_at_the_boundary_the_window_resets()
    {
        var (store, clock) = NewStore();
        var policy = new RateLimitPolicy(store, clock, Expr);

        await policy.ApplyAsync(PolicyContexts.For(PolicySection.Inbound), RateLimit(1, 60)); // t=0
        clock.Advance(TimeSpan.FromSeconds(60));
        var ctx = PolicyContexts.For(PolicySection.Inbound);

        await policy.ApplyAsync(ctx, RateLimit(1, 60)); // t=60, new window

        ctx.ShortCircuited.ShouldBeFalse();
    }

    [Fact]
    public async Task By_key_counts_distinct_keys_independently()
    {
        var (store, clock) = NewStore();
        var policy = new RateLimitByKeyPolicy(store, clock, Expr);

        await policy.ApplyAsync(PolicyContexts.For(PolicySection.Inbound), RateLimitByKey(1, 60, "alice"));
        var bob = PolicyContexts.For(PolicySection.Inbound);

        await policy.ApplyAsync(bob, RateLimitByKey(1, 60, "bob"));

        bob.ShortCircuited.ShouldBeFalse();
    }

    [Fact]
    public async Task Quota_exceeded_returns_403()
    {
        var (store, clock) = NewStore();
        var policy = new QuotaPolicy(store, clock, Expr);
        var ctx = PolicyContexts.For(PolicySection.Inbound);

        await policy.ApplyAsync(ctx, new PolicyNode("quota",
            new Dictionary<string, string> { ["calls"] = "1", ["renewal-period"] = "86400" }, [], null));
        await policy.ApplyAsync(ctx, new PolicyNode("quota",
            new Dictionary<string, string> { ["calls"] = "1", ["renewal-period"] = "86400" }, [], null));

        ctx.Response.StatusCode.ShouldBe(403);
        ctx.ShortCircuited.ShouldBeTrue();
    }
}
