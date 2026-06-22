using Heimdall.Domain;
using Heimdall.Infrastructure.ApiOpsLoader;
using Heimdall.Infrastructure.XmlOpenApiLoader;
using Shouldly;
using Xunit;

namespace Heimdall.Tests.Loader;

/// <summary>
/// The two loaders read different on-disk shapes of the SAME logical config (samples/ vs
/// samples/apiops-layout/) and must produce a structurally identical <see cref="GatewayConfig"/> -
/// the proof that the APIOps loader is a pure adapter with no impact on the engine. GatewayConfig is a
/// record but its collection members compare by reference, so this compares structurally and
/// order-normalized via a canonical projection.
/// </summary>
public class LoaderParityTests
{
    [Fact]
    public async Task Both_loaders_produce_the_same_gateway_config()
    {
        var xml = await new XmlOpenApiConfigLoader().LoadAsync(RepoPath("samples"));
        var apiops = await new ApiOpsConfigLoader().LoadAsync(RepoPath("samples", "apiops-layout"));

        Canonical(apiops).ShouldBe(Canonical(xml));
    }

    private static string Canonical(GatewayConfig c) => string.Join("\n",
        "APIS:\n" + string.Join("\n", c.Apis.OrderBy(a => a.Id, StringComparer.Ordinal).Select(CanonApi)),
        "PRODUCTS:\n" + string.Join("\n", c.Products.OrderBy(p => p.Id, StringComparer.Ordinal).Select(CanonProduct)),
        "SUBSCRIPTIONS:\n" + string.Join("\n", c.Subscriptions.OrderBy(s => s.Id, StringComparer.Ordinal).Select(CanonSub)),
        "NAMEDVALUES:\n" + string.Join("\n", c.NamedValues.OrderBy(n => n.Name, StringComparer.Ordinal)
            .Select(n => $"{n.Name}={n.Value} secret={n.Secret}")),
        "BACKENDS:\n" + string.Join("\n", c.Backends.OrderBy(b => b.Id, StringComparer.Ordinal)
            .Select(b => $"{b.Id}={b.Url}")),
        "GLOBAL:\n" + CanonDoc(c.GlobalPolicy),
        "FRAGMENTS:\n" + string.Join("\n", (c.Fragments ?? new Dictionary<string, IReadOnlyList<PolicyNode>>())
            .OrderBy(f => f.Key, StringComparer.Ordinal).Select(f => $"{f.Key}:{CanonNodes(f.Value)}")));

    private static string CanonApi(Heimdall.Domain.Api a) =>   // 'Api' alone binds to the Heimdall.Api namespace here
        $"{a.Id} | {a.DisplayName} | path='{a.Path}' | url={a.ServiceUrl} | subReq={a.SubscriptionRequired} " +
        $"| products=[{string.Join(",", a.ProductIds.OrderBy(x => x, StringComparer.Ordinal))}]\n" +
        $"  policy: {CanonDoc(a.Policy)}\n" +
        string.Join("\n", a.Operations.OrderBy(o => o.Id, StringComparer.Ordinal)
            .Select(o => $"  op {o.Id} {o.Method} {o.UriTemplate}: {CanonDoc(o.Policy)}"));

    private static string CanonProduct(Product p) =>
        $"{p.Id} | {p.DisplayName} | reqSub={p.RequiresSubscription} " +
        $"| apis=[{string.Join(",", p.ApiIds.OrderBy(x => x, StringComparer.Ordinal))}] | policy: {CanonDoc(p.Policy)}";

    private static string CanonSub(Subscription s) =>
        $"{s.Id} | {s.DisplayName} | {s.PrimaryKey}/{s.SecondaryKey} | {s.Scope} " +
        $"| product={s.ProductId} | api={s.ApiId} | {s.State}";

    private static string CanonDoc(PolicyDocument? d) => d is null ? "(none)"
        : $"in[{CanonNodes(d.Inbound)}] be[{CanonNodes(d.Backend)}] out[{CanonNodes(d.Outbound)}] err[{CanonNodes(d.OnError)}]";

    private static string CanonNodes(IReadOnlyList<PolicyNode> nodes) => string.Join(";", nodes.Select(CanonNode));

    private static string CanonNode(PolicyNode n) =>
        $"{n.Name}{{{string.Join(",", n.Attributes.OrderBy(a => a.Key, StringComparer.Ordinal).Select(a => $"{a.Key}={a.Value}"))}}}" +
        $"({CanonNodes(n.Children)})" + (string.IsNullOrEmpty(n.RawText) ? "" : $"='{n.RawText.Trim()}'");

    private static string RepoPath(params string[] parts)
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "Heimdall.slnx")))
        {
            dir = Directory.GetParent(dir)?.FullName;
        }

        dir.ShouldNotBeNull("Could not locate the repo root (Heimdall.slnx).");
        return Path.Combine([dir!, .. parts]);
    }
}
