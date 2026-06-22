using System.Globalization;
using System.Net;
using Heimdall.Application;
using Heimdall.Domain;
using Heimdall.Infrastructure.Context;

namespace Heimdall.Infrastructure.Policies.Auth;

/// <summary>
/// check-header: requires a header to be present and (when <c>&lt;value&gt;</c>s are given) to match one
/// of them. On failure, short-circuits with the configured code/message (default 401).
/// </summary>
public sealed class CheckHeaderPolicy : IPolicy
{
    public string ElementName => "check-header";
    public PolicySection Sections => PolicySection.Inbound;

    public ValueTask ApplyAsync(IPolicyContext context, PolicyNode node, CancellationToken ct = default)
    {
        if (!node.Attributes.TryGetValue("name", out var name))
        {
            throw new PolicyException(ElementName, "MissingAttribute", "<check-header> requires a 'name' attribute.");
        }

        var ignoreCase = !node.Attributes.TryGetValue("ignore-case", out var ic)
            || string.Equals(ic, "true", StringComparison.OrdinalIgnoreCase);
        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        var expected = node.Children.Where(c => c.Name == "value").Select(c => c.RawText ?? string.Empty).ToArray();
        var present = context.Request.Headers.TryGetValue(name, out var actual);
        var ok = present && (expected.Length == 0 || actual!.Any(a => expected.Any(e => string.Equals(a, e, comparison))));

        if (!ok)
        {
            var code = node.Attributes.TryGetValue("failed-check-httpcode", out var c)
                && int.TryParse(c, CultureInfo.InvariantCulture, out var parsed) ? parsed : 401;
            context.Response.StatusCode = code;
            if (node.Attributes.TryGetValue("failed-check-error-message", out var message))
            {
                context.Response.Body = new HttpEmuBody(message);
            }
            context.ShortCircuited = true;
        }

        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// ip-filter: <c>action="allow"</c> passes only listed addresses/ranges (others get 403);
/// <c>action="forbid"</c> blocks listed ones. IPv4 addresses and address-ranges are supported;
/// IPv6 clients are not matched by ranges (a documented tier-1 boundary).
/// </summary>
public sealed class IpFilterPolicy : IPolicy
{
    public string ElementName => "ip-filter";
    public PolicySection Sections => PolicySection.Inbound;

    public ValueTask ApplyAsync(IPolicyContext context, PolicyNode node, CancellationToken ct = default)
    {
        var allow = string.Equals(node.Attributes.GetValueOrDefault("action"), "allow", StringComparison.OrdinalIgnoreCase);
        var ip = context.Request.IpAddress;

        var listed = ip is not null && (
            node.Children.Where(c => c.Name == "address").Any(a => string.Equals(a.RawText, ip, StringComparison.Ordinal)) ||
            node.Children.Where(c => c.Name == "address-range").Any(r => InRange(ip,
                r.Attributes.GetValueOrDefault("from"), r.Attributes.GetValueOrDefault("to"))));

        var blocked = allow ? !listed : listed;
        if (blocked)
        {
            context.Response.StatusCode = 403;
            context.ShortCircuited = true;
        }

        return ValueTask.CompletedTask;
    }

    private static bool InRange(string ip, string? from, string? to)
    {
        if (from is null || to is null
            || !IPAddress.TryParse(ip, out var address)
            || !IPAddress.TryParse(from, out var low)
            || !IPAddress.TryParse(to, out var high))
        {
            return false;
        }

        var value = ToUInt32(address);
        return value >= ToUInt32(low) && value <= ToUInt32(high);
    }

    private static uint ToUInt32(IPAddress ip)
    {
        var bytes = ip.GetAddressBytes();
        return bytes.Length != 4 ? 0u : ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
    }
}

/// <summary>
/// cors: answers a CORS preflight (OPTIONS + Origin + Access-Control-Request-Method) by short-circuiting
/// with the configured Access-Control-Allow-* headers. Header injection on actual responses is a tier-1 boundary.
/// </summary>
public sealed class CorsPolicy : IPolicy
{
    public string ElementName => "cors";
    public PolicySection Sections => PolicySection.Inbound;

    public ValueTask ApplyAsync(IPolicyContext context, PolicyNode node, CancellationToken ct = default)
    {
        var headers = context.Request.Headers;
        var isPreflight = string.Equals(context.Request.Method, "OPTIONS", StringComparison.OrdinalIgnoreCase)
            && headers.ContainsKey("Origin")
            && headers.ContainsKey("Access-Control-Request-Method");
        if (!isPreflight)
        {
            return ValueTask.CompletedTask;
        }

        var origins = Values(node, "allowed-origins", "origin");
        var methods = Values(node, "allowed-methods", "method");
        var allowedHeaders = Values(node, "allowed-headers", "header");

        // Access-Control-Allow-Origin must be a single value: reflect the request Origin when it is in
        // the allow-list (or "*" when wildcard/unconfigured), rather than echoing every configured origin.
        var requestOrigin = headers.TryGetValue("Origin", out var o) && o.Length > 0 ? o[0] : null;
        var allowOrigin = origins.Length == 0 || origins.Contains("*", StringComparer.Ordinal)
            ? "*"
            : requestOrigin is not null && origins.Contains(requestOrigin, StringComparer.Ordinal) ? requestOrigin : origins[0];

        context.Response.StatusCode = 200;
        context.Response.Headers["Access-Control-Allow-Origin"] = [allowOrigin];
        if (methods.Length > 0)
        {
            context.Response.Headers["Access-Control-Allow-Methods"] = [string.Join(",", methods)];
        }
        var maxAge = node.Children.FirstOrDefault(c => c.Name == "allowed-methods")?
            .Attributes.GetValueOrDefault("preflight-result-max-age");
        if (!string.IsNullOrEmpty(maxAge))
        {
            context.Response.Headers["Access-Control-Max-Age"] = [maxAge];
        }
        if (allowedHeaders.Length > 0)
        {
            context.Response.Headers["Access-Control-Allow-Headers"] = [string.Join(",", allowedHeaders)];
        }
        if (string.Equals(node.Attributes.GetValueOrDefault("allow-credentials"), "true", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.Headers["Access-Control-Allow-Credentials"] = ["true"];
        }

        context.ShortCircuited = true;
        return ValueTask.CompletedTask;
    }

    private static string[] Values(PolicyNode node, string container, string item) =>
        node.Children.FirstOrDefault(c => c.Name == container)?.Children
            .Where(c => c.Name == item)
            .Select(c => c.RawText ?? string.Empty)
            .ToArray() ?? [];
}
