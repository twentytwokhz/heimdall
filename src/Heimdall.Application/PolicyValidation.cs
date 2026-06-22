using Heimdall.Domain;

namespace Heimdall.Application;

/// <summary>
/// Validates an authored <see cref="PolicyDocument"/> the way the engine resolves it: every policy
/// element must be supported by the <see cref="IPolicyRegistry"/>, else it fails loud. Structural
/// elements the pipeline expands before execution (<c>base</c>, <c>include-fragment</c>) are allowed,
/// and <c>choose</c> branches are recursed into because their child policies run via the registry too.
/// Attribute- and expression-level errors are not checked here; they surface on the request trace.
/// </summary>
public static class PolicyValidation
{
    public static void Validate(PolicyDocument document, IPolicyRegistry registry)
    {
        ValidateNodes(document.Inbound, registry);
        ValidateNodes(document.Backend, registry);
        ValidateNodes(document.Outbound, registry);
        ValidateNodes(document.OnError, registry);
    }

    private static void ValidateNodes(IReadOnlyList<PolicyNode> nodes, IPolicyRegistry registry)
    {
        foreach (var node in nodes)
        {
            switch (node.Name)
            {
                // Spliced/expanded by EffectivePolicyBuilder before the registry ever sees the section.
                // include-fragment's own nodes are not recursed here: a fragment is shared config, not
                // part of the scope being authored. An unsupported policy inside a fragment still fails
                // loud at request time when the builder inlines it, so it is never silently skipped.
                case "base":
                case "include-fragment":
                    break;

                // choose runs its branch (when/otherwise) children through the registry; recurse into them.
                case "choose":
                    Require(registry, node.Name);
                    foreach (var branch in node.Children)
                    {
                        if (branch.Name is "when" or "otherwise")
                        {
                            ValidateNodes(branch.Children, registry);
                        }
                        else
                        {
                            throw new UnsupportedPolicyException(branch.Name);
                        }
                    }

                    break;

                default:
                    Require(registry, node.Name);
                    break;
            }
        }
    }

    private static void Require(IPolicyRegistry registry, string elementName)
    {
        if (!registry.IsSupported(elementName))
        {
            throw new UnsupportedPolicyException(elementName);
        }
    }
}
