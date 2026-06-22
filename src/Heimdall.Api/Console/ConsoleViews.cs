using Heimdall.Domain;

namespace Heimdall.Api.Console;

/// <summary>
/// Read-only projections of the loaded config for the console. Secrets (subscription keys, secret
/// named-value bodies) are stripped here at projection time, so they cannot leak through the API
/// regardless of serializer settings.
/// </summary>
public sealed record ConfigView(
    IReadOnlyList<ApiView> Apis,
    IReadOnlyList<ProductView> Products,
    IReadOnlyList<SubscriptionView> Subscriptions,
    IReadOnlyList<NamedValueView> NamedValues,
    IReadOnlyList<BackendView> Backends,
    IReadOnlyList<string> Fragments,
    bool HasGlobalPolicy)
{
    public static ConfigView From(GatewayConfig c) => new(
        [.. c.Apis.Select(ApiView.From)],
        [.. c.Products.Select(ProductView.From)],
        [.. c.Subscriptions.Select(SubscriptionView.From)],
        [.. c.NamedValues.Select(NamedValueView.From)],
        [.. c.Backends.Select(b => new BackendView(b.Id, b.Url.ToString()))],
        [.. c.Fragments?.Keys ?? []],
        c.GlobalPolicy is not null);
}

public sealed record ApiView(
    string Id, string DisplayName, string Path, bool SubscriptionRequired, string? ServiceUrl,
    IReadOnlyList<OperationView> Operations, IReadOnlyList<string> ProductIds, bool HasPolicy)
{
    public static ApiView From(Heimdall.Domain.Api a) => new(
        a.Id, a.DisplayName, a.Path, a.SubscriptionRequired, a.ServiceUrl?.ToString(),
        [.. a.Operations.Select(OperationView.From)], a.ProductIds, a.Policy is not null);
}

public sealed record OperationView(string Id, string Method, string UriTemplate, bool HasPolicy)
{
    public static OperationView From(Operation o) => new(o.Id, o.Method, o.UriTemplate, o.Policy is not null);
}

public sealed record ProductView(
    string Id, string DisplayName, bool RequiresSubscription, IReadOnlyList<string> ApiIds, bool HasPolicy)
{
    public static ProductView From(Product p) =>
        new(p.Id, p.DisplayName, p.RequiresSubscription, p.ApiIds, p.Policy is not null);
}

public sealed record SubscriptionView(
    string Id, string? DisplayName, string Scope, string? ProductId, string? ApiId, string State)
{
    // Primary/secondary keys are intentionally dropped: they never reach the console.
    public static SubscriptionView From(Subscription s) =>
        new(s.Id, s.DisplayName, s.Scope.ToString(), s.ProductId, s.ApiId, s.State.ToString());
}

// Value is always serialized (masked to "***" for secrets), never omitted: the SPA needs to show that
// a value exists. Do not add [JsonIgnore] for secrets - that would erase the masking indicator.
public sealed record NamedValueView(string Name, bool Secret, string Value)
{
    public static NamedValueView From(NamedValue n) => new(n.Name, n.Secret, n.Secret ? "***" : n.Value);
}

public sealed record BackendView(string Id, string Url);
