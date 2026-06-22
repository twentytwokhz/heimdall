using System.Text;
using System.Text.RegularExpressions;
using Heimdall.Application;

namespace Heimdall.Infrastructure.Playground;

/// <summary>
/// Imports a <c>.http</c> / <c>.rest</c> file (VS Code REST Client / JetBrains format) into replayable
/// <see cref="PlaygroundRequest"/>s. Requests split on <c>###</c>; file-level <c>@name = value</c>
/// variables resolve <c>{{vars}}</c>; URLs rebase onto the local gateway. Response-handler scripts
/// (<c>&gt; {% ... %}</c>) are flagged, never run.
/// </summary>
public sealed partial class HttpFileImporter : ICollectionImporter
{
    private static readonly HashSet<string> Methods = new(StringComparer.OrdinalIgnoreCase)
    {
        "GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS", "TRACE", "CONNECT",
    };

    [GeneratedRegex(@"^\s*@([A-Za-z0-9_-]+)\s*=\s*(.*?)\s*$")]
    private static partial Regex FileVariable();

    public bool CanImport(string fileName, string content) =>
        fileName.EndsWith(".http", StringComparison.OrdinalIgnoreCase)
        || fileName.EndsWith(".rest", StringComparison.OrdinalIgnoreCase);

    public CollectionImportResult Import(string fileName, string content, string? environmentContent, Uri gatewayOrigin)
    {
        var lines = content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var variables = CollectFileVariables(lines);

        var requests = new List<PlaygroundRequest>();
        foreach (var (name, blockLines) in SplitIntoBlocks(lines))
        {
            var request = ParseBlock(name, blockLines, variables, gatewayOrigin);
            if (request is not null)
            {
                requests.Add(request);
            }
        }

        return new CollectionImportResult(fileName, requests, []);
    }

    private static Dictionary<string, string> CollectFileVariables(string[] lines)
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            var match = FileVariable().Match(line);
            if (match.Success)
            {
                variables[match.Groups[1].Value] = match.Groups[2].Value;
            }
        }

        return variables;
    }

    private static IEnumerable<(string? Name, List<string> Lines)> SplitIntoBlocks(string[] lines)
    {
        var blocks = new List<(string?, List<string>)>();
        string? name = null;
        var current = new List<string>();

        foreach (var line in lines)
        {
            if (line.StartsWith("###", StringComparison.Ordinal))
            {
                blocks.Add((name, current));
                name = line[3..].Trim();
                if (name.Length == 0)
                {
                    name = null;
                }

                current = [];
            }
            else
            {
                current.Add(line);
            }
        }

        blocks.Add((name, current));
        return blocks;
    }

    private static PlaygroundRequest? ParseBlock(
        string? name, List<string> lines, IReadOnlyDictionary<string, string> variables, Uri gateway)
    {
        var index = 0;
        while (index < lines.Count && IsSkippable(lines[index]))
        {
            index++;
        }

        if (index >= lines.Count)
        {
            return null;
        }

        var unresolved = new SortedSet<string>(StringComparer.Ordinal);
        var notes = new List<string>();

        var (method, originalUrl) = ParseRequestLine(lines[index]);
        index++;

        var resolvedUrl = PlaceholderResolver.Resolve(originalUrl, variables, unresolved);
        var url = UrlRebaser.Rebase(resolvedUrl, gateway, notes);

        string? contentType = null;
        var headers = new List<PlaygroundHeader>();
        while (index < lines.Count && lines[index].Trim().Length > 0 && !IsScriptMarker(lines[index]))
        {
            var raw = lines[index];
            index++;

            var colon = raw.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            var key = raw[..colon].Trim();
            var value = PlaceholderResolver.Resolve(raw[(colon + 1)..].Trim(), variables, unresolved);

            if (string.Equals(key, "Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                contentType = value;
                continue;
            }

            headers.Add(new PlaygroundHeader(key, value));
        }

        if (index < lines.Count && lines[index].Trim().Length == 0)
        {
            index++;
        }

        var bodyBuilder = new StringBuilder();
        var scriptsPresent = false;
        for (; index < lines.Count; index++)
        {
            if (IsScriptMarker(lines[index]))
            {
                scriptsPresent = true;
                break;
            }

            bodyBuilder.Append(lines[index]).Append('\n');
        }

        var body = bodyBuilder.ToString().Trim();
        var resolvedBody = body.Length == 0 ? null : PlaceholderResolver.Resolve(body, variables, unresolved);

        if (scriptsPresent)
        {
            notes.Add("Response-handler script present (> {% %}) was not executed.");
        }

        foreach (var variable in unresolved)
        {
            notes.Add("Unresolved variable: {{" + variable + "}}");
        }

        var displayName = name ?? $"{method} {url}";
        return new PlaygroundRequest(displayName, method, url, originalUrl, headers, resolvedBody, contentType, notes);
    }

    // Skippable: blank lines, comments (# or //), and @var declarations, before the request line.
    private static bool IsSkippable(string line)
    {
        var trimmed = line.TrimStart();
        return trimmed.Length == 0
            || trimmed.StartsWith('#')
            || trimmed.StartsWith("//", StringComparison.Ordinal)
            || FileVariable().IsMatch(line);
    }

    private static bool IsScriptMarker(string line) => line.TrimStart().StartsWith('>');

    private static (string Method, string Url) ParseRequestLine(string line)
    {
        var tokens = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length >= 2 && Methods.Contains(tokens[0]))
        {
            return (tokens[0].ToUpperInvariant(), tokens[1]);
        }

        return ("GET", tokens[0]);
    }
}
