using Heimdall.Application;

namespace Heimdall.Api.Configuration;

/// <summary>
/// Resolves <c>{{name}}</c> tokens from the loaded config's named values, read live from the holder so
/// reloads take effect. Loading named values from config files lands with the resource model (Phase 4).
/// </summary>
public sealed class ConfigNamedValues(GatewayConfigHolder holder) : INamedValues
{
    public bool TryResolve(string name, out string value)
    {
        foreach (var namedValue in holder.Current.NamedValues)
        {
            if (string.Equals(namedValue.Name, name, StringComparison.Ordinal))
            {
                value = namedValue.Value;
                return true;
            }
        }

        value = string.Empty;
        return false;
    }

    public string Resolve(string name) =>
        TryResolve(name, out var value)
            ? value
            : throw new InvalidOperationException($"Named value '{name}' is not defined.");
}
