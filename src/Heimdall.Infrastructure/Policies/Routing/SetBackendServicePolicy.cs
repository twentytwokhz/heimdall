using Heimdall.Application;
using Heimdall.Domain;

namespace Heimdall.Infrastructure.Policies.Routing;

/// <summary>
/// set-backend-service: overrides the forward destination via <c>base-url</c> (expression-interpolated)
/// or <c>backend-id</c> (a named Backend entity, URL resolved from the loaded config — mirroring APIM).
/// </summary>
public sealed class SetBackendServicePolicy(IExpressionEvaluator expressions) : IPolicy
{
    public string ElementName => "set-backend-service";
    public PolicySection Sections => PolicySection.Inbound | PolicySection.Backend;

    public ValueTask ApplyAsync(IPolicyContext context, PolicyNode node, CancellationToken ct = default)
    {
        if (node.Attributes.TryGetValue("base-url", out var baseUrl))
        {
            context.BackendServiceUrl = new Uri(expressions.Interpolate(baseUrl, context));
            return ValueTask.CompletedTask;
        }

        if (node.Attributes.TryGetValue("backend-id", out var backendId))
        {
            if (!context.Backends.TryGetValue(backendId, out var url))
            {
                throw new PolicyException(
                    ElementName, "UnknownBackend",
                    $"<set-backend-service> references unknown backend-id '{backendId}'.");
            }

            context.BackendServiceUrl = url;
            return ValueTask.CompletedTask;
        }

        throw new PolicyException(
            ElementName, "MissingAttribute",
            "<set-backend-service> requires a 'base-url' or 'backend-id' attribute.");
    }
}
