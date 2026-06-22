using Heimdall.Application;
using Heimdall.Domain;

namespace Heimdall.Infrastructure.Policies.Transforms;

/// <summary>
/// set-header: adds, overrides, appends to, or deletes a header. Targets the request in
/// inbound/backend and the response in outbound/on-error (matching APIM). Each <c>&lt;value&gt;</c>
/// is expression-interpolated.
/// </summary>
public sealed class SetHeaderPolicy(IExpressionEvaluator expressions) : IPolicy
{
    public string ElementName => "set-header";
    public PolicySection Sections => PolicySection.All;

    public ValueTask ApplyAsync(IPolicyContext context, PolicyNode node, CancellationToken ct = default)
    {
        if (!node.Attributes.TryGetValue("name", out var name))
        {
            throw new PolicyException(ElementName, "MissingAttribute", "<set-header> requires a 'name' attribute.");
        }

        var existsAction = node.Attributes.TryGetValue("exists-action", out var a) ? a : "override";
        var headers = TargetHeaders(context);

        switch (existsAction)
        {
            case "delete":
                headers.Remove(name);
                return ValueTask.CompletedTask;
            case "skip" when headers.ContainsKey(name):
                return ValueTask.CompletedTask;
        }

        var values = node.Children
            .Where(c => c.Name == "value")
            .Select(c => expressions.Interpolate(c.RawText ?? string.Empty, context))
            .ToArray();

        headers[name] = existsAction == "append" && headers.TryGetValue(name, out var existing)
            ? [.. existing, .. values]
            : values;

        return ValueTask.CompletedTask;
    }

    // Request headers in inbound/backend; response headers in outbound/on-error.
    private static IDictionary<string, string[]> TargetHeaders(IPolicyContext context) =>
        context.CurrentSection is PolicySection.Outbound or PolicySection.OnError
            ? context.Response.Headers
            : context.Request.Headers;
}
