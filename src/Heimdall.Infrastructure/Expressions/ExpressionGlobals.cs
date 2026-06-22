using Heimdall.Application;

namespace Heimdall.Infrastructure.Expressions;

/// <summary>Globals object for compiled scripts: exposes <c>context</c> exactly as APIM expressions reference it.</summary>
public sealed class ExpressionGlobals(IPolicyContext context)
{
    public readonly IPolicyContext context = context;
}
