using Heimdall.Application;
using Heimdall.Domain;

namespace Heimdall.Infrastructure.Policies.RateLimiting;

/// <summary>rate-limit: caps calls per renewal-period; exceeding returns 429. Keyed per-API (subscriptions land in Phase 4).</summary>
public sealed class RateLimitPolicy(ICounterStore counters, IClock clock, IExpressionEvaluator expressions)
    : CounterPolicyBase(counters, clock, expressions)
{
    public override string ElementName => "rate-limit";
    protected override int ExceededStatusCode => 429;
    protected override string ResolveKey(IPolicyContext context, PolicyNode node) => context.Api.Id;
}

/// <summary>rate-limit-by-key: like rate-limit but keyed by the evaluated counter-key expression.</summary>
public sealed class RateLimitByKeyPolicy(ICounterStore counters, IClock clock, IExpressionEvaluator expressions)
    : CounterPolicyBase(counters, clock, expressions)
{
    public override string ElementName => "rate-limit-by-key";
    protected override int ExceededStatusCode => 429;
    protected override string ResolveKey(IPolicyContext context, PolicyNode node) => EvaluateCounterKey(context, node);
}

/// <summary>quota: caps calls per (typically long) renewal-period; exceeding returns 403. Keyed per-API.</summary>
public sealed class QuotaPolicy(ICounterStore counters, IClock clock, IExpressionEvaluator expressions)
    : CounterPolicyBase(counters, clock, expressions)
{
    public override string ElementName => "quota";
    protected override int ExceededStatusCode => 403;
    protected override string ResolveKey(IPolicyContext context, PolicyNode node) => context.Api.Id;
}

/// <summary>quota-by-key: like quota but keyed by the evaluated counter-key expression.</summary>
public sealed class QuotaByKeyPolicy(ICounterStore counters, IClock clock, IExpressionEvaluator expressions)
    : CounterPolicyBase(counters, clock, expressions)
{
    public override string ElementName => "quota-by-key";
    protected override int ExceededStatusCode => 403;
    protected override string ResolveKey(IPolicyContext context, PolicyNode node) => EvaluateCounterKey(context, node);
}
