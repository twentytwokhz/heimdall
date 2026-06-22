using Heimdall.Api.Routing;
using Heimdall.Domain;
using Shouldly;
using Xunit;

namespace Heimdall.Tests.Routing;

public class ApiRouterTests
{
    private static GatewayConfig BuildConfig(string apiPath = "") =>
        new(
            Apis:
            [
                new Heimdall.Domain.Api(
                    Id: "acme",
                    DisplayName: "Acme Platform API",
                    Path: apiPath,
                    Operations:
                    [
                        new Operation("listCatalogItems", "GET", "/catalog/items", Policy: null),
                        new Operation("getCatalogItem", "GET", "/catalog/items/{id}", Policy: null),
                        new Operation("createCatalogItem", "POST", "/catalog/items", Policy: null),
                    ],
                    Policy: null,
                    ProductIds: []),
            ],
            Products: [],
            Subscriptions: [],
            NamedValues: [],
            Backends: [],
            GlobalPolicy: null);

    [Fact]
    public void Get_collection_matches_list_operation_with_no_values()
    {
        var match = ApiRouter.Match(BuildConfig(), "GET", "/catalog/items");

        match.ShouldNotBeNull();
        match!.Operation.Id.ShouldBe("listCatalogItems");
        match.TemplateValues.ShouldBeEmpty();
    }

    [Fact]
    public void Get_item_matches_get_operation_and_captures_id()
    {
        var match = ApiRouter.Match(BuildConfig(), "GET", "/catalog/items/42");

        match.ShouldNotBeNull();
        match!.Operation.Id.ShouldBe("getCatalogItem");
        match.TemplateValues["id"].ShouldBe("42");
    }

    [Fact]
    public void Post_collection_matches_create_operation()
    {
        var match = ApiRouter.Match(BuildConfig(), "POST", "/catalog/items");

        match.ShouldNotBeNull();
        match!.Operation.Id.ShouldBe("createCatalogItem");
    }

    [Fact]
    public void Unmatched_method_returns_null()
    {
        var match = ApiRouter.Match(BuildConfig(), "DELETE", "/catalog/items");

        match.ShouldBeNull();
    }

    [Fact]
    public void Unmatched_path_returns_null()
    {
        var match = ApiRouter.Match(BuildConfig(), "GET", "/unknown");

        match.ShouldBeNull();
    }

    [Fact]
    public void Non_empty_api_path_prefix_is_stripped()
    {
        var match = ApiRouter.Match(BuildConfig(apiPath: "acme"), "GET", "/acme/catalog/items");

        match.ShouldNotBeNull();
        match!.Operation.Id.ShouldBe("listCatalogItems");
    }
}
