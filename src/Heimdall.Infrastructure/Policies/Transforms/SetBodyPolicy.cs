using Heimdall.Application;
using Heimdall.Domain;
using Heimdall.Infrastructure.Context;

namespace Heimdall.Infrastructure.Policies.Transforms;

/// <summary>
/// set-body: replaces the message body with the (expression-interpolated) inner text. Targets the
/// request in inbound/backend and the response in outbound/on-error.
/// </summary>
public sealed class SetBodyPolicy(IExpressionEvaluator expressions) : IPolicy
{
    public string ElementName => "set-body";
    public PolicySection Sections => PolicySection.All;

    public ValueTask ApplyAsync(IPolicyContext context, PolicyNode node, CancellationToken ct = default)
    {
        var body = new HttpEmuBody(expressions.Interpolate(node.RawText ?? string.Empty, context));

        if (context.CurrentSection is PolicySection.Outbound or PolicySection.OnError)
        {
            context.Response.Body = body;
        }
        else
        {
            context.Request.Body = body;
        }

        return ValueTask.CompletedTask;
    }
}
