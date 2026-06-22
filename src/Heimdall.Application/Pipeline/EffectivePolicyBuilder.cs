using Heimdall.Domain;

namespace Heimdall.Application;

/// <summary>Computes the effective policy by splicing parent-scope sections into <c>&lt;base/&gt;</c> placeholders.</summary>
public sealed class EffectivePolicyBuilder
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<PolicyNode>> NoFragments =
        new Dictionary<string, IReadOnlyList<PolicyNode>>();

    /// <summary>
    /// Flattens a scope chain (outermost first, innermost last) into one effective policy and inlines
    /// <c>&lt;include-fragment&gt;</c> references using <paramref name="fragments"/>.
    /// </summary>
    public EffectivePolicy Build(
        IReadOnlyList<PolicyDocument?> scopes,
        IReadOnlyDictionary<string, IReadOnlyList<PolicyNode>>? fragments = null)
    {
        if (scopes.Count == 0)
        {
            return EffectivePolicy.Empty;
        }

        var frags = fragments ?? NoFragments;
        return new EffectivePolicy(
            ExpandFragments(FlattenSection(scopes, static d => d.Inbound), frags, []),
            ExpandFragments(FlattenSection(scopes, static d => d.Backend), frags, []),
            ExpandFragments(FlattenSection(scopes, static d => d.Outbound), frags, []),
            ExpandFragments(FlattenSection(scopes, static d => d.OnError), frags, []));
    }

    // Replace each <include-fragment fragment-id="x"/> with the fragment's nodes (recursing into
    // children and nested fragments). Missing or self-referential fragments fail loud.
    private static IReadOnlyList<PolicyNode> ExpandFragments(
        IReadOnlyList<PolicyNode> nodes,
        IReadOnlyDictionary<string, IReadOnlyList<PolicyNode>> fragments,
        HashSet<string> active)
    {
        var result = new List<PolicyNode>();
        foreach (var node in nodes)
        {
            if (node.Name == "include-fragment")
            {
                if (!node.Attributes.TryGetValue("fragment-id", out var id))
                {
                    throw new InvalidOperationException("<include-fragment> requires a 'fragment-id' attribute.");
                }
                if (!fragments.TryGetValue(id, out var fragment))
                {
                    throw new InvalidOperationException($"Policy fragment '{id}' referenced by <include-fragment> was not found.");
                }
                if (!active.Add(id))
                {
                    throw new InvalidOperationException($"Policy fragment '{id}' includes itself (cycle).");
                }

                result.AddRange(ExpandFragments(fragment, fragments, active));
                active.Remove(id);
            }
            else if (node.Children.Count > 0)
            {
                result.Add(node with { Children = ExpandFragments(node.Children, fragments, active) });
            }
            else
            {
                result.Add(node);
            }
        }

        return result;
    }

    // A null scope is a pure pass-through, i.e. a section containing a single <base/> node.
    private static readonly IReadOnlyList<PolicyNode> PassThrough = [new PolicyNode("base", new Dictionary<string, string>(), [], null)];

    private static IReadOnlyList<PolicyNode> FlattenSection(
        IReadOnlyList<PolicyDocument?> scopes,
        Func<PolicyDocument, IReadOnlyList<PolicyNode>> select)
    {
        IReadOnlyList<PolicyNode> parent = [];

        foreach (var scope in scopes)
        {
            var section = scope is null ? PassThrough : select(scope);
            parent = Splice(section, parent);
        }

        return parent;
    }

    // Replace each <base/> node in place with the parent's flattened sequence; keep other nodes in order.
    private static IReadOnlyList<PolicyNode> Splice(IReadOnlyList<PolicyNode> section, IReadOnlyList<PolicyNode> parent)
    {
        var result = new List<PolicyNode>();

        foreach (var node in section)
        {
            if (node.Name == "base")
            {
                result.AddRange(parent);
            }
            else
            {
                result.Add(node);
            }
        }

        return result;
    }
}
