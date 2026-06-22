namespace Heimdall.Application;

/// <summary>
/// The HTTP message body as APIM exposes it to expressions (<c>context.Request.Body</c>).
/// <c>As&lt;T&gt;()</c> deserializes the body; the concrete implementation lives in Infrastructure
/// so the Newtonsoft dependency stays out of the canonical model.
/// </summary>
public abstract class EmuBody
{
    /// <summary>Deserialize the body as T. <c>As&lt;string&gt;()</c> returns the raw body text.</summary>
    public abstract T As<T>();
}
