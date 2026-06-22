using Heimdall.Application;
using Newtonsoft.Json;

namespace Heimdall.Infrastructure.Context;

/// <summary>
/// <see cref="EmuBody"/> backed by the raw body text. <c>As&lt;T&gt;()</c> deserializes via Newtonsoft
/// (APIM returns Newtonsoft <c>JObject</c> etc.); <c>As&lt;string&gt;()</c> returns the raw text.
/// This is a Newtonsoft seam site. String-backed for now; Phase 3 feeds it from the HttpContext body.
/// </summary>
public sealed class HttpEmuBody(string raw) : EmuBody
{
    public override T As<T>()
    {
        if (typeof(T) == typeof(string))
        {
            return (T)(object)raw;
        }

        try
        {
            return JsonConvert.DeserializeObject<T>(raw)
                ?? throw new InvalidOperationException("Request body deserialized to null.");
        }
        catch (JsonException ex)
        {
            var preview = raw[..Math.Min(raw.Length, 200)];
            throw new InvalidOperationException(
                $"Failed to deserialize the request body as {typeof(T).Name}. Body (first 200 chars): {preview}", ex);
        }
    }
}
