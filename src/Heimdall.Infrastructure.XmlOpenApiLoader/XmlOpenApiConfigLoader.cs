using System.Text.Json;
using System.Text.Json.Serialization;
using Heimdall.Application;
using Heimdall.Domain;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;
using Microsoft.OpenApi.YamlReader;

namespace Heimdall.Infrastructure.XmlOpenApiLoader;

/// <summary>
/// v1 config loader: a directory with an OpenAPI spec per API, policy XML by scope convention,
/// and a config.json (apis, backends). Produces the canonical <see cref="GatewayConfig"/>.
/// </summary>
internal sealed class XmlOpenApiConfigLoader : IConfigLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<GatewayConfig> LoadAsync(
        string sourcePath, string configFileName = "config.json", CancellationToken ct = default)
    {
        var config = await ReadConfigFile(sourcePath, configFileName, ct);
        var policiesDir = Path.Combine(sourcePath, "policies");

        var settings = new OpenApiReaderSettings();
        settings.AddYamlReader();

        var apis = new List<Api>();
        foreach (var apiConfig in config.Apis ?? [])
        {
            var openApiPath = Path.Combine(sourcePath, apiConfig.OpenApiFile);
            var (document, _) = await OpenApiDocument.LoadAsync(openApiPath, settings);
            if (document is null)
            {
                throw new InvalidOperationException($"Failed to parse OpenAPI document '{apiConfig.OpenApiFile}'.");
            }

            var serviceUrl = document.Servers is { Count: > 0 } servers && servers[0].Url is { } url
                ? new Uri(url)
                : null;

            var operations = new List<Operation>();
            foreach (var (template, pathItem) in document.Paths)
            {
                if (pathItem.Operations is null)
                {
                    continue;
                }

                foreach (var (method, operation) in pathItem.Operations)
                {
                    var operationId = operation.OperationId
                        ?? throw new InvalidOperationException(
                            $"Operation {method.Method} {template} in '{apiConfig.OpenApiFile}' has no operationId.");
                    var operationPolicy = ReadPolicy(Path.Combine(policiesDir, $"{apiConfig.Id}.{operationId}.op.xml"));
                    operations.Add(new Operation(operationId, method.Method, template, operationPolicy));
                }
            }

            var apiPolicy = ReadPolicy(Path.Combine(policiesDir, $"{apiConfig.Id}.api.xml"));
            apis.Add(new Api(
                apiConfig.Id, apiConfig.DisplayName, apiConfig.Path, operations, apiPolicy,
                apiConfig.ProductIds ?? [], apiConfig.SubscriptionRequired ?? true, serviceUrl));
        }

        var backends = (config.Backends ?? [])
            .Select(b => new Backend(b.Id, new Uri(b.Url)))
            .ToList();

        // Product policy follows the same file convention as API/operation policy: {productId}.product.xml.
        var products = (config.Products ?? [])
            .Select(p => new Product(
                p.Id, p.DisplayName, p.RequiresSubscription,
                ReadPolicy(Path.Combine(policiesDir, $"{p.Id}.product.xml")), p.ApiIds ?? []))
            .ToList();

        var subscriptions = (config.Subscriptions ?? [])
            .Select(s => new Subscription(
                s.Id, s.PrimaryKey, s.SecondaryKey, s.Scope, s.ProductId, s.ApiId, s.State, s.DisplayName))
            .ToList();

        var namedValues = (config.NamedValues ?? [])
            .Select(n => new NamedValue(n.Name, n.Value, n.Secret))
            .ToList();

        var globalPolicy = ReadPolicy(Path.Combine(policiesDir, "global.xml"));
        var fragments = LoadFragments(Path.Combine(policiesDir, "fragments"));

        return new GatewayConfig(apis, products, subscriptions, namedValues, backends, globalPolicy, fragments);
    }

    // Fragment files live under policies/fragments/; the fragment-id is the file name without extension.
    private static IReadOnlyDictionary<string, IReadOnlyList<PolicyNode>> LoadFragments(string fragmentsDir)
    {
        var fragments = new Dictionary<string, IReadOnlyList<PolicyNode>>(StringComparer.Ordinal);
        if (!Directory.Exists(fragmentsDir))
        {
            return fragments;
        }

        foreach (var file in Directory.EnumerateFiles(fragmentsDir, "*.xml"))
        {
            var id = Path.GetFileNameWithoutExtension(file);
            try
            {
                fragments[id] = PolicyXmlParser.ParseFragment(File.ReadAllText(file));
            }
            catch (System.Xml.XmlException ex)
            {
                throw new InvalidOperationException($"Failed to parse policy fragment at '{file}': {ex.Message}", ex);
            }
        }

        return fragments;
    }

    private static async Task<ConfigFile> ReadConfigFile(string sourcePath, string configFileName, CancellationToken ct)
    {
        var path = Path.Combine(sourcePath, configFileName);
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<ConfigFile>(stream, JsonOptions, ct)
            ?? throw new InvalidOperationException($"Config file '{path}' is empty or invalid.");
    }

    private static PolicyDocument? ReadPolicy(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return PolicyXmlParser.Parse(File.ReadAllText(path));
        }
        catch (System.Xml.XmlException ex)
        {
            throw new InvalidOperationException($"Failed to parse policy XML at '{path}': {ex.Message}", ex);
        }
    }

    private sealed record ConfigFile(
        IReadOnlyList<ApiConfig>? Apis,
        IReadOnlyList<BackendConfig>? Backends,
        IReadOnlyList<ProductConfig>? Products,
        IReadOnlyList<SubscriptionConfig>? Subscriptions,
        IReadOnlyList<NamedValueConfig>? NamedValues);

    private sealed record ApiConfig(
        string Id,
        string DisplayName,
        string Path,
        string OpenApiFile,
        IReadOnlyList<string>? ProductIds,
        bool? SubscriptionRequired);

    private sealed record BackendConfig(string Id, string Url);

    private sealed record ProductConfig(
        string Id,
        string DisplayName,
        bool RequiresSubscription,
        IReadOnlyList<string>? ApiIds);

    private sealed record SubscriptionConfig(
        string Id,
        string PrimaryKey,
        string SecondaryKey,
        SubscriptionScope Scope,
        string? ProductId,
        string? ApiId,
        SubscriptionState State,
        string? DisplayName);

    private sealed record NamedValueConfig(string Name, string Value, bool Secret);
}
