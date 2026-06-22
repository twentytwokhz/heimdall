using Heimdall.Application;

namespace Heimdall.Infrastructure.Policies;

/// <summary>
/// Resolves <see cref="IPolicy"/> implementations by their XML element name. Built from every
/// IPolicy registered in the container (Scrutor scan); unknown elements fail loud.
/// </summary>
public sealed class PolicyRegistry : IPolicyRegistry
{
    private readonly IReadOnlyDictionary<string, IPolicy> _byElementName;

    public PolicyRegistry(IEnumerable<IPolicy> policies)
    {
        // Element names are the literal XML tag (e.g. "set-header"), matched case-sensitively as in APIM.
        _byElementName = policies.ToDictionary(p => p.ElementName, StringComparer.Ordinal);
    }

    public bool IsSupported(string elementName) => _byElementName.ContainsKey(elementName);

    public IPolicy Resolve(string elementName) =>
        _byElementName.TryGetValue(elementName, out var policy)
            ? policy
            : throw new UnsupportedPolicyException(elementName);
}
