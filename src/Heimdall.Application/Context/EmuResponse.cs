namespace Heimdall.Application;

/// <summary>The response as exposed to expressions (<c>context.Response</c>); meaningful in outbound and on-error.</summary>
public sealed class EmuResponse
{
    public int StatusCode { get; set; }

    /// <summary>Mutable so outbound transforms (set-header, etc.) can change response headers in place.</summary>
    public IDictionary<string, string[]> Headers { get; set; } =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
    public EmuBody? Body { get; set; }
}
