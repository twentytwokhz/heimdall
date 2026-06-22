using Heimdall.Application;
using Heimdall.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace Heimdall.Infrastructure.Policies.Routing;

/// <summary>
/// return-response: builds a response from its child policies (set-status / set-header / set-body)
/// and short-circuits the pipeline. The children run with the section forced to Outbound so they
/// target the response being built (matching APIM, where return-response composes a response).
/// </summary>
/// <remarks>
/// Takes <see cref="IServiceProvider"/> rather than <see cref="IPolicyRegistry"/> directly: the
/// registry is built from all IPolicy instances, so an IPolicy depending on it at construction would
/// be a DI cycle. The registry is resolved lazily when the policy runs.
/// </remarks>
public sealed class ReturnResponsePolicy(IServiceProvider services) : IPolicy
{
    public string ElementName => "return-response";
    public PolicySection Sections => PolicySection.Inbound | PolicySection.Backend | PolicySection.Outbound | PolicySection.OnError;

    public async ValueTask ApplyAsync(IPolicyContext context, PolicyNode node, CancellationToken ct = default)
    {
        var registry = services.GetRequiredService<IPolicyRegistry>();

        // return-response starts from a fresh 200 OK; children may override status/headers/body.
        context.Response.StatusCode = 200;
        context.Response.Headers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        context.Response.Body = null;

        var saved = context.CurrentSection;
        context.CurrentSection = PolicySection.Outbound;
        try
        {
            foreach (var child in node.Children)
            {
                await registry.Resolve(child.Name).ApplyAsync(context, child, ct);
            }
        }
        finally
        {
            context.CurrentSection = saved;
        }

        context.ShortCircuited = true;
    }
}
