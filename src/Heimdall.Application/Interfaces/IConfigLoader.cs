using Heimdall.Domain;

namespace Heimdall.Application;

/// <summary>Loads gateway configuration from a source path into the canonical model.</summary>
public interface IConfigLoader
{
    Task<GatewayConfig> LoadAsync(string sourcePath, CancellationToken ct = default);
}
