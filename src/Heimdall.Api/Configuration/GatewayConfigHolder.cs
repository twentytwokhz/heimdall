using Heimdall.Domain;

namespace Heimdall.Api.Configuration;

/// <summary>
/// Holds the loaded gateway configuration. Mutable so a reload (or a test) can swap it.
/// The reference is volatile: a single writer (startup load / reload) swaps the immutable
/// <see cref="GatewayConfig"/> while many request threads read it.
/// </summary>
public sealed class GatewayConfigHolder
{
    private volatile GatewayConfig _current = GatewayConfig.Empty;

    public GatewayConfig Current
    {
        get => _current;
        set => _current = value;
    }
}
