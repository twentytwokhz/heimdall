using Heimdall.Application;
using Heimdall.Domain;

namespace Heimdall.Api.Configuration;

/// <summary>
/// Optional demo overlay (off by default): when <c>Heimdall:EnableDemoApi</c> is true, a second
/// descriptor (<c>config.demo.json</c>, in the same folder as the base <c>config.json</c>) is loaded
/// through the active loader and its resources are appended to the base <see cref="GatewayConfig"/>.
/// This showcases the engine on the live console without a real backend (every demo operation responds
/// via mock-response / return-response). The base config is untouched when the flag is off.
/// </summary>
public static class DemoApi
{
    private const string EnableKey = "Heimdall:EnableDemoApi";
    private const string PathKey = "Heimdall:DemoConfigPath";
    private const string DefaultDescriptor = "config.demo.json";

    /// <summary>True when the demo overlay is enabled (default false, mirroring Heimdall:EnableAdminApi).</summary>
    public static bool IsEnabled(IConfiguration configuration) =>
        configuration.GetValue(EnableKey, false);

    /// <summary>
    /// Appends the demo descriptor's resources to <paramref name="baseConfig"/>. The descriptor lives in
    /// the same folder as the base config (Heimdall:ConfigPath); override the file via
    /// <c>Heimdall:DemoConfigPath</c> (a path relative to that folder, or just a file name). Demo entries
    /// are appended after the base entries, so the base 'acme' API and the demo API both route.
    /// </summary>
    public static async Task<GatewayConfig> MergeAsync(
        GatewayConfig baseConfig, IConfigLoader loader, IConfiguration configuration, CancellationToken ct = default)
    {
        var sourcePath = configuration["Heimdall:ConfigPath"] ?? "samples";
        var descriptor = configuration[PathKey] ?? DefaultDescriptor;
        var demo = await loader.LoadAsync(sourcePath, descriptor, ct);

        return baseConfig with
        {
            Apis = [.. baseConfig.Apis, .. demo.Apis],
            Products = [.. baseConfig.Products, .. demo.Products],
            Subscriptions = [.. baseConfig.Subscriptions, .. demo.Subscriptions],
            NamedValues = [.. baseConfig.NamedValues, .. demo.NamedValues],
            Backends = [.. baseConfig.Backends, .. demo.Backends],
        };
    }
}
