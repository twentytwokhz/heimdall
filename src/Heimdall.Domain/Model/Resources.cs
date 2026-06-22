namespace Heimdall.Domain;

/// <summary>The single canonical configuration model that every loader produces.</summary>
public sealed record GatewayConfig(
    IReadOnlyList<Api> Apis,
    IReadOnlyList<Product> Products,
    IReadOnlyList<Subscription> Subscriptions,
    IReadOnlyList<NamedValue> NamedValues,
    IReadOnlyList<Backend> Backends,
    PolicyDocument? GlobalPolicy,
    IReadOnlyDictionary<string, IReadOnlyList<PolicyNode>>? Fragments = null)
{
    public static GatewayConfig Empty { get; } = new([], [], [], [], [], null);
}

public sealed record Api(
    string Id,
    string DisplayName,
    string Path,
    IReadOnlyList<Operation> Operations,
    PolicyDocument? Policy,
    IReadOnlyList<string> ProductIds,
    bool SubscriptionRequired = true,   // APIM's per-API gate; true is APIM's default. Open APIs set false.
    Uri? ServiceUrl = null);            // APIM's per-API "web service URL"; the default forward destination.

public sealed record Operation(
    string Id,
    string Method,
    string UriTemplate,
    PolicyDocument? Policy);

public sealed record Product(
    string Id,
    string DisplayName,
    bool RequiresSubscription,
    PolicyDocument? Policy,
    IReadOnlyList<string> ApiIds);

public sealed record Subscription(
    string Id,
    string PrimaryKey,
    string SecondaryKey,
    SubscriptionScope Scope,
    string? ProductId,
    string? ApiId,
    SubscriptionState State,
    string? DisplayName = null);   // APIM's subscription name; falls back to Id when unset.

public enum SubscriptionScope { Product, Api, AllApis, AllAccess }

public enum SubscriptionState { Active, Suspended, Cancelled }

public sealed record NamedValue(string Name, string Value, bool Secret);

public sealed record Backend(string Id, Uri Url);
