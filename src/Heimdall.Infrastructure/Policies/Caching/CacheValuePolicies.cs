using System.Globalization;
using Heimdall.Application;
using Heimdall.Domain;

namespace Heimdall.Infrastructure.Policies.Caching;

/// <summary>cache-store-value: stores a (typed) value under a key for <c>duration</c> seconds.</summary>
public sealed class CacheStoreValuePolicy(ICacheStore cache, IExpressionEvaluator expressions) : IPolicy
{
    public string ElementName => "cache-store-value";
    public PolicySection Sections => PolicySection.All;

    public ValueTask ApplyAsync(IPolicyContext context, PolicyNode node, CancellationToken ct = default)
    {
        var key = expressions.Interpolate(Require(node, "key"), context);
        if (!int.TryParse(Require(node, "duration"), CultureInfo.InvariantCulture, out var seconds))
        {
            throw new PolicyException(ElementName, "MissingAttribute", "<cache-store-value> requires a numeric 'duration' attribute.");
        }

        var rawValue = Require(node, "value");
        var value = IsExpression(rawValue) ? expressions.Evaluate<object>(rawValue, context) : expressions.Interpolate(rawValue, context);

        cache.Set(key, value, TimeSpan.FromSeconds(seconds));
        return ValueTask.CompletedTask;
    }

    private string Require(PolicyNode node, string attribute) =>
        node.Attributes.TryGetValue(attribute, out var v)
            ? v
            : throw new PolicyException(ElementName, "MissingAttribute", $"<{ElementName}> requires a '{attribute}' attribute.");

    private static bool IsExpression(string value)
    {
        var trimmed = value.TrimStart();
        return trimmed.StartsWith("@(", StringComparison.Ordinal) || trimmed.StartsWith("@{", StringComparison.Ordinal);
    }
}

/// <summary>
/// cache-lookup-value: reads a cached value into a variable. On a miss, uses <c>default-value</c>
/// if given; otherwise the variable is left unset (matching APIM).
/// </summary>
public sealed class CacheLookupValuePolicy(ICacheStore cache, IExpressionEvaluator expressions) : IPolicy
{
    public string ElementName => "cache-lookup-value";
    public PolicySection Sections => PolicySection.All;

    public ValueTask ApplyAsync(IPolicyContext context, PolicyNode node, CancellationToken ct = default)
    {
        var key = expressions.Interpolate(Require(node, "key"), context);
        var variableName = Require(node, "variable-name");

        if (cache.TryGet(key, out var value))
        {
            context.Variables[variableName] = value;
        }
        else if (node.Attributes.TryGetValue("default-value", out var defaultValue))
        {
            context.Variables[variableName] = expressions.Interpolate(defaultValue, context);
        }

        return ValueTask.CompletedTask;
    }

    private string Require(PolicyNode node, string attribute) =>
        node.Attributes.TryGetValue(attribute, out var v)
            ? v
            : throw new PolicyException(ElementName, "MissingAttribute", $"<{ElementName}> requires a '{attribute}' attribute.");
}
