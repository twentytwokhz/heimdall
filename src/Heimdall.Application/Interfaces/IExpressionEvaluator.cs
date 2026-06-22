namespace Heimdall.Application;

/// <summary>Compiles and evaluates APIM policy expressions (real C# via Roslyn, Phase 2).</summary>
public interface IExpressionEvaluator
{
    T Evaluate<T>(string expressionText, IPolicyContext context);
    string Interpolate(string template, IPolicyContext context);
}
