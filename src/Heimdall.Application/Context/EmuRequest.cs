namespace Heimdall.Application;

/// <summary>The inbound request as exposed to expressions (<c>context.Request</c>).</summary>
public sealed class EmuRequest
{
    public required string Method { get; set; }
    public required Uri Url { get; set; }

    /// <summary>Mutable so inbound transforms (set-header, etc.) can change request headers in place.</summary>
    public required IDictionary<string, string[]> Headers { get; init; }
    public required EmuBody Body { get; set; }

    /// <summary>The client IP (<c>context.Request.IpAddress</c>); null when unavailable (e.g. in-memory test host).</summary>
    public string? IpAddress { get; set; }
}
