namespace Heimdall.Application;

/// <summary>
/// Per-request context exposed to policies and expressions, mirroring APIM's <c>context</c>.
/// Phase 2 wires Request/Response/Variables and the route facets; Subscription/Product/User/LastError
/// are populated in later phases (null until then).
/// </summary>
public interface IPolicyContext
{
    EmuRequest Request { get; }
    EmuResponse Response { get; }
    IDictionary<string, object?> Variables { get; }

    /// <summary><c>set-backend-service backend-id</c>: named backend entities by id (URLs reflect any
    /// per-environment backend overrides). Empty when the config declares no backends.</summary>
    IReadOnlyDictionary<string, Uri> Backends { get; }

    /// <summary><c>context.RequestId</c>: a unique id assigned to this request.</summary>
    Guid RequestId { get; }

    /// <summary><c>context.Timestamp</c>: when the gateway received this request.</summary>
    DateTimeOffset Timestamp { get; }

    ApiInfo Api { get; }
    OperationInfo Operation { get; }
    DeploymentInfo Deployment { get; }
    SubscriptionInfo? Subscription { get; }
    ProductInfo? Product { get; }
    UserInfo? User { get; }
    LastErrorInfo? LastError { get; set; }
    Uri? BackendServiceUrl { get; set; }

    /// <summary>True once a policy short-circuited the request (return-response / mock-response).</summary>
    bool ShortCircuited { get; set; }

    /// <summary>The section currently executing; section-aware policies (e.g. set-header) read this.</summary>
    PolicySection CurrentSection { get; set; }

    IExpressionEvaluator Expressions { get; }
    INamedValues NamedValues { get; }
}
