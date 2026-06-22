using System.Text.RegularExpressions;

namespace Heimdall.Infrastructure.Playground;

/// <summary>
/// Resolves <c>{{name}}</c> placeholders from a variable map at import time. An unknown name is left
/// verbatim and recorded in <paramref name="unresolved"/> so the UI can flag it: import never silently
/// blanks a value it could not resolve.
/// </summary>
internal static partial class PlaceholderResolver
{
    [GeneratedRegex(@"\{\{([^{}]+)\}\}")]
    private static partial Regex Token();

    public static string Resolve(string? input, IReadOnlyDictionary<string, string> variables, ISet<string> unresolved)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input ?? "";
        }

        return Token().Replace(input, match =>
        {
            var key = match.Groups[1].Value.Trim();
            if (variables.TryGetValue(key, out var value))
            {
                return value;
            }

            unresolved.Add(key);
            return match.Value;
        });
    }
}
