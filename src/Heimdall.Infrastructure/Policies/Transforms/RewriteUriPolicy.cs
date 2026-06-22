using Heimdall.Application;
using Heimdall.Domain;

namespace Heimdall.Infrastructure.Policies.Transforms;

/// <summary>
/// rewrite-uri: replaces the request path (and optionally query) with a template. By default
/// (<c>copy-unmatched-params="true"</c>) existing query parameters not named in the template are kept.
/// </summary>
public sealed class RewriteUriPolicy(IExpressionEvaluator expressions) : IPolicy
{
    public string ElementName => "rewrite-uri";
    public PolicySection Sections => PolicySection.Inbound | PolicySection.Backend;

    public ValueTask ApplyAsync(IPolicyContext context, PolicyNode node, CancellationToken ct = default)
    {
        if (!node.Attributes.TryGetValue("template", out var template))
        {
            throw new PolicyException(ElementName, "MissingAttribute", "<rewrite-uri> requires a 'template' attribute.");
        }

        template = expressions.Interpolate(template, context);
        var copyUnmatched = !node.Attributes.TryGetValue("copy-unmatched-params", out var c)
            || !string.Equals(c, "false", StringComparison.OrdinalIgnoreCase);

        var split = template.IndexOf('?');
        var newPath = split < 0 ? template : template[..split];
        var pairs = QueryString.Parse(split < 0 ? string.Empty : template[(split + 1)..]);

        if (copyUnmatched)
        {
            var named = pairs.Select(p => p.Key).ToHashSet(StringComparer.Ordinal);
            foreach (var existing in QueryString.Parse(context.Request.Url.Query))
            {
                if (!named.Contains(existing.Key))
                {
                    pairs.Add(existing);
                }
            }
        }

        context.Request.Url = new UriBuilder(context.Request.Url)
        {
            Path = newPath,
            Query = QueryString.Format(pairs),
        }.Uri;
        return ValueTask.CompletedTask;
    }
}
