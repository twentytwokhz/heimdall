namespace Heimdall.Api.Console;

/// <summary>
/// A policy-authoring save: the target scope (<c>global|api|operation|product</c>), the ids that scope
/// needs, and the policy XML to validate and hot-swap. Bound from the JSON request body.
/// </summary>
public sealed record AuthoringPolicyRequest(
    string? Scope,
    string? ApiId,
    string? OperationId,
    string? ProductId,
    string? Xml);
