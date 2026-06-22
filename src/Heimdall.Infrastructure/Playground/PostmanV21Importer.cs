using System.Net;
using System.Text;
using System.Text.Json;
using Heimdall.Application;

namespace Heimdall.Infrastructure.Playground;

/// <summary>
/// Imports a Postman Collection v2.1 export into replayable <see cref="PlaygroundRequest"/>s. Replay-only:
/// folders flatten into a breadcrumb name, <c>{{vars}}</c> resolve best-effort from collection variables
/// (plus an optional environment export), and URLs rebase onto the local gateway. Scripts are flagged,
/// never run. A non-v2.1 schema (or non-Postman JSON) fails loudly.
/// </summary>
public sealed class PostmanV21Importer : ICollectionImporter
{
    private const string FormDataBoundary = "----HeimdallPlaygroundBoundary";

    public bool CanImport(string fileName, string content)
    {
        // A Postman collection is JSON carrying the Postman schema marker. Probing content (not just the
        // .json extension) keeps CanImport honest: an OpenAPI/ARM .json is not claimed here, so the upload
        // endpoint reports "unsupported format" rather than a confusing Postman-specific parse error.
        var looksLikePostman = content.Contains("schema.getpostman.com");
        if (fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            return looksLikePostman;
        }

        return content.TrimStart().StartsWith('{') && looksLikePostman;
    }

    public CollectionImportResult Import(string fileName, string content, string? environmentContent, Uri gatewayOrigin)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(content);
        }
        catch (JsonException ex)
        {
            throw new NotSupportedException($"Not a valid JSON Postman collection: {ex.Message}", ex);
        }

        using (doc)
        {
            var root = doc.RootElement;
            RequireV21(root);

            var variables = BuildVariables(root, environmentContent);
            var requests = new List<PlaygroundRequest>();
            if (root.TryGetProperty("item", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in items.EnumerateArray())
                {
                    Walk(item, prefix: "", variables, gatewayOrigin, requests);
                }
            }

            var notes = new List<string>
            {
                environmentContent is null
                    ? "No environment supplied; {{vars}} resolved from collection variables only."
                    : "Environment applied when resolving {{vars}}.",
            };
            return new CollectionImportResult(fileName, requests, notes);
        }
    }

    private static void RequireV21(JsonElement root)
    {
        string? schema = null;
        if (root.TryGetProperty("info", out var info) && info.ValueKind == JsonValueKind.Object)
        {
            schema = GetString(info, "schema");
        }

        if (schema is null)
        {
            throw new NotSupportedException(
                "Not a recognised Postman collection: missing info.schema. Only Postman Collection v2.1 is supported.");
        }

        if (!schema.Contains("v2.1") && !schema.Contains("/2.1"))
        {
            throw new NotSupportedException(
                $"Unsupported Postman collection schema '{schema}'. Only v2.1 is supported - re-export from Postman as Collection v2.1.");
        }
    }

    private static Dictionary<string, string> BuildVariables(JsonElement root, string? environmentContent)
    {
        var vars = new Dictionary<string, string>(StringComparer.Ordinal);
        if (root.TryGetProperty("variable", out var collectionVars) && collectionVars.ValueKind == JsonValueKind.Array)
        {
            AddKeyValues(collectionVars, vars, respectEnabled: false);
        }

        if (environmentContent is not null)
        {
            using var env = JsonDocument.Parse(environmentContent);
            if (env.RootElement.TryGetProperty("values", out var values) && values.ValueKind == JsonValueKind.Array)
            {
                AddKeyValues(values, vars, respectEnabled: true);
            }
        }

        return vars;
    }

    private static void AddKeyValues(JsonElement array, Dictionary<string, string> into, bool respectEnabled)
    {
        foreach (var entry in array.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (respectEnabled && entry.TryGetProperty("enabled", out var enabled) && enabled.ValueKind == JsonValueKind.False)
            {
                continue;
            }

            var key = GetString(entry, "key");
            if (key is not null)
            {
                into[key] = GetString(entry, "value") ?? "";
            }
        }
    }

    private void Walk(
        JsonElement item, string prefix, IReadOnlyDictionary<string, string> variables, Uri gateway, List<PlaygroundRequest> output)
    {
        var name = GetString(item, "name") ?? "(unnamed)";
        var label = string.IsNullOrEmpty(prefix) ? name : $"{prefix} / {name}";

        if (item.TryGetProperty("item", out var children) && children.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in children.EnumerateArray())
            {
                Walk(child, label, variables, gateway, output);
            }

            return;
        }

        if (item.TryGetProperty("request", out var request) && request.ValueKind == JsonValueKind.Object)
        {
            output.Add(ParseRequest(label, item, request, variables, gateway));
        }
    }

    private PlaygroundRequest ParseRequest(
        string name, JsonElement item, JsonElement request, IReadOnlyDictionary<string, string> variables, Uri gateway)
    {
        var unresolved = new SortedSet<string>(StringComparer.Ordinal);
        var notes = new List<string>();

        var method = (GetString(request, "method") ?? "GET").ToUpperInvariant();

        var originalUrl = ReadRawUrl(request) ?? "";
        var resolvedUrl = PlaceholderResolver.Resolve(originalUrl, variables, unresolved);
        var url = UrlRebaser.Rebase(resolvedUrl, gateway, notes);

        string? explicitContentType = null;
        var headers = new List<PlaygroundHeader>();
        if (request.TryGetProperty("header", out var headerArray) && headerArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var header in headerArray.EnumerateArray())
            {
                if (header.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (header.TryGetProperty("disabled", out var disabled) && disabled.ValueKind == JsonValueKind.True)
                {
                    continue;
                }

                var key = GetString(header, "key");
                if (key is null)
                {
                    continue;
                }

                var value = PlaceholderResolver.Resolve(GetString(header, "value"), variables, unresolved);

                // Content-Type is carried by BodyMediaType so replay sets it on the content, not as a plain header.
                if (string.Equals(key, "Content-Type", StringComparison.OrdinalIgnoreCase))
                {
                    explicitContentType = value;
                    continue;
                }

                headers.Add(new PlaygroundHeader(key, value));
            }
        }

        var (body, mediaType) = ReadBody(request, variables, unresolved, notes, explicitContentType);

        if (HasScripts(item))
        {
            notes.Add("Scripts present (prerequest/test) were not executed.");
        }

        foreach (var name2 in unresolved)
        {
            notes.Add("Unresolved variable: {{" + name2 + "}}");
        }

        return new PlaygroundRequest(name, method, url, originalUrl, headers, body, mediaType, notes);
    }

    private static string? ReadRawUrl(JsonElement request)
    {
        if (!request.TryGetProperty("url", out var url))
        {
            return null;
        }

        if (url.ValueKind == JsonValueKind.String)
        {
            return url.GetString();
        }

        return url.ValueKind == JsonValueKind.Object ? GetString(url, "raw") : null;
    }

    private static (string? Body, string? MediaType) ReadBody(
        JsonElement request,
        IReadOnlyDictionary<string, string> variables,
        ISet<string> unresolved,
        List<string> notes,
        string? explicitContentType)
    {
        if (!request.TryGetProperty("body", out var body) || body.ValueKind != JsonValueKind.Object)
        {
            return (null, explicitContentType);
        }

        switch (GetString(body, "mode"))
        {
            case "raw":
                var raw = PlaceholderResolver.Resolve(GetString(body, "raw"), variables, unresolved);
                return (raw, explicitContentType ?? RawLanguageMediaType(body));

            case "urlencoded":
                return (BuildUrlEncoded(body, variables, unresolved), explicitContentType ?? "application/x-www-form-urlencoded");

            case "formdata":
                return (BuildFormData(body, variables, unresolved, notes), $"multipart/form-data; boundary={FormDataBoundary}");

            case { } mode:
                notes.Add($"Body mode '{mode}' is not supported for replay; body omitted.");
                return (null, explicitContentType);

            default:
                return (null, explicitContentType);
        }
    }

    private static string? RawLanguageMediaType(JsonElement body)
    {
        if (body.TryGetProperty("options", out var options) && options.ValueKind == JsonValueKind.Object
            && options.TryGetProperty("raw", out var raw) && raw.ValueKind == JsonValueKind.Object)
        {
            return GetString(raw, "language") switch
            {
                "json" => "application/json",
                "xml" => "application/xml",
                "html" => "text/html",
                "text" => "text/plain",
                "javascript" => "application/javascript",
                _ => null,
            };
        }

        return null;
    }

    private static string BuildUrlEncoded(JsonElement body, IReadOnlyDictionary<string, string> variables, ISet<string> unresolved)
    {
        if (!body.TryGetProperty("urlencoded", out var fields) || fields.ValueKind != JsonValueKind.Array)
        {
            return "";
        }

        var parts = new List<string>();
        foreach (var field in fields.EnumerateArray())
        {
            if (field.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (field.TryGetProperty("disabled", out var disabled) && disabled.ValueKind == JsonValueKind.True)
            {
                continue;
            }

            var key = GetString(field, "key");
            if (key is null)
            {
                continue;
            }

            var value = PlaceholderResolver.Resolve(GetString(field, "value"), variables, unresolved);
            parts.Add($"{WebUtility.UrlEncode(key)}={WebUtility.UrlEncode(value)}");
        }

        return string.Join("&", parts);
    }

    private static string BuildFormData(
        JsonElement body, IReadOnlyDictionary<string, string> variables, ISet<string> unresolved, List<string> notes)
    {
        var builder = new StringBuilder();
        if (body.TryGetProperty("formdata", out var fields) && fields.ValueKind == JsonValueKind.Array)
        {
            foreach (var field in fields.EnumerateArray())
            {
                if (field.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (field.TryGetProperty("disabled", out var disabled) && disabled.ValueKind == JsonValueKind.True)
                {
                    continue;
                }

                var key = GetString(field, "key");
                if (key is null)
                {
                    continue;
                }

                if (string.Equals(GetString(field, "type"), "file", StringComparison.OrdinalIgnoreCase))
                {
                    notes.Add($"Form-data file field '{key}' was not replayed (file uploads are not supported).");
                    continue;
                }

                var value = PlaceholderResolver.Resolve(GetString(field, "value"), variables, unresolved);
                builder.Append("--").Append(FormDataBoundary).Append("\r\n");
                builder.Append("Content-Disposition: form-data; name=\"").Append(key).Append("\"\r\n\r\n");
                builder.Append(value).Append("\r\n");
            }
        }

        builder.Append("--").Append(FormDataBoundary).Append("--\r\n");
        return builder.ToString();
    }

    private static bool HasScripts(JsonElement item)
    {
        if (!item.TryGetProperty("event", out var events) || events.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var entry in events.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object
                || !entry.TryGetProperty("script", out var script) || script.ValueKind != JsonValueKind.Object
                || !script.TryGetProperty("exec", out var exec))
            {
                continue;
            }

            if (exec.ValueKind == JsonValueKind.Array
                && exec.EnumerateArray().Any(line => line.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(line.GetString())))
            {
                return true;
            }

            if (exec.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(exec.GetString()))
            {
                return true;
            }
        }

        return false;
    }

    private static string? GetString(JsonElement obj, string property) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
