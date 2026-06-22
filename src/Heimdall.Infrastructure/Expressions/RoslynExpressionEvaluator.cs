using System.Text;
using Heimdall.Application;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace Heimdall.Infrastructure.Expressions;

/// <summary>
/// Evaluates APIM policy expressions (real C# via Roslyn). <c>@(...)</c> is a single expression,
/// <c>@{...}</c> a statement block returning a value. Each unique (code, return type) compiles once
/// and is cached. Scripts run against <see cref="ExpressionGlobals"/> (exposing <c>context</c>).
/// </summary>
public sealed class RoslynExpressionEvaluator : IExpressionEvaluator
{
    private readonly ScriptOptions _options = ScriptOptionsFactory.Create();
    private readonly ExpressionCompileCache _cache = new();

    // Number of actual Roslyn compilations performed (cache misses). Used by tests to assert cache hits.
    internal int CompilationCount;

    public T Evaluate<T>(string expressionText, IPolicyContext context)
    {
        var code = StripSigil(SubstituteNamedValues(expressionText, context));
        var runner = _cache.GetOrAdd<T>(code, c =>
        {
            Interlocked.Increment(ref CompilationCount);
            return CSharpScript.Create<T>(c, _options, typeof(ExpressionGlobals)).CreateDelegate();
        });

        return runner(new ExpressionGlobals(context)).GetAwaiter().GetResult();
    }

    public string Interpolate(string template, IPolicyContext context)
    {
        template = SubstituteNamedValues(template, context);
        if (string.IsNullOrEmpty(template) || !template.Contains('@'))
        {
            return template;
        }

        var result = new StringBuilder(template.Length);
        var i = 0;
        while (i < template.Length)
        {
            if (template[i] == '@' && i + 1 < template.Length && template[i + 1] is '(' or '{')
            {
                var close = template[i + 1] == '(' ? ')' : '}';
                var end = FindMatchingClose(template, i + 1, template[i + 1], close);
                if (end < 0)
                {
                    result.Append(template, i, template.Length - i);   // unbalanced: emit the rest verbatim
                    break;
                }

                var segment = template[i..(end + 1)];
                result.Append(Evaluate<object>(segment, context)?.ToString() ?? string.Empty);
                i = end + 1;
            }
            else if (template[i] == '@' && i + 1 < template.Length && template[i + 1] == '@')
            {
                result.Append('@');   // @@ is the escape for a literal @
                i += 2;
            }
            else
            {
                result.Append(template[i]);
                i++;
            }
        }

        return result.ToString();
    }

    // Replaces APIM named-value tokens {{name}} with their values before expression processing.
    private static string SubstituteNamedValues(string text, IPolicyContext context)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains("{{", StringComparison.Ordinal))
        {
            return text;
        }

        if (context.NamedValues is null)
        {
            throw new InvalidOperationException("A {{named-value}} was referenced but no named values are configured.");
        }

        var result = new StringBuilder(text.Length);
        var i = 0;
        while (i < text.Length)
        {
            if (i + 1 < text.Length && text[i] == '{' && text[i + 1] == '{')
            {
                var end = text.IndexOf("}}", i + 2, StringComparison.Ordinal);
                if (end < 0)
                {
                    result.Append(text, i, text.Length - i);   // unbalanced: emit the rest verbatim
                    break;
                }

                result.Append(context.NamedValues.Resolve(text[(i + 2)..end].Trim()));
                i = end + 2;
            }
            else
            {
                result.Append(text[i]);
                i++;
            }
        }

        return result.ToString();
    }

    private static string StripSigil(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.Length >= 3 && trimmed[0] == '@' && trimmed[1] is '(' or '{')
        {
            var open = trimmed[1];
            var close = open == '(' ? ')' : '}';
            var end = FindMatchingClose(trimmed, 1, open, close);
            if (end != trimmed.Length - 1)
            {
                throw new ArgumentException(
                    $"Malformed expression: unbalanced '{open}{close}' in \"{text}\".", nameof(text));
            }

            return trimmed[2..end];
        }

        return trimmed;
    }

    // Returns the index of the close delimiter that balances the open at openIndex, or -1 if unbalanced.
    // Skips string ("...") and char ('...') literals so delimiters inside them are not counted.
    // Known gaps (not needed by APIM expressions today): verbatim/interpolated strings and comments.
    private static int FindMatchingClose(string s, int openIndex, char open, char close)
    {
        var depth = 0;
        var j = openIndex;
        while (j < s.Length)
        {
            var c = s[j];
            if (c is '"' or '\'')
            {
                j = SkipLiteral(s, j, c);
                continue;
            }

            if (c == open)
            {
                depth++;
            }
            else if (c == close && --depth == 0)
            {
                return j;
            }

            j++;
        }

        return -1;
    }

    // Advances past a string/char literal that starts at openQuote, honoring backslash escapes.
    private static int SkipLiteral(string s, int openQuote, char quote)
    {
        var j = openQuote + 1;
        while (j < s.Length && s[j] != quote)
        {
            j += s[j] == '\\' ? 2 : 1;
        }

        return j + 1;   // position just past the closing quote
    }
}
