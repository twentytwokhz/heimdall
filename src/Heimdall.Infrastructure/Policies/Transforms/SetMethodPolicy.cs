using Heimdall.Application;
using Heimdall.Domain;

namespace Heimdall.Infrastructure.Policies.Transforms;

/// <summary>set-method: changes the HTTP method of the request before it is forwarded.</summary>
public sealed class SetMethodPolicy(IExpressionEvaluator expressions) : IPolicy
{
    public string ElementName => "set-method";
    public PolicySection Sections => PolicySection.Inbound | PolicySection.Backend;

    public ValueTask ApplyAsync(IPolicyContext context, PolicyNode node, CancellationToken ct = default)
    {
        var method = expressions.Interpolate(node.RawText ?? string.Empty, context).Trim();
        if (method.Length == 0)
        {
            throw new PolicyException(ElementName, "MissingValue", "<set-method> requires a method value.");
        }

        context.Request.Method = method;
        return ValueTask.CompletedTask;
    }
}
