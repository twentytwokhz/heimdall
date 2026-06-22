using Microsoft.AspNetCore.Http;

namespace Heimdall.Api.Middleware;

/// <summary>
/// Reads and removes the APIM subscription key on the gateway request. APIM accepts the key in the
/// <c>Ocp-Apim-Subscription-Key</c> header (preferred) or the <c>subscription-key</c> query string,
/// and strips it before forwarding so it never reaches the backend.
/// </summary>
public static class SubscriptionKey
{
    public const string HeaderName = "Ocp-Apim-Subscription-Key";
    public const string QueryName = "subscription-key";

    /// <summary>The presented key (header first, then query), or null if none was supplied.</summary>
    public static string? Extract(HttpRequest request)
    {
        var header = request.Headers[HeaderName].ToString();
        if (!string.IsNullOrEmpty(header))
        {
            return header;
        }

        var query = request.Query[QueryName].ToString();
        return string.IsNullOrEmpty(query) ? null : query;
    }

    /// <summary>Removes the key header and query parameter from the request, leaving everything else.</summary>
    public static void Strip(HttpRequest request)
    {
        request.Headers.Remove(HeaderName);

        if (!request.Query.ContainsKey(QueryName))
        {
            return;
        }

        var kept = request.Query
            .Where(p => !string.Equals(p.Key, QueryName, StringComparison.OrdinalIgnoreCase))
            .SelectMany(p => p.Value.Where(v => v is not null).Select(v => new KeyValuePair<string, string?>(p.Key, v)));
        request.QueryString = QueryString.Create(kept);
    }
}
