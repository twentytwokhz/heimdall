using System.Reflection;
using Heimdall.Application;
using Microsoft.CodeAnalysis.Scripting;
using Newtonsoft.Json.Linq;

namespace Heimdall.Infrastructure.Expressions;

/// <summary>
/// Builds the Roslyn <see cref="ScriptOptions"/> for policy expressions: the references and the
/// APIM-documented namespace allow-list. This is a Newtonsoft seam site (scripts compile against it).
/// </summary>
internal static class ScriptOptionsFactory
{
    public static ScriptOptions Create() =>
        ScriptOptions.Default
            .WithReferences(
                typeof(object).Assembly,                 // core BCL
                typeof(Enumerable).Assembly,             // System.Linq
                typeof(Uri).Assembly,                    // System.Private.Uri
                typeof(JObject).Assembly,                // Newtonsoft.Json
                typeof(IPolicyContext).Assembly,         // Heimdall.Application (context.* types)
                typeof(ExpressionGlobals).Assembly,      // Heimdall.Infrastructure (globals + EmuBody impl)
                Assembly.Load("System.Runtime"),         // facade many BCL types forward through
                Assembly.Load("netstandard"))
            .WithImports(
                "System",
                "System.Linq",
                "System.Collections.Generic",
                "System.Text",
                "System.Net",
                "Newtonsoft.Json",
                "Newtonsoft.Json.Linq",
                "Heimdall.Infrastructure.Expressions");   // APIM-parity context.Variables.GetValueOrDefault<T>
}
