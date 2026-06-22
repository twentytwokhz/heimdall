using Heimdall.Domain;

namespace Heimdall.Application;

/// <summary>The pipeline sections a policy is valid in.</summary>
[Flags]
public enum PolicySection
{
    None = 0,
    Inbound = 1 << 0,
    Backend = 1 << 1,
    Outbound = 1 << 2,
    OnError = 1 << 3,
    All = Inbound | Backend | Outbound | OnError
}

/// <summary>One APIM policy element (e.g. set-header). Implementations live in Infrastructure.</summary>
public interface IPolicy
{
    string ElementName { get; }
    PolicySection Sections { get; }
    ValueTask ApplyAsync(IPolicyContext context, PolicyNode node, CancellationToken ct = default);
}
