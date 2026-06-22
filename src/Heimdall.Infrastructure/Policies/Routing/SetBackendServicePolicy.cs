using Heimdall.Application;
using Heimdall.Domain;

namespace Heimdall.Infrastructure.Policies.Routing;

/// <summary>set-backend-service: overrides the forward destination via <c>base-url</c> (expression-interpolated).</summary>
public sealed class SetBackendServicePolicy(IExpressionEvaluator expressions) : IPolicy
{
    public string ElementName => "set-backend-service";
    public PolicySection Sections => PolicySection.Inbound | PolicySection.Backend;

    public ValueTask ApplyAsync(IPolicyContext context, PolicyNode node, CancellationToken ct = default)
    {
        if (!node.Attributes.TryGetValue("base-url", out var baseUrl))
        {
            throw new PolicyException(ElementName, "MissingAttribute", "<set-backend-service> requires a 'base-url' attribute.");
        }

        context.BackendServiceUrl = new Uri(expressions.Interpolate(baseUrl, context));
        return ValueTask.CompletedTask;
    }
}
