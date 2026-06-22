using Heimdall.Application;
using Heimdall.Domain;

namespace Heimdall.Infrastructure.Policies.ControlFlow;

/// <summary>
/// set-variable: stores a value in <c>context.Variables</c>. A pure <c>@(...)</c>/<c>@{...}</c>
/// value is evaluated to its typed result; anything else is interpolated to a string (matching APIM,
/// where only expressions yield typed values).
/// </summary>
public sealed class SetVariablePolicy(IExpressionEvaluator expressions) : IPolicy
{
    public string ElementName => "set-variable";
    public PolicySection Sections => PolicySection.All;

    public ValueTask ApplyAsync(IPolicyContext context, PolicyNode node, CancellationToken ct = default)
    {
        if (!node.Attributes.TryGetValue("name", out var name))
        {
            throw new PolicyException(ElementName, "MissingAttribute", "<set-variable> requires a 'name' attribute.");
        }
        if (!node.Attributes.TryGetValue("value", out var value))
        {
            throw new PolicyException(ElementName, "MissingAttribute", "<set-variable> requires a 'value' attribute.");
        }

        context.Variables[name] = IsExpression(value)
            ? expressions.Evaluate<object>(value, context)
            : expressions.Interpolate(value, context);
        return ValueTask.CompletedTask;
    }

    private static bool IsExpression(string value)
    {
        var trimmed = value.TrimStart();
        return trimmed.StartsWith("@(", StringComparison.Ordinal) || trimmed.StartsWith("@{", StringComparison.Ordinal);
    }
}
