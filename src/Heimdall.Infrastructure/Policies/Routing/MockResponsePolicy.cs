using System.Globalization;
using Heimdall.Application;
using Heimdall.Domain;
using Heimdall.Infrastructure.Context;

namespace Heimdall.Infrastructure.Policies.Routing;

/// <summary>
/// mock-response: short-circuits the pipeline with a canned response (no backend call). Sets the
/// status code and content-type. Generating a sample body from the OpenAPI schema is a documented
/// fidelity boundary; the body is left empty unless a later policy sets it.
/// </summary>
public sealed class MockResponsePolicy : IPolicy
{
    public string ElementName => "mock-response";
    public PolicySection Sections => PolicySection.Inbound | PolicySection.Backend | PolicySection.Outbound;

    public ValueTask ApplyAsync(IPolicyContext context, PolicyNode node, CancellationToken ct = default)
    {
        var statusCode = node.Attributes.TryGetValue("status-code", out var s)
            && int.TryParse(s, CultureInfo.InvariantCulture, out var code) ? code : StatusOk;
        context.Response.StatusCode = statusCode;

        if (node.Attributes.TryGetValue("content-type", out var contentType))
        {
            context.Response.Headers["Content-Type"] = [contentType];
        }

        context.Response.Body ??= new HttpEmuBody(string.Empty);
        context.ShortCircuited = true;
        return ValueTask.CompletedTask;
    }

    private const int StatusOk = 200;
}
