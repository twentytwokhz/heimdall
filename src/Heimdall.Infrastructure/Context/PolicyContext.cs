using Heimdall.Application;

namespace Heimdall.Infrastructure.Context;

/// <summary>
/// Plain <see cref="IPolicyContext"/> implementation, constructible from data. Tests build it directly;
/// Phase 3 populates it from the request's HttpContext before running the pipeline.
/// </summary>
public sealed class PolicyContext : IPolicyContext
{
    public required EmuRequest Request { get; init; }
    public EmuResponse Response { get; init; } = new();
    public IDictionary<string, object?> Variables { get; init; } = new Dictionary<string, object?>();
    public Guid RequestId { get; init; } = Guid.NewGuid();
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public required ApiInfo Api { get; init; }
    public required OperationInfo Operation { get; init; }
    public DeploymentInfo Deployment { get; init; } = new("heimdall", "local", "heimdall-gateway");
    public SubscriptionInfo? Subscription { get; init; }
    public ProductInfo? Product { get; init; }
    public UserInfo? User { get; init; }
    public LastErrorInfo? LastError { get; set; }
    public Uri? BackendServiceUrl { get; set; }
    public bool ShortCircuited { get; set; }
    public PolicySection CurrentSection { get; set; }
    public IExpressionEvaluator Expressions { get; init; } = null!;
    public INamedValues NamedValues { get; init; } = null!;
}
