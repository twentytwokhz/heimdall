using System.Globalization;
using Heimdall.Application;
using Heimdall.Domain;

namespace Heimdall.Infrastructure.Policies.RateLimiting;

/// <summary>
/// Shared logic for the rate-limit and quota families: increment an epoch-aligned counter and, when
/// the configured <c>calls</c> is exceeded in the <c>renewal-period</c>, short-circuit with the
/// family's status code and a <c>Retry-After</c> header.
/// </summary>
public abstract class CounterPolicyBase(ICounterStore counters, IClock clock, IExpressionEvaluator expressions) : IPolicy
{
    public abstract string ElementName { get; }
    public PolicySection Sections => PolicySection.Inbound;

    /// <summary>The HTTP status returned when the limit is exceeded (429 for rate-limit, 403 for quota).</summary>
    protected abstract int ExceededStatusCode { get; }

    /// <summary>The counter key (per-API for the plain variants; the evaluated counter-key for by-key variants).</summary>
    protected abstract string ResolveKey(IPolicyContext context, PolicyNode node);

    public ValueTask ApplyAsync(IPolicyContext context, PolicyNode node, CancellationToken ct = default)
    {
        var calls = RequireLong(node, "calls");
        var period = RequireLong(node, "renewal-period");
        var key = $"{ElementName}:{ResolveKey(context, node)}";

        var state = counters.Increment(key, TimeSpan.FromSeconds(period));
        if (state.Count > calls)
        {
            var retryAfter = Math.Max(1, (int)Math.Ceiling((state.ResetsAt - clock.UtcNow).TotalSeconds));
            context.Response.StatusCode = ExceededStatusCode;
            context.Response.Headers["Retry-After"] = [retryAfter.ToString(CultureInfo.InvariantCulture)];
            context.ShortCircuited = true;
        }

        return ValueTask.CompletedTask;
    }

    protected string EvaluateCounterKey(IPolicyContext context, PolicyNode node) =>
        node.Attributes.TryGetValue("counter-key", out var key)
            ? expressions.Interpolate(key, context)
            : throw new PolicyException(ElementName, "MissingAttribute", $"<{ElementName}> requires a 'counter-key' attribute.");

    private long RequireLong(PolicyNode node, string attribute) =>
        node.Attributes.TryGetValue(attribute, out var raw) && long.TryParse(raw, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new PolicyException(ElementName, "MissingAttribute", $"<{ElementName}> requires a numeric '{attribute}' attribute.");
}
