using Heimdall.Application;
using Heimdall.Domain;
using Shouldly;
using Xunit;
using DomainApi = Heimdall.Domain.Api;

namespace Heimdall.Tests.Resources;

public class SubscriptionKeyValidatorTests
{
    private static readonly SubscriptionKeyValidator Validator = new();

    private static DomainApi Api(string id = "acme", bool required = true) =>
        new(id, id, $"/{id}", [], null, [], SubscriptionRequired: required);

    private static Subscription Sub(
        string primary = "PRIMARY",
        string secondary = "SECONDARY",
        SubscriptionScope scope = SubscriptionScope.AllAccess,
        string? productId = null,
        string? apiId = null,
        SubscriptionState state = SubscriptionState.Active) =>
        new("sub-1", primary, secondary, scope, productId, apiId, state);

    private static Product Prod(string id, params string[] apiIds) =>
        new(id, id, true, null, apiIds);

    [Fact]
    public void Open_api_allows_without_a_key()
    {
        var result = Validator.Validate(Api(required: false), null, [], []);

        result.Outcome.ShouldBe(SubscriptionKeyOutcome.Allowed);
        result.Subscription.ShouldBeNull();
        result.Product.ShouldBeNull();
    }

    [Fact]
    public void Required_api_with_null_key_is_missing()
    {
        var result = Validator.Validate(Api(), null, [Sub()], []);

        result.Outcome.ShouldBe(SubscriptionKeyOutcome.MissingKey);
    }

    [Fact]
    public void Required_api_with_blank_key_is_missing()
    {
        var result = Validator.Validate(Api(), "   ", [Sub()], []);

        result.Outcome.ShouldBe(SubscriptionKeyOutcome.MissingKey);
    }

    [Fact]
    public void Valid_primary_key_with_all_access_scope_is_allowed()
    {
        var sub = Sub(scope: SubscriptionScope.AllAccess);

        var result = Validator.Validate(Api(), "PRIMARY", [sub], []);

        result.Outcome.ShouldBe(SubscriptionKeyOutcome.Allowed);
        result.Subscription.ShouldBe(sub);
        result.Product.ShouldBeNull();
    }

    [Fact]
    public void Valid_secondary_key_with_all_apis_scope_is_allowed()
    {
        var sub = Sub(scope: SubscriptionScope.AllApis);

        var result = Validator.Validate(Api(), "SECONDARY", [sub], []);

        result.Outcome.ShouldBe(SubscriptionKeyOutcome.Allowed);
        result.Subscription.ShouldBe(sub);
    }

    [Fact]
    public void Unknown_key_is_invalid()
    {
        var result = Validator.Validate(Api(), "NOPE", [Sub()], []);

        result.Outcome.ShouldBe(SubscriptionKeyOutcome.InvalidKey);
    }

    [Fact]
    public void Suspended_subscription_is_invalid()
    {
        var sub = Sub(state: SubscriptionState.Suspended);

        var result = Validator.Validate(Api(), "PRIMARY", [sub], []);

        result.Outcome.ShouldBe(SubscriptionKeyOutcome.InvalidKey);
    }

    [Fact]
    public void Cancelled_subscription_is_invalid()
    {
        var sub = Sub(state: SubscriptionState.Cancelled);

        var result = Validator.Validate(Api(), "PRIMARY", [sub], []);

        result.Outcome.ShouldBe(SubscriptionKeyOutcome.InvalidKey);
    }

    [Fact]
    public void Api_scoped_key_matching_the_api_is_allowed()
    {
        var sub = Sub(scope: SubscriptionScope.Api, apiId: "acme");

        var result = Validator.Validate(Api("acme"), "PRIMARY", [sub], []);

        result.Outcome.ShouldBe(SubscriptionKeyOutcome.Allowed);
        result.Product.ShouldBeNull();
    }

    [Fact]
    public void Api_scoped_key_for_a_different_api_is_invalid()
    {
        var sub = Sub(scope: SubscriptionScope.Api, apiId: "other");

        var result = Validator.Validate(Api("acme"), "PRIMARY", [sub], []);

        result.Outcome.ShouldBe(SubscriptionKeyOutcome.InvalidKey);
    }

    [Fact]
    public void Api_scoped_key_with_no_api_id_is_invalid()
    {
        var sub = Sub(scope: SubscriptionScope.Api, apiId: null);

        var result = Validator.Validate(Api("acme"), "PRIMARY", [sub], []);

        result.Outcome.ShouldBe(SubscriptionKeyOutcome.InvalidKey);
    }

    [Fact]
    public void Product_scoped_key_for_an_api_in_the_product_is_allowed_and_carries_the_product()
    {
        var sub = Sub(scope: SubscriptionScope.Product, productId: "prod-1");
        var product = Prod("prod-1", "acme");

        var result = Validator.Validate(Api("acme"), "PRIMARY", [sub], [product]);

        result.Outcome.ShouldBe(SubscriptionKeyOutcome.Allowed);
        result.Product.ShouldBe(product);
    }

    [Fact]
    public void Product_scoped_key_for_an_api_not_in_the_product_is_invalid()
    {
        var sub = Sub(scope: SubscriptionScope.Product, productId: "prod-1");
        var product = Prod("prod-1", "other");

        var result = Validator.Validate(Api("acme"), "PRIMARY", [sub], [product]);

        result.Outcome.ShouldBe(SubscriptionKeyOutcome.InvalidKey);
    }

    [Fact]
    public void Product_scoped_key_with_an_unknown_product_is_invalid()
    {
        var sub = Sub(scope: SubscriptionScope.Product, productId: "missing");

        var result = Validator.Validate(Api("acme"), "PRIMARY", [sub], []);

        result.Outcome.ShouldBe(SubscriptionKeyOutcome.InvalidKey);
    }
}
