namespace Heimdall.Application;

/// <summary>
/// A policy raised an error at runtime. The executor catches it, populates <c>context.LastError</c>
/// from <see cref="Source"/>/<see cref="Reason"/>, and routes execution to the on-error section
/// (matching APIM's error handling).
/// </summary>
public sealed class PolicyException : Exception
{
    public PolicyException(string source, string reason, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Source = source; // the inherited Exception.Source; APIM exposes it as context.LastError.Source
        Reason = reason;
    }

    /// <summary>APIM <c>context.LastError.Reason</c> (a short machine-readable reason).</summary>
    public string Reason { get; }
}
