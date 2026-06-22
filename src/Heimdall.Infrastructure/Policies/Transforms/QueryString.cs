namespace Heimdall.Infrastructure.Policies.Transforms;

/// <summary>Minimal ordered query-string parse/format for the URI transforms (rewrite-uri, set-query-parameter).</summary>
internal static class QueryString
{
    public static List<KeyValuePair<string, string>> Parse(string query)
    {
        var result = new List<KeyValuePair<string, string>>();
        query = query.TrimStart('?');
        if (query.Length == 0)
        {
            return result;
        }

        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            var key = eq < 0 ? pair : pair[..eq];
            var value = eq < 0 ? string.Empty : pair[(eq + 1)..];
            result.Add(new KeyValuePair<string, string>(Uri.UnescapeDataString(key), Uri.UnescapeDataString(value)));
        }

        return result;
    }

    public static string Format(IEnumerable<KeyValuePair<string, string>> pairs) =>
        string.Join("&", pairs.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));
}
