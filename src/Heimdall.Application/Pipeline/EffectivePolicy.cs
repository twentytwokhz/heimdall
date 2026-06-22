using Heimdall.Domain;

namespace Heimdall.Application;

/// <summary>The flattened policy for one operation: the four sections after splicing <c>&lt;base/&gt;</c>.</summary>
public sealed record EffectivePolicy(
    IReadOnlyList<PolicyNode> Inbound,
    IReadOnlyList<PolicyNode> Backend,
    IReadOnlyList<PolicyNode> Outbound,
    IReadOnlyList<PolicyNode> OnError)
{
    /// <summary>An effective policy with no nodes in any section.</summary>
    public static EffectivePolicy Empty { get; } = new([], [], [], []);

    /// <summary>
    /// True when a policy actually re-reads the request body, so the gateway must spool it
    /// (<c>EnableBuffering</c>). Otherwise the body streams straight to the backend, untouched.
    /// Triggers: <c>find-and-replace</c> on a request-side section, or any expression (in any section)
    /// that references <c>context.Request.Body</c>.
    /// </summary>
    public bool RequiresBodyBuffering =>
        ReadsRequestBody(Inbound) || ReadsRequestBody(Backend)
        || ReferencesRequestBody(Inbound) || ReferencesRequestBody(Backend)
        || ReferencesRequestBody(Outbound) || ReferencesRequestBody(OnError);

    private static bool ReadsRequestBody(IReadOnlyList<PolicyNode> nodes) =>
        nodes.Any(n => n.Name == "find-and-replace" || ReadsRequestBody(n.Children));

    private static bool ReferencesRequestBody(IReadOnlyList<PolicyNode> nodes) =>
        nodes.Any(n =>
            (n.RawText?.Contains("Request.Body", StringComparison.Ordinal) ?? false)
            || n.Attributes.Values.Any(v => v.Contains("Request.Body", StringComparison.Ordinal))
            || ReferencesRequestBody(n.Children));
}
