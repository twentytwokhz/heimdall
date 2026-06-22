using Heimdall.Application;
using Heimdall.Domain;

namespace Heimdall.Infrastructure.Policies.Transforms;

/// <summary>
/// set-query-parameter: adds, overrides, appends to, or deletes a query parameter on the request URL.
/// Each <c>&lt;value&gt;</c> is expression-interpolated; mirrors set-header's exists-action semantics.
/// </summary>
public sealed class SetQueryParameterPolicy(IExpressionEvaluator expressions) : IPolicy
{
    public string ElementName => "set-query-parameter";
    public PolicySection Sections => PolicySection.Inbound | PolicySection.Backend;

    public ValueTask ApplyAsync(IPolicyContext context, PolicyNode node, CancellationToken ct = default)
    {
        if (!node.Attributes.TryGetValue("name", out var name))
        {
            throw new PolicyException(ElementName, "MissingAttribute", "<set-query-parameter> requires a 'name' attribute.");
        }

        var existsAction = node.Attributes.TryGetValue("exists-action", out var a) ? a : "override";
        var pairs = QueryString.Parse(context.Request.Url.Query);

        switch (existsAction)
        {
            case "delete":
                pairs.RemoveAll(p => p.Key == name);
                break;
            case "skip" when pairs.Any(p => p.Key == name):
                break;
            default:
                var values = node.Children
                    .Where(c => c.Name == "value")
                    .Select(c => expressions.Interpolate(c.RawText ?? string.Empty, context));
                if (existsAction != "append")
                {
                    pairs.RemoveAll(p => p.Key == name);
                }
                pairs.AddRange(values.Select(v => new KeyValuePair<string, string>(name, v)));
                break;
        }

        context.Request.Url = new UriBuilder(context.Request.Url) { Query = QueryString.Format(pairs) }.Uri;
        return ValueTask.CompletedTask;
    }
}
