using System.Diagnostics;
using Heimdall.Api.Forwarding;
using Heimdall.Application;
using Heimdall.Domain;
using Heimdall.Infrastructure.Context;
using Microsoft.AspNetCore.Http;

namespace Heimdall.Api.Pipeline;

/// <summary>
/// Drives the four policy sections over a per-request context (IMPLEMENTATION.md section 6):
/// inbound -> forward (unless short-circuited) -> outbound, with policy errors routed to on-error.
/// </summary>
public sealed class PolicyPipelineExecutor(IPolicyRegistry registry, IForwarder forwarder)
{
    public async Task ExecuteAsync(
        HttpContext httpContext, IPolicyContext context, EffectivePolicy policy, Uri defaultBackend,
        CancellationToken ct, RequestTraceBuilder? trace = null)
    {
        try
        {
            trace?.BeginStage("Inbound");
            await RunSection(PolicySection.Inbound, policy.Inbound, context, ct, trace);
            trace?.EndStage();

            // The Backend stage spans the backend-section policies and the forward, so it is opened only
            // when not short-circuited: its absence is how the trace knows a request never hit the backend.
            if (!context.ShortCircuited)
            {
                trace?.BeginStage("Backend");
                // Backend-section policies (e.g. set-backend-service) run before the implicit forward.
                await RunSection(PolicySection.Backend, policy.Backend, context, ct, trace);

                if (!context.ShortCircuited)
                {
                    var destination = context.BackendServiceUrl ?? defaultBackend;
                    await forwarder.ForwardAsync(httpContext, context, destination, ct);
                }

                trace?.EndStage();
            }

            // An inbound short-circuit only skips the backend; outbound runs regardless (matching APIM).
            // It is consumed here so a fresh short-circuit inside outbound can still stop the rest of outbound.
            context.ShortCircuited = false;
            trace?.BeginStage("Outbound");
            await RunSection(PolicySection.Outbound, policy.Outbound, context, ct, trace);
            trace?.EndStage();
        }
        catch (PolicyException ex)
        {
            context.LastError = new LastErrorInfo(ex.Source ?? "policy", ex.Reason, ex.Message);
            context.ShortCircuited = false; // on-error gets a clean run of its own section
            trace?.EndStage(); // close whichever stage was open when the policy threw
            trace?.BeginStage("OnError");
            try
            {
                await RunSection(PolicySection.OnError, policy.OnError, context, ct, trace);
            }
            catch (PolicyException)
            {
                // An error raised inside on-error has nowhere left to route: surface a clean 500
                // rather than re-entering on-error (which would loop) or leaking the exception.
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                context.Response.Body = new HttpEmuBody("Policy error handler failed.");
            }

            trace?.EndStage();
        }
    }

    private async Task RunSection(
        PolicySection section, IReadOnlyList<PolicyNode> nodes, IPolicyContext context, CancellationToken ct,
        RequestTraceBuilder? trace)
    {
        context.CurrentSection = section;
        foreach (var node in nodes)
        {
            if (context.ShortCircuited)
            {
                break;
            }

            var policy = registry.Resolve(node.Name);
            var policyStartTs = Stopwatch.GetTimestamp();
            await policy.ApplyAsync(context, node, ct);
            trace?.RecordPolicy(node.Name, policyStartTs);
        }
    }
}
