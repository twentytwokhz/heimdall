using System.Diagnostics;
using Heimdall.Application;
using Heimdall.Domain;

namespace Heimdall.Api.Pipeline;

/// <summary>
/// Host-side accumulator that times a request's stages and assembles an immutable <see cref="RequestTrace"/>.
/// One per request, not thread-safe by design: it lives outside the policy context so policies stay
/// unaware of tracing, and it is only ever touched by the single thread running that request.
/// </summary>
public sealed class RequestTraceBuilder
{
    private readonly long _startTs = Stopwatch.GetTimestamp();
    private readonly string _method;
    private readonly string _path;
    private readonly DateTimeOffset _receivedAt;
    private readonly List<TraceStage> _stages = [];

    private Guid _requestId = Guid.NewGuid();
    private string _apiId = "";
    private string _apiName = "";
    private string _operationId = "";
    private string _operationMethod = "";
    private string? _subscriptionId;
    private string? _productId;

    private bool _frontendRecorded;
    private string? _openSection;
    private long _stageStartTs;
    private List<PolicyTrace> _stagePolicies = [];

    public RequestTraceBuilder(string method, string path, DateTimeOffset receivedAt)
    {
        _method = method;
        _path = path;
        _receivedAt = receivedAt;
    }

    /// <summary>
    /// Forces the trace id, for a playground replay correlated by an inbound id. Set before the pipeline
    /// runs; on the success path <see cref="Bind"/> re-applies the same id from the context.
    /// </summary>
    public void UseRequestId(Guid id) => _requestId = id;

    /// <summary>Route metadata, known right after the router matches (available even on the auth-reject path).</summary>
    public void SetRoute(Heimdall.Domain.Api api, Operation operation)
    {
        _apiId = api.Id;
        _apiName = api.DisplayName;
        _operationId = operation.Id;
        _operationMethod = operation.Method;
    }

    /// <summary>Correlate the trace id with the per-request context, and capture resolved subscription/product.</summary>
    public void Bind(IPolicyContext context)
    {
        _requestId = context.RequestId;
        _subscriptionId = context.Subscription?.Id;
        _productId = context.Product?.Id;
    }

    /// <summary>Closes the Frontend stage (receive + route + auth + context build) as the pipeline begins.</summary>
    public void StartPipeline() => RecordFrontend();

    /// <summary>Begins timing a pipeline section (Inbound / Backend / Outbound / OnError).</summary>
    public void BeginStage(string section)
    {
        _openSection = section;
        _stageStartTs = Stopwatch.GetTimestamp();
        _stagePolicies = [];
    }

    /// <summary>Records that a policy element ran in the open stage, timed from <paramref name="policyStartTs"/>.</summary>
    public void RecordPolicy(string name, long policyStartTs) =>
        _stagePolicies.Add(new PolicyTrace(name, ElapsedMs(policyStartTs)));

    /// <summary>Closes the open stage and appends it; a no-op when no stage is open.</summary>
    public void EndStage()
    {
        if (_openSection is null)
        {
            return;
        }

        _stages.Add(new TraceStage(_openSection, ElapsedMs(_stageStartTs), _stagePolicies));
        _openSection = null;
        _stagePolicies = [];
    }

    /// <summary>Finalizes a request that ran the pipeline; infers the outcome from what was recorded.</summary>
    public RequestTrace Complete(IPolicyContext context)
    {
        EndStage();
        var outcome = context.LastError is not null
            ? TraceOutcome.Error
            : _stages.Any(s => s.Section == "Backend")
                ? TraceOutcome.Completed
                : TraceOutcome.ShortCircuited;
        return Build(context.Response.StatusCode, outcome, context.LastError);
    }

    /// <summary>Finalizes a request rejected before the pipeline (no route match handled elsewhere, or auth failed).</summary>
    public RequestTrace Rejected(int statusCode)
    {
        RecordFrontend();
        EndStage();
        return Build(statusCode, TraceOutcome.Rejected, error: null);
    }

    /// <summary>
    /// Finalizes a request whose pipeline faulted (unsupported policy, or an unhandled error).
    /// <paramref name="faultingPolicy"/>, when known, is recorded in the open stage so the trace shows
    /// which element failed rather than an incomplete-looking policy list.
    /// </summary>
    public RequestTrace Faulted(int statusCode, string? faultingPolicy = null)
    {
        if (faultingPolicy is not null && _openSection is not null)
        {
            _stagePolicies.Add(new PolicyTrace(faultingPolicy, DurationMs: 0));
        }

        EndStage();
        return Build(statusCode, TraceOutcome.Error, error: null);
    }

    private void RecordFrontend()
    {
        if (_frontendRecorded)
        {
            return;
        }

        _stages.Add(new TraceStage("Frontend", ElapsedMs(_startTs), []));
        _frontendRecorded = true;
    }

    private RequestTrace Build(int statusCode, TraceOutcome outcome, LastErrorInfo? error) =>
        // Snapshot the stages so the built (immutable) trace never aliases the builder's mutable list.
        new(_requestId, _receivedAt, _method, _path, _apiId, _apiName, _operationId, _operationMethod,
            _subscriptionId, _productId, statusCode, ElapsedMs(_startTs), outcome, error, [.. _stages]);

    private static double ElapsedMs(long fromTimestamp) =>
        Stopwatch.GetElapsedTime(fromTimestamp).TotalMilliseconds;
}
