using Heimdall.Domain;

namespace Heimdall.Api.Configuration;

/// <summary>
/// Per-environment override of an API's default forward target (<see cref="Api.ServiceUrl"/>), applied
/// in the host layer after the loader produces the canonical config. This is the documented
/// "default-backend via env vars" delivery hook (IMPLEMENTATION.md §11): the same on-disk config can
/// forward to <c>localhost</c> on a host run and to a compose service name in a container, selected by
/// the <c>Heimdall:BackendOverrides</c> section (env: <c>Heimdall__BackendOverrides__&lt;apiId&gt;</c>).
/// Loaders stay pure - they never see this - so loader parity is unaffected.
/// </summary>
public static class BackendOverrides
{
    private const string Section = "Heimdall:BackendOverrides";

    /// <summary>Reads the apiId -&gt; URL override map from configuration (empty when the section is absent).</summary>
    public static IReadOnlyDictionary<string, string> Read(IConfiguration configuration) =>
        configuration.GetSection(Section)
            .GetChildren()
            .Where(c => !string.IsNullOrWhiteSpace(c.Value))
            .ToDictionary(c => c.Key, c => c.Value!, StringComparer.Ordinal);

    /// <summary>Convenience overload: read the section and apply it in one call.</summary>
    public static GatewayConfig Apply(GatewayConfig config, IConfiguration configuration) =>
        Apply(config, Read(configuration));

    /// <summary>
    /// Returns <paramref name="config"/> with the named APIs' <see cref="Api.ServiceUrl"/> rewritten.
    /// Fails loud (project convention): a non-absolute http(s) URL, or an override naming an unknown
    /// API id, throws. An empty map is a no-op and returns the same instance.
    /// </summary>
    public static GatewayConfig Apply(GatewayConfig config, IReadOnlyDictionary<string, string> overrides)
    {
        if (overrides.Count == 0)
        {
            return config;
        }

        // Validate every entry before mutating anything: a single bad override fails the whole apply
        // (all-or-nothing), so the gateway never starts with a partially-applied override set.
        // Api ids are matched with ordinal (case-sensitive) equality, matching the loaded config's ids.
        foreach (var (apiId, url) in overrides)
        {
            if (config.Apis.All(a => !string.Equals(a.Id, apiId, StringComparison.Ordinal)))
            {
                var known = string.Join(", ", config.Apis.Select(a => a.Id));
                throw new InvalidOperationException(
                    $"Heimdall:BackendOverrides names unknown API id '{apiId}'. Known API ids: [{known}].");
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed)
                || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
            {
                throw new InvalidOperationException(
                    $"Heimdall:BackendOverrides['{apiId}'] = '{url}' is not an absolute http(s) URL.");
            }
        }

        var apis = config.Apis
            .Select(a => overrides.TryGetValue(a.Id, out var url) ? a with { ServiceUrl = new Uri(url) } : a)
            .ToList();

        return config with { Apis = apis };
    }
}
