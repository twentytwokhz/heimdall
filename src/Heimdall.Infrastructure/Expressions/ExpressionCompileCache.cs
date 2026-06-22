using System.Collections.Concurrent;
using Microsoft.CodeAnalysis.Scripting;

namespace Heimdall.Infrastructure.Expressions;

/// <summary>Caches compiled script delegates, keyed by (expression code, return type).</summary>
internal sealed class ExpressionCompileCache
{
    // Lazy values ensure each expression compiles exactly once even under concurrent misses
    // (ConcurrentDictionary.GetOrAdd may otherwise invoke the factory more than once).
    private readonly ConcurrentDictionary<(string Code, Type ReturnType), Lazy<object>> _delegates = new();

    public ScriptRunner<T> GetOrAdd<T>(string code, Func<string, ScriptRunner<T>> compile) =>
        (ScriptRunner<T>)_delegates
            .GetOrAdd((code, typeof(T)), key => new Lazy<object>(() => compile(key.Code)))
            .Value;
}
