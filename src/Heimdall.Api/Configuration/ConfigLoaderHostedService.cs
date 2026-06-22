using Heimdall.Application;

namespace Heimdall.Api.Configuration;

/// <summary>Loads the gateway config into the holder at host start (fail loud on a bad/missing config).</summary>
internal sealed class ConfigLoaderHostedService(
    IConfigLoader loader,
    GatewayConfigHolder holder,
    IConfiguration configuration,
    ILogger<ConfigLoaderHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var configPath = configuration["Heimdall:ConfigPath"] ?? "samples";
        var loaderName = configuration["Heimdall:ConfigLoader"] ?? "XmlOpenApi";
        var loaded = await loader.LoadAsync(configPath, cancellationToken);

        // Apply per-environment backend overrides (Heimdall:BackendOverrides) after loading, so the same
        // on-disk config forwards to localhost on the host and to a compose service name in a container.
        var overrides = BackendOverrides.Read(configuration);
        holder.Current = BackendOverrides.Apply(loaded, overrides);
        foreach (var (apiId, url) in overrides)
        {
            logger.LogInformation("Backend override applied: API '{ApiId}' forwards to {Url}", apiId, url);
        }

        var config = holder.Current;
        logger.LogInformation(
            "Heimdall loaded via {Loader} from {ConfigPath}: {Apis} API(s), {Products} product(s), " +
            "{Subscriptions} subscription(s), {NamedValues} named value(s), {Backends} backend(s), {Fragments} fragment(s)",
            loaderName, configPath, config.Apis.Count, config.Products.Count, config.Subscriptions.Count,
            config.NamedValues.Count, config.Backends.Count, config.Fragments?.Count ?? 0);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
