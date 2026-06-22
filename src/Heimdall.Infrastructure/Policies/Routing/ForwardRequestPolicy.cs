using Heimdall.Application;
using Heimdall.Domain;

namespace Heimdall.Infrastructure.Policies.Routing;

/// <summary>
/// forward-request: in APIM this performs the call to the backend. Heimdall's pipeline executor owns
/// the (buffered) backend stage, so this element is a recognized backend-section marker - its presence
/// does not change tier-1 behavior. The timeout/buffering attributes are a documented fidelity boundary.
/// </summary>
public sealed class ForwardRequestPolicy : IPolicy
{
    public string ElementName => "forward-request";
    public PolicySection Sections => PolicySection.Backend;

    public ValueTask ApplyAsync(IPolicyContext context, PolicyNode node, CancellationToken ct = default) =>
        ValueTask.CompletedTask;
}
