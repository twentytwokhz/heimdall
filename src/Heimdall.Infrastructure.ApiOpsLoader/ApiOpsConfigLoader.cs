using System.Text.Json;
using System.Text.Json.Serialization;
using Heimdall.Application;
using Heimdall.Domain;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;
using Microsoft.OpenApi.YamlReader;

namespace Heimdall.Infrastructure.ApiOpsLoader;

/// <summary>
/// Reads an APIOps extractor folder (target layout: APIOps v6, camelCase folders) and produces the
/// canonical <see cref="GatewayConfig"/> - the same model the XmlOpenApi loader emits, so the engine is
/// unaware of which loader ran. Secrets the extractor cannot export (subscription keys, secret
/// named-value values) come from an optional <c>heimdall.overrides.json</c> at the folder root.
/// </summary>
internal sealed class ApiOpsConfigLoader : IConfigLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public async Task<GatewayConfig> LoadAsync(
        string sourcePath, string configFileName = "config.json", CancellationToken ct = default)
    {
        // The APIOps layout is a folder tree, not a single descriptor file, so it has no config.json to
        // re-point. The demo overlay (config.demo.json) is therefore unsupported with this loader; fail
        // loud rather than silently ignore the request.
        if (configFileName != "config.json")
        {
            throw new InvalidOperationException(
                "The APIOps loader has no single descriptor file, so the demo overlay (Heimdall:EnableDemoApi) " +
                "is not supported with Heimdall:ConfigLoader=ApiOps. Use the XmlOpenApi loader for the demo API.");
        }

        ApiOpsLayout.EnsureV6Layout(sourcePath);

        var overrides = await ReadOverrides(sourcePath, ct);
        var (productToApis, apiToProducts) = ReadProductApiLinks(sourcePath);

        var apis = await ReadApis(sourcePath, apiToProducts, ct);
        var products = await ReadProducts(sourcePath, productToApis, ct);
        var namedValues = await ReadNamedValues(sourcePath, overrides.NamedValues ?? EmptyOverrideMap, ct);
        var backends = await ReadBackends(sourcePath, ct);
        var fragments = ReadFragments(sourcePath);
        var globalPolicy = ReadPolicy(Path.Combine(sourcePath, ApiOpsLayout.GlobalPolicyDir, ApiOpsLayout.PolicyFile));

        var subscriptions = (overrides.Subscriptions ?? [])
            .Select(s => new Subscription(
                s.Id, s.PrimaryKey, s.SecondaryKey, s.Scope, s.ProductId, s.ApiId, s.State, s.DisplayName))
            .ToList();

        return new GatewayConfig(apis, products, subscriptions, namedValues, backends, globalPolicy, fragments);
    }

    private async Task<List<Api>> ReadApis(
        string root, IReadOnlyDictionary<string, List<string>> apiToProducts, CancellationToken ct)
    {
        var apisRoot = Path.Combine(root, ApiOpsLayout.ApisDir);
        var apis = new List<Api>();
        if (!Directory.Exists(apisRoot))
        {
            return apis;
        }

        var settings = new OpenApiReaderSettings();
        settings.AddYamlReader();

        foreach (var apiDir in Directory.EnumerateDirectories(apisRoot))
        {
            var id = Path.GetFileName(apiDir);
            var props = await ReadProperties<ApiProps>(
                RequireFile(apiDir, ApiOpsLayout.ApiInformationFile, "api", id), ct);

            var specFile = ApiOpsLayout.FindSpecFile(apiDir)
                ?? throw new InvalidOperationException($"APIOps api '{id}' has no specification.(yaml|json).");
            var (document, _) = await OpenApiDocument.LoadAsync(specFile, settings);
            if (document is null)
            {
                throw new InvalidOperationException($"Failed to parse OpenAPI document for api '{id}'.");
            }

            // APIM's "Web service URL" (apiInformation.json serviceUrl) takes precedence over the spec's
            // servers[0], mirroring the portal; fall back to the spec when the property is absent.
            Uri? serviceUrl = null;
            if (props.ServiceUrl is { Length: > 0 } configured)
            {
                serviceUrl = ParseUri(configured, $"api '{id}' serviceUrl");
            }
            else if (document.Servers is { Count: > 0 } servers && servers[0].Url is { } specUrl)
            {
                serviceUrl = ParseUri(specUrl, $"api '{id}' spec server");
            }

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
                            $"Operation {method.Method} {template} in api '{id}' has no operationId.");
                    var operationPolicy = ReadPolicy(
                        Path.Combine(apiDir, ApiOpsLayout.OperationsDir, operationId, ApiOpsLayout.PolicyFile));
                    operations.Add(new Operation(operationId, method.Method, template, operationPolicy));
                }
            }

            var apiPolicy = ReadPolicy(Path.Combine(apiDir, ApiOpsLayout.PolicyFile));
            var productIds = apiToProducts.TryGetValue(id, out var ps) ? ps : [];
            apis.Add(new Api(
                id, props.DisplayName ?? id, props.Path ?? "", operations, apiPolicy, productIds,
                props.SubscriptionRequired ?? true, serviceUrl));
        }

        return apis;
    }

    private async Task<List<Product>> ReadProducts(
        string root, IReadOnlyDictionary<string, List<string>> productToApis, CancellationToken ct)
    {
        var productsRoot = Path.Combine(root, ApiOpsLayout.ProductsDir);
        var products = new List<Product>();
        if (!Directory.Exists(productsRoot))
        {
            return products;
        }

        foreach (var productDir in Directory.EnumerateDirectories(productsRoot))
        {
            var id = Path.GetFileName(productDir);
            var props = await ReadProperties<ProductProps>(
                RequireFile(productDir, ApiOpsLayout.ProductInformationFile, "product", id), ct);
            var policy = ReadPolicy(Path.Combine(productDir, ApiOpsLayout.PolicyFile));
            var apiIds = productToApis.TryGetValue(id, out var a) ? a : [];
            products.Add(new Product(id, props.DisplayName ?? id, props.SubscriptionRequired ?? true, policy, apiIds));
        }

        return products;
    }

    private async Task<List<NamedValue>> ReadNamedValues(
        string root, IReadOnlyDictionary<string, string> overrides, CancellationToken ct)
    {
        var namedValuesRoot = Path.Combine(root, ApiOpsLayout.NamedValuesDir);
        var namedValues = new List<NamedValue>();
        if (!Directory.Exists(namedValuesRoot))
        {
            return namedValues;
        }

        foreach (var namedValueDir in Directory.EnumerateDirectories(namedValuesRoot))
        {
            var folder = Path.GetFileName(namedValueDir);
            var props = await ReadProperties<NamedValueProps>(
                RequireFile(namedValueDir, ApiOpsLayout.NamedValueInformationFile, "named value", folder), ct);

            // APIM policies reference a named value by its displayName ({{displayName}}), not the ARM name.
            var name = props.DisplayName ?? folder;

            // The extractor never exports secret/Key Vault values, so an override fills them in. A secret
            // with neither an inline value nor an override is a hard error - never serve a blank secret.
            var value = overrides.TryGetValue(name, out var overridden) ? overridden : props.Value;
            if (value is null)
            {
                throw new InvalidOperationException(
                    $"Named value '{name}' has no value (APIOps does not export secret/Key Vault values). " +
                    $"Supply it under \"namedValues\" in '{ApiOpsLayout.OverridesFile}'.");
            }

            namedValues.Add(new NamedValue(name, value, props.Secret));
        }

        return namedValues;
    }

    private async Task<List<Backend>> ReadBackends(string root, CancellationToken ct)
    {
        var backendsRoot = Path.Combine(root, ApiOpsLayout.BackendsDir);
        var backends = new List<Backend>();
        if (!Directory.Exists(backendsRoot))
        {
            return backends;
        }

        foreach (var backendDir in Directory.EnumerateDirectories(backendsRoot))
        {
            var id = Path.GetFileName(backendDir);
            var props = await ReadProperties<BackendProps>(
                RequireFile(backendDir, ApiOpsLayout.BackendInformationFile, "backend", id), ct);
            if (props.Url is not { Length: > 0 })
            {
                throw new InvalidOperationException($"APIOps backend '{id}' has no url.");
            }

            backends.Add(new Backend(id, ParseUri(props.Url, $"backend '{id}'")));
        }

        return backends;
    }

    // products/<product>/apis/<api>/ are the product<->API link folders; build both directions at once.
    private static (Dictionary<string, List<string>> ProductToApis, Dictionary<string, List<string>> ApiToProducts)
        ReadProductApiLinks(string root)
    {
        var productToApis = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var apiToProducts = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var productsRoot = Path.Combine(root, ApiOpsLayout.ProductsDir);
        if (!Directory.Exists(productsRoot))
        {
            return (productToApis, apiToProducts);
        }

        foreach (var productDir in Directory.EnumerateDirectories(productsRoot))
        {
            var productId = Path.GetFileName(productDir);
            var apiIds = productToApis[productId] = [];

            var linksDir = Path.Combine(productDir, ApiOpsLayout.ApisDir);
            if (!Directory.Exists(linksDir))
            {
                continue;
            }

            foreach (var apiLinkDir in Directory.EnumerateDirectories(linksDir))
            {
                var apiId = Path.GetFileName(apiLinkDir);
                apiIds.Add(apiId);
                if (!apiToProducts.TryGetValue(apiId, out var productIds))
                {
                    productIds = apiToProducts[apiId] = [];
                }

                productIds.Add(productId);
            }
        }

        return (productToApis, apiToProducts);
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<PolicyNode>> ReadFragments(string root)
    {
        var fragments = new Dictionary<string, IReadOnlyList<PolicyNode>>(StringComparer.Ordinal);
        var fragmentsRoot = Path.Combine(root, ApiOpsLayout.PolicyFragmentsDir);
        if (!Directory.Exists(fragmentsRoot))
        {
            return fragments;
        }

        foreach (var fragmentDir in Directory.EnumerateDirectories(fragmentsRoot))
        {
            var id = Path.GetFileName(fragmentDir);
            var policyFile = Path.Combine(fragmentDir, ApiOpsLayout.PolicyFile);
            if (!File.Exists(policyFile))
            {
                // The extractor always writes a policy.xml for every fragment, so a fragment folder
                // without one is a misconfiguration - fail loud rather than silently drop the fragment.
                throw new InvalidOperationException($"APIOps policy fragment '{id}' is missing '{ApiOpsLayout.PolicyFile}'.");
            }

            try
            {
                fragments[id] = PolicyXmlParser.ParseFragment(File.ReadAllText(policyFile));
            }
            catch (System.Xml.XmlException ex)
            {
                throw new InvalidOperationException($"Failed to parse policy fragment '{id}': {ex.Message}", ex);
            }
        }

        return fragments;
    }

    private static async Task<Overrides> ReadOverrides(string root, CancellationToken ct)
    {
        var path = Path.Combine(root, ApiOpsLayout.OverridesFile);
        if (!File.Exists(path))
        {
            return new Overrides(null, null);
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<Overrides>(stream, JsonOptions, ct)
            ?? new Overrides(null, null);
    }

    private static async Task<TProps> ReadProperties<TProps>(string file, CancellationToken ct)
    {
        await using var stream = File.OpenRead(file);
        var arm = await JsonSerializer.DeserializeAsync<Arm<TProps>>(stream, JsonOptions, ct);
        return arm is { Properties: { } props }
            ? props
            : throw new InvalidOperationException($"APIOps file '{file}' has no 'properties' object.");
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

    private static Uri ParseUri(string value, string context)
    {
        try
        {
            return new Uri(value);
        }
        catch (UriFormatException ex)
        {
            throw new InvalidOperationException($"APIOps {context} has an invalid URL '{value}': {ex.Message}", ex);
        }
    }

    private static string RequireFile(string dir, string fileName, string kind, string id)
    {
        var path = Path.Combine(dir, fileName);
        return File.Exists(path)
            ? path
            : throw new InvalidOperationException($"APIOps {kind} '{id}' is missing '{fileName}'.");
    }

    private static readonly IReadOnlyDictionary<string, string> EmptyOverrideMap =
        new Dictionary<string, string>(StringComparer.Ordinal);

    private sealed record Arm<TProps>(TProps? Properties);

    private sealed record ApiProps(string? DisplayName, string? Path, string? ServiceUrl, bool? SubscriptionRequired);

    private sealed record ProductProps(string? DisplayName, bool? SubscriptionRequired);

    private sealed record NamedValueProps(string? DisplayName, string? Value, bool Secret);

    private sealed record BackendProps(string? Url);

    private sealed record Overrides(
        IReadOnlyList<SubscriptionOverride>? Subscriptions,
        IReadOnlyDictionary<string, string>? NamedValues);

    private sealed record SubscriptionOverride(
        string Id,
        string PrimaryKey,
        string SecondaryKey,
        SubscriptionScope Scope,
        string? ProductId,
        string? ApiId,
        SubscriptionState State,
        string? DisplayName);
}
