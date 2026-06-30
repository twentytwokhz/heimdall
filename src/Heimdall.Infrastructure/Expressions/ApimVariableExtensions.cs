namespace Heimdall.Infrastructure.Expressions;

/// <summary>
/// APIM-parity extensions for <c>context.Variables</c>. Real Azure API Management exposes
/// <c>GetValueOrDefault&lt;T&gt;(key, default)</c> on the variables collection — a value-typed
/// generic that casts the stored <see cref="object"/> to <typeparamref name="T"/>. The BCL only
/// ships <c>GetValueOrDefault</c> for <see cref="IReadOnlyDictionary{TKey,TValue}"/> (and not as a
/// value-typed generic), so policies using this common APIM idiom — e.g.
/// <c>@((string)context.Variables.GetValueOrDefault&lt;string&gt;("isPartner","") == "true")</c> —
/// would otherwise fail to compile against <see cref="IPolicyContext.Variables"/>
/// (<c>IDictionary&lt;string, object?&gt;</c>). This namespace is imported into the Roslyn script
/// options (see <see cref="ScriptOptionsFactory"/>) so expressions compile as they do in APIM.
/// </summary>
public static class ApimVariableExtensions
{
    /// <summary>
    /// Returns the value stored at <paramref name="key"/> cast to <typeparamref name="T"/>, or
    /// <paramref name="defaultValue"/> when the key is absent (or the value is not a
    /// <typeparamref name="T"/>). Mirrors APIM's <c>context.Variables.GetValueOrDefault&lt;T&gt;</c>.
    /// </summary>
    public static T GetValueOrDefault<T>(
        this IDictionary<string, object?> variables, string key, T defaultValue = default!)
        => variables.TryGetValue(key, out var value) && value is T typed ? typed : defaultValue;
}
