using System.Globalization;
using System.Security.Cryptography;
using Heimdall.Application;
using Heimdall.Domain;
using Heimdall.Infrastructure.Context;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Heimdall.Infrastructure.Policies.Auth;

/// <summary>
/// validate-jwt: validates a token's signature and lifetime against locally configured signing keys
/// (HS256 from a base64 secret, or RS256 from a PEM public key; multiple &lt;key&gt; elements are tried
/// in turn, so key rotation and HS/RS mixes work). The token is read from the configured header
/// (honouring <c>require-scheme</c>) or, failing that, the <c>query-parameter-name</c>. Optional
/// issuer/audience and <c>required-claims</c> checks; <c>clock-skew</c> (whole seconds, a Heimdall
/// convenience) relaxes lifetime. On failure, short-circuits with the configured code (default 401).
/// OpenID metadata discovery, remote JWKS, and <c>output-token-variable-name</c> are tier-1 boundaries.
/// </summary>
public sealed class ValidateJwtPolicy : IPolicy
{
    // Parsed signing keys are cached by their text: the policy is a singleton, so an RSA handle is
    // created once per unique key and reused, rather than allocated (and leaked) on every request.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, SecurityKey> _keyCache = new(StringComparer.Ordinal);

    public string ElementName => "validate-jwt";
    public PolicySection Sections => PolicySection.Inbound;

    public async ValueTask ApplyAsync(IPolicyContext context, PolicyNode node, CancellationToken ct = default)
    {
        var code = node.Attributes.TryGetValue("failed-validation-httpcode", out var c)
            && int.TryParse(c, CultureInfo.InvariantCulture, out var parsed) ? parsed : 401;

        var token = ExtractToken(context, node);
        if (token is null)
        {
            Fail(context, node, code);
            return;
        }

        var parameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = SigningKeys(node).ToList(),
            ValidateLifetime = !string.Equals(node.Attributes.GetValueOrDefault("require-expiration-time"), "false", StringComparison.OrdinalIgnoreCase),
            ClockSkew = ClockSkew(node),
            ValidIssuers = ChildValues(node, "issuers", "issuer"),
            ValidAudiences = ChildValues(node, "audiences", "audience"),
        };
        parameters.ValidateIssuer = parameters.ValidIssuers.Any();
        parameters.ValidateAudience = parameters.ValidAudiences.Any();

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, parameters);
        if (!result.IsValid || !RequiredClaimsSatisfied(node, result.ClaimsIdentity))
        {
            Fail(context, node, code);
        }
    }

    private static string? ExtractToken(IPolicyContext context, PolicyNode node)
    {
        var headerName = node.Attributes.GetValueOrDefault("header-name") ?? "Authorization";
        var requireScheme = node.Attributes.GetValueOrDefault("require-scheme");

        if (context.Request.Headers.TryGetValue(headerName, out var values) && values.Length > 0)
        {
            return FromHeader(values[0], requireScheme);
        }

        // header-name takes precedence; fall back to the query parameter when one is configured.
        var queryParam = node.Attributes.GetValueOrDefault("query-parameter-name");
        if (queryParam is not null)
        {
            var pairs = Transforms.QueryString.Parse(context.Request.Url.Query);
            // Query parameter names are case-insensitive (matching APIM).
            var match = pairs.FirstOrDefault(p => string.Equals(p.Key, queryParam, StringComparison.OrdinalIgnoreCase));
            return string.IsNullOrEmpty(match.Value) ? null : match.Value;
        }

        return null;
    }

    private static string? FromHeader(string raw, string? requireScheme)
    {
        if (requireScheme is not null)
        {
            // The value must carry exactly the required scheme, e.g. "Bearer <token>".
            var prefix = requireScheme + " ";
            return raw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? raw[prefix.Length..].Trim() : null;
        }

        return raw.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? raw["Bearer ".Length..].Trim() : raw.Trim();
    }

    private static TimeSpan ClockSkew(PolicyNode node) =>
        node.Attributes.TryGetValue("clock-skew", out var s)
            && int.TryParse(s, CultureInfo.InvariantCulture, out var seconds) && seconds > 0
            ? TimeSpan.FromSeconds(seconds)
            : TimeSpan.Zero;

    // <required-claims><claim name=".." match="all|any"><value>..</value></claim></required-claims>.
    // "all" (APIM default): the token claim must carry every listed value; "any": at least one.
    private static bool RequiredClaimsSatisfied(PolicyNode node, System.Security.Claims.ClaimsIdentity? identity)
    {
        var claims = node.Children.FirstOrDefault(c => c.Name == "required-claims")?.Children
            .Where(c => c.Name == "claim") ?? [];

        foreach (var claim in claims)
        {
            var name = claim.Attributes.GetValueOrDefault("name");
            if (name is null)
            {
                continue;
            }

            var tokenValues = identity?.FindAll(name).Select(c => c.Value).ToArray() ?? [];
            var required = claim.Children.Where(c => c.Name == "value").Select(c => c.RawText ?? string.Empty).ToArray();

            if (required.Length == 0)
            {
                if (tokenValues.Length == 0) return false;   // claim must merely be present
                continue;
            }

            var any = string.Equals(claim.Attributes.GetValueOrDefault("match"), "any", StringComparison.OrdinalIgnoreCase);
            var satisfied = any
                ? required.Any(r => tokenValues.Contains(r, StringComparer.Ordinal))
                : required.All(r => tokenValues.Contains(r, StringComparer.Ordinal));
            if (!satisfied) return false;
        }

        return true;
    }

    private IEnumerable<SecurityKey> SigningKeys(PolicyNode node)
    {
        var keys = node.Children.FirstOrDefault(c => c.Name == "issuer-signing-keys")?.Children
            .Where(c => c.Name == "key")
            .Select(c => c.RawText ?? string.Empty) ?? [];

        return keys.Where(k => k.Length > 0).Select(ParseKey);
    }

    private SecurityKey ParseKey(string keyText) => _keyCache.GetOrAdd(keyText, static text =>
    {
        if (text.Contains("-----BEGIN", StringComparison.Ordinal))
        {
            var rsa = RSA.Create();
            rsa.ImportFromPem(text);
            return new RsaSecurityKey(rsa);
        }

        return new SymmetricSecurityKey(DecodeSecret(text));
    });

    // APIM inline keys are base64; fall back to UTF-8 bytes for a plain secret.
    private static byte[] DecodeSecret(string text)
    {
        try
        {
            return Convert.FromBase64String(text);
        }
        catch (FormatException)
        {
            return System.Text.Encoding.UTF8.GetBytes(text);
        }
    }

    private static string[] ChildValues(PolicyNode node, string container, string item) =>
        node.Children.FirstOrDefault(c => c.Name == container)?.Children
            .Where(c => c.Name == item)
            .Select(c => c.RawText ?? string.Empty)
            .ToArray() ?? [];

    private static void Fail(IPolicyContext context, PolicyNode node, int code)
    {
        context.Response.StatusCode = code;
        if (node.Attributes.TryGetValue("failed-validation-error-message", out var message))
        {
            context.Response.Body = new HttpEmuBody(message);
        }
        context.ShortCircuited = true;
    }
}
