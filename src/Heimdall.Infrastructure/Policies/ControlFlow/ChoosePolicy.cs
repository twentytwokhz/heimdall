using Heimdall.Application;
using Heimdall.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace Heimdall.Infrastructure.Policies.ControlFlow;

/// <summary>
/// choose / when / otherwise: runs the child policies of the first <c>&lt;when&gt;</c> whose condition
/// evaluates true, else the <c>&lt;otherwise&gt;</c> branch. Branch policies run in the current section.
/// </summary>
/// <remarks>Resolves the registry lazily via <see cref="IServiceProvider"/> to avoid a DI cycle (see ReturnResponsePolicy).</remarks>
public sealed class ChoosePolicy(IServiceProvider services) : IPolicy
{
    public string ElementName => "choose";
    public PolicySection Sections => PolicySection.All;

    public async ValueTask ApplyAsync(IPolicyContext context, PolicyNode node, CancellationToken ct = default)
    {
        var registry = services.GetRequiredService<IPolicyRegistry>();

        foreach (var branch in node.Children)
        {
            if (branch.Name == "when")
            {
                if (!branch.Attributes.TryGetValue("condition", out var condition))
                {
                    throw new PolicyException(ElementName, "MissingAttribute", "<when> requires a 'condition' attribute.");
                }

                if (context.Expressions.Evaluate<bool>(condition, context))
                {
                    await RunBranch(branch, registry, context, ct);
                    return;
                }
            }
            else if (branch.Name == "otherwise")
            {
                await RunBranch(branch, registry, context, ct);
                return;
            }
        }
    }

    private static async ValueTask RunBranch(PolicyNode branch, IPolicyRegistry registry, IPolicyContext context, CancellationToken ct)
    {
        foreach (var node in branch.Children)
        {
            if (context.ShortCircuited)
            {
                break;
            }

            await registry.Resolve(node.Name).ApplyAsync(context, node, ct);
        }
    }
}
