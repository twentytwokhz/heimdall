using Heimdall.Domain;

namespace Heimdall.Application;

/// <summary>Loads gateway configuration from a source path into the canonical model.</summary>
public interface IConfigLoader
{
    /// <param name="configFileName">
    /// The descriptor file name to read from <paramref name="sourcePath"/>. Defaults to the loader's
    /// canonical descriptor. Used by the demo overlay to load a second descriptor (config.demo.json)
    /// from the same folder; loaders whose layout has no single descriptor file may reject a non-default
    /// value (fail loud) rather than silently ignore it.
    /// </param>
    Task<GatewayConfig> LoadAsync(
        string sourcePath, string configFileName = "config.json", CancellationToken ct = default);
}
