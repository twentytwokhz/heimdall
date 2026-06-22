using Heimdall.Domain;

namespace Heimdall.Api.Routing;

/// <summary>Matches an incoming request (method + path) to an API operation in the loaded config.</summary>
public static class ApiRouter
{
    /// <summary>Returns the first matching API operation, or null when nothing matches (caller maps null to 404).</summary>
    public static RouteMatch? Match(GatewayConfig config, string method, string path)
    {
        foreach (var api in config.Apis)
        {
            if (!TryStripPrefix(api.Path, path, out var remainder))
            {
                continue;
            }

            foreach (var operation in api.Operations)
            {
                if (!string.Equals(operation.Method, method, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (UriTemplateMatcher.TryMatch(operation.UriTemplate, remainder, out var values))
                {
                    return new RouteMatch(api, operation, values);
                }
            }
        }

        return null;
    }

    // Removes the API path prefix from the front of the request path. An empty prefix matches everything.
    private static bool TryStripPrefix(string apiPath, string path, out string remainder)
    {
        var prefixSegments = apiPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var pathSegments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (prefixSegments.Length > pathSegments.Length)
        {
            remainder = string.Empty;
            return false;
        }

        for (var i = 0; i < prefixSegments.Length; i++)
        {
            if (!string.Equals(prefixSegments[i], pathSegments[i], StringComparison.Ordinal))
            {
                remainder = string.Empty;
                return false;
            }
        }

        remainder = "/" + string.Join('/', pathSegments[prefixSegments.Length..]);
        return true;
    }
}
