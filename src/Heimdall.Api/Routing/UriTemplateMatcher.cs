namespace Heimdall.Api.Routing;

/// <summary>Matches a request path against a single URI template, capturing {name} segment values.</summary>
public static class UriTemplateMatcher
{
    /// <summary>Returns true and the captured values when <paramref name="requestPath"/> matches <paramref name="template"/>.</summary>
    public static bool TryMatch(string template, string requestPath, out IReadOnlyDictionary<string, string> values)
    {
        var templateSegments = Split(template);
        var pathSegments = Split(requestPath);

        if (templateSegments.Length != pathSegments.Length)
        {
            values = EmptyValues;
            return false;
        }

        var captured = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < templateSegments.Length; i++)
        {
            var templateSegment = templateSegments[i];
            var pathSegment = pathSegments[i];

            if (templateSegment.Length > 1 && templateSegment[0] == '{' && templateSegment[^1] == '}')
            {
                // {name} captures exactly one non-empty path segment.
                captured[templateSegment[1..^1]] = pathSegment;
                continue;
            }

            if (!string.Equals(templateSegment, pathSegment, StringComparison.Ordinal))
            {
                values = EmptyValues;
                return false;
            }
        }

        values = captured;
        return true;
    }

    private static readonly IReadOnlyDictionary<string, string> EmptyValues =
        new Dictionary<string, string>(StringComparer.Ordinal);

    // Split into non-empty segments, which tolerates leading and trailing slashes.
    private static string[] Split(string path) =>
        path.Split('/', StringSplitOptions.RemoveEmptyEntries);
}
