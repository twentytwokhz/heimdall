using System.Globalization;
using Heimdall.Application;
using Heimdall.Domain;

namespace Heimdall.Infrastructure.Policies.Transforms;

/// <summary>set-status: sets the response status code (the reason phrase is accepted but not modelled).</summary>
public sealed class SetStatusPolicy : IPolicy
{
    public string ElementName => "set-status";
    public PolicySection Sections => PolicySection.Outbound | PolicySection.OnError;

    public ValueTask ApplyAsync(IPolicyContext context, PolicyNode node, CancellationToken ct = default)
    {
        if (!node.Attributes.TryGetValue("code", out var code) ||
            !int.TryParse(code, CultureInfo.InvariantCulture, out var statusCode))
        {
            throw new PolicyException(ElementName, "MissingAttribute", "<set-status> requires a numeric 'code' attribute.");
        }

        context.Response.StatusCode = statusCode;
        return ValueTask.CompletedTask;
    }
}
