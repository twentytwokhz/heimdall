using Heimdall.Domain;

namespace Heimdall.Application;

/// <summary>Why a subscription key was accepted or rejected; missing vs invalid drive different 401 copy.</summary>
public enum SubscriptionKeyOutcome
{
    Allowed,
    MissingKey,
    InvalidKey,
}

/// <summary>
/// The result of validating a presented subscription key against the matched API. On
/// <see cref="SubscriptionKeyOutcome.Allowed"/>, <see cref="Subscription"/> is the matched subscription
/// (null for open APIs) and <see cref="Product"/> is set only for product-scoped access (the
/// scope-bypass rule that pulls product policies into the effective policy).
/// </summary>
public sealed record SubscriptionKeyValidationResult(
    SubscriptionKeyOutcome Outcome,
    Subscription? Subscription,
    Product? Product)
{
    public static readonly SubscriptionKeyValidationResult Missing = new(SubscriptionKeyOutcome.MissingKey, null, null);
    public static readonly SubscriptionKeyValidationResult Invalid = new(SubscriptionKeyOutcome.InvalidKey, null, null);

    public static SubscriptionKeyValidationResult Allow(Subscription? subscription, Product? product) =>
        new(SubscriptionKeyOutcome.Allowed, subscription, product);
}

/// <summary>Validates a presented subscription key against the matched API's requirement, scope, and state.</summary>
public interface ISubscriptionKeyValidator
{
    SubscriptionKeyValidationResult Validate(
        Api api,
        string? presentedKey,
        IReadOnlyList<Subscription> subscriptions,
        IReadOnlyList<Product> products);
}

/// <inheritdoc />
public sealed class SubscriptionKeyValidator : ISubscriptionKeyValidator
{
    public SubscriptionKeyValidationResult Validate(
        Api api,
        string? presentedKey,
        IReadOnlyList<Subscription> subscriptions,
        IReadOnlyList<Product> products)
    {
        // Open APIs forward without a key (APIM's subscriptionRequired = false).
        if (!api.SubscriptionRequired)
        {
            return SubscriptionKeyValidationResult.Allow(null, null);
        }

        if (string.IsNullOrWhiteSpace(presentedKey))
        {
            return SubscriptionKeyValidationResult.Missing;
        }

        var subscription = subscriptions.FirstOrDefault(s =>
            string.Equals(s.PrimaryKey, presentedKey, StringComparison.Ordinal) ||
            string.Equals(s.SecondaryKey, presentedKey, StringComparison.Ordinal));

        // Unknown key, or a key on a subscription that is not active, is rejected.
        if (subscription is null || subscription.State != SubscriptionState.Active)
        {
            return SubscriptionKeyValidationResult.Invalid;
        }

        // The scope decides whether this subscription grants access to the matched API, and whether
        // product policies apply (only product-scoped access carries a Product).
        return subscription.Scope switch
        {
            SubscriptionScope.AllAccess or SubscriptionScope.AllApis =>
                SubscriptionKeyValidationResult.Allow(subscription, null),

            SubscriptionScope.Api when subscription.ApiId is not null &&
                string.Equals(subscription.ApiId, api.Id, StringComparison.Ordinal) =>
                SubscriptionKeyValidationResult.Allow(subscription, null),

            SubscriptionScope.Product => ValidateProductScope(api, subscription, products),

            _ => SubscriptionKeyValidationResult.Invalid,
        };
    }

    private static SubscriptionKeyValidationResult ValidateProductScope(
        Api api, Subscription subscription, IReadOnlyList<Product> products)
    {
        var product = products.FirstOrDefault(p =>
            string.Equals(p.Id, subscription.ProductId, StringComparison.Ordinal));

        // The product must exist and actually include the matched API.
        if (product is null || !product.ApiIds.Contains(api.Id, StringComparer.Ordinal))
        {
            return SubscriptionKeyValidationResult.Invalid;
        }

        return SubscriptionKeyValidationResult.Allow(subscription, product);
    }
}
