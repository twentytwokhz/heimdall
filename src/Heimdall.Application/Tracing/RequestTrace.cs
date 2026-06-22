namespace Heimdall.Application;

/// <summary>How a traced request ended, for the live feed's status chip.</summary>
public enum TraceOutcome
{
    /// <summary>Ran the full pipeline and forwarded to the backend.</summary>
    Completed,

    /// <summary>A policy short-circuited the request (return-response / mock-response); no backend call.</summary>
    ShortCircuited,

    /// <summary>Rejected before the pipeline (no route match, or subscription-key auth failed).</summary>
    Rejected,

    /// <summary>A policy error routed into on-error (or the pipeline faulted).</summary>
    Error,
}

/// <summary>One policy element that executed in a stage, with how long it took.</summary>
public sealed record PolicyTrace(string Name, double DurationMs);

/// <summary>
/// One stage of the APIM canvas (Frontend / Inbound / Backend / Outbound / OnError): its wall-clock
/// duration and the policy elements that fired inside it (empty for Frontend and pure-forward Backend).
/// </summary>
public sealed record TraceStage(string Section, double DurationMs, IReadOnlyList<PolicyTrace> Policies);

/// <summary>
/// A full record of one request through the gateway, captured for the console's live trace feed.
/// Built host-side from the pipeline run; never touches policy code. <see cref="RequestId"/> and
/// <see cref="Timestamp"/> mirror the per-request <c>context</c> so the feed and the engine agree.
/// </summary>
public sealed record RequestTrace(
    Guid RequestId,
    DateTimeOffset Timestamp,
    string Method,
    string Path,
    string ApiId,
    string ApiName,
    string OperationId,
    string OperationMethod,
    string? SubscriptionId,
    string? ProductId,
    int StatusCode,
    double DurationMs,
    TraceOutcome Outcome,
    LastErrorInfo? Error,
    IReadOnlyList<TraceStage> Stages);
