using Heimdall.Application;
using Heimdall.Domain;
using Heimdall.Infrastructure.Context;

namespace Heimdall.Infrastructure.Policies.Transforms;

/// <summary>
/// find-and-replace: replaces every occurrence of <c>from</c> with <c>to</c> in the message body
/// (the request body in inbound/backend, the response body in outbound/on-error). Both are interpolated.
/// </summary>
public sealed class FindAndReplacePolicy(IExpressionEvaluator expressions) : IPolicy
{
    public string ElementName => "find-and-replace";
    public PolicySection Sections => PolicySection.All;

    public ValueTask ApplyAsync(IPolicyContext context, PolicyNode node, CancellationToken ct = default)
    {
        if (!node.Attributes.TryGetValue("from", out var from) || from.Length == 0)
        {
            throw new PolicyException(ElementName, "MissingAttribute", "<find-and-replace> requires a non-empty 'from' attribute.");
        }

        from = expressions.Interpolate(from, context);
        var to = expressions.Interpolate(node.Attributes.TryGetValue("to", out var t) ? t : string.Empty, context);

        var isResponse = context.CurrentSection is PolicySection.Outbound or PolicySection.OnError;
        var body = isResponse ? context.Response.Body : context.Request.Body;
        if (body is null)
        {
            return ValueTask.CompletedTask;
        }

        var replaced = new HttpEmuBody(body.As<string>().Replace(from, to));
        if (isResponse)
        {
            context.Response.Body = replaced;
        }
        else
        {
            context.Request.Body = replaced;
        }

        return ValueTask.CompletedTask;
    }
}
