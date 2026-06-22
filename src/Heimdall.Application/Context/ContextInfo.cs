namespace Heimdall.Application;

// The smaller context.* facets. Fields are minimal for now; Phase 5 completes the fidelity.

/// <summary><c>context.Api</c>.</summary>
public sealed record ApiInfo(string Id, string Name, string Path);

/// <summary><c>context.Operation</c>.</summary>
public sealed record OperationInfo(string Id, string Method, string UrlTemplate);

/// <summary><c>context.Deployment</c>.</summary>
public sealed record DeploymentInfo(string ServiceName, string Region, string GatewayId);

/// <summary><c>context.Subscription</c> (null when the request has no subscription; populated in Phase 4).</summary>
public sealed record SubscriptionInfo(string Id, string Name, string Key);

/// <summary><c>context.Product</c> (null when not applicable; populated in Phase 4).</summary>
public sealed record ProductInfo(string Id, string Name);

/// <summary><c>context.User</c> (null when unauthenticated).</summary>
public sealed record UserInfo(string Id, string Email);

/// <summary><c>context.LastError</c> (set only in the on-error section).</summary>
public sealed record LastErrorInfo(string Source, string Reason, string Message);
