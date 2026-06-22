namespace Heimdall.Application;

/// <summary>
/// Thrown when a policy element has no registered implementation. Unsupported policies fail loud
/// (never silently skipped); unlike <see cref="PolicyException"/> this is not routed to on-error.
/// </summary>
public sealed class UnsupportedPolicyException(string elementName)
    : Exception($"Policy element '<{elementName}>' is not supported by Heimdall.")
{
    public string ElementName { get; } = elementName;
}
