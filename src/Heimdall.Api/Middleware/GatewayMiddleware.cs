using Heimdall.Api.Configuration;
using Heimdall.Api.Pipeline;
using Heimdall.Api.Playground;
using Heimdall.Api.Routing;
using Heimdall.Application;
using Microsoft.AspNetCore.Http;

namespace Heimdall.Api.Middleware;

/// <summary>
/// The gateway request handler: routes a request to an API operation, builds the policy context,
/// runs the flattened effective policy through the pipeline, and writes the result.
/// Runs as the fallback endpoint, so reserved routes like /health are handled before it.
/// </summary>
public sealed class GatewayMiddleware(
    GatewayConfigHolder configHolder,
    ISubscriptionKeyValidator subscriptionKeyValidator,
    EffectivePolicyBuilder policyBuilder,
    PolicyPipelineExecutor executor,
    HttpContextPolicyContextFactory contextFactory,
    IClock clock,
    ITraceSink traceSink,
    ILogger<GatewayMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext http)
    {
        // Reserve the console namespace: /_apim/* is the admin API surface, never proxied as gateway
        // traffic. Holds even when the console is disabled (its endpoints unmapped) so the prefix is
        // never forwarded to a backend. No trace is recorded, matching the no-route 404 below.
        if (http.Request.Path.StartsWithSegments("/_apim"))
        {
            http.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        // A playground replay tags the request with its id (and the header is removed here, before the
        // context, the trace, and YARP's forward see the request) so the recorded trace correlates back.
        ReplayCorrelation.Capture(http);

        var trace = new RequestTraceBuilder(http.Request.Method, http.Request.Path.ToString(), clock.UtcNow);
        if (ReplayCorrelation.RequestId(http) is { } replayId)
        {
            // Adopt the replay id even on the auth-reject path (no context is built there to Bind from).
            trace.UseRequestId(replayId);
        }

        var config = configHolder.Current;
        var match = ApiRouter.Match(config, http.Request.Method, http.Request.Path.ToString());
        if (match is null)
        {
            // No API matched: there is no route to trace, so this is left out of the feed for now.
            http.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        trace.SetRoute(match.Api, match.Operation);

        // Subscription-key auth runs before the pipeline (APIM order): reject missing/invalid keys here.
        var presentedKey = SubscriptionKey.Extract(http.Request);
        var auth = subscriptionKeyValidator.Validate(match.Api, presentedKey, config.Subscriptions, config.Products);
        if (auth.Outcome != SubscriptionKeyOutcome.Allowed)
        {
            await ApimErrorShaper.WriteUnauthorizedAsync(http.Response, auth.Outcome, http.RequestAborted);
            traceSink.Add(trace.Rejected(http.Response.StatusCode));
            return;
        }

        // Strip the key so it never reaches the backend (APIM default). Done before the context is built
        // so both the policy context and YARP's forward (which copy from http.Request) see the clean request.
        SubscriptionKey.Strip(http.Request);

        try
        {
            // Scope bypass: product policy joins the chain only for product-scoped access (global < product < api < op).
            var effectivePolicy = policyBuilder.Build(
                [config.GlobalPolicy, auth.Product?.Policy, match.Api.Policy, match.Operation.Policy], config.Fragments);

            var defaultBackend = match.Api.ServiceUrl
                ?? throw new InvalidOperationException($"API '{match.Api.Id}' has no service URL to forward to.");

            var subscriptionInfo = auth.Subscription is null
                ? null
                : new SubscriptionInfo(
                    auth.Subscription.Id,
                    auth.Subscription.DisplayName ?? auth.Subscription.Id,
                    presentedKey ?? string.Empty);
            var productInfo = auth.Product is null ? null : new ProductInfo(auth.Product.Id, auth.Product.DisplayName);

            var context = await contextFactory.CreateAsync(
                http, match, subscriptionInfo, productInfo, effectivePolicy.RequiresBodyBuffering, http.RequestAborted);
            trace.Bind(context);
            trace.StartPipeline(); // closes the Frontend stage; the pipeline begins now
            await executor.ExecuteAsync(http, context, effectivePolicy, defaultBackend, http.RequestAborted, trace);
            await contextFactory.WriteResponseAsync(http, context, http.RequestAborted);
            traceSink.Add(trace.Complete(context));
        }
        catch (UnsupportedPolicyException ex)
        {
            // Unsupported policies fail loud, but as a controlled 501 rather than a raw framework 500.
            logger.LogError(ex, "Unsupported policy on {Method} {Path}", http.Request.Method, http.Request.Path);
            await WriteError(http, StatusCodes.Status501NotImplemented, ex.Message);
            traceSink.Add(trace.Faulted(http.Response.StatusCode, ex.ElementName));
        }
        catch (Exception ex)
        {
            // Any other pipeline failure (bad config, malformed expression, on-error fault) is surfaced
            // as a clean 500 with the message rather than leaking a stack trace.
            logger.LogError(ex, "Policy pipeline error on {Method} {Path}", http.Request.Method, http.Request.Path);
            await WriteError(http, StatusCodes.Status500InternalServerError, $"Policy pipeline error: {ex.Message}");
            traceSink.Add(trace.Faulted(http.Response.StatusCode));
        }
    }

    private static async Task WriteError(HttpContext http, int statusCode, string message)
    {
        if (http.Response.HasStarted)
        {
            return;
        }

        http.Response.Clear();
        http.Response.StatusCode = statusCode;
        await http.Response.WriteAsync(message);
    }
}
