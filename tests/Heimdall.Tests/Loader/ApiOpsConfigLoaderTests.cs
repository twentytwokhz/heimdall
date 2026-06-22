using Heimdall.Application;
using Heimdall.Domain;
using Heimdall.Infrastructure.ApiOpsLoader;
using Shouldly;
using Xunit;

namespace Heimdall.Tests.Loader;

public class ApiOpsConfigLoaderTests
{
    [Fact]
    public async Task Loads_apiops_folder_into_gateway_config()
    {
        IConfigLoader loader = new ApiOpsConfigLoader();

        var config = await loader.LoadAsync(ApiOpsDir());

        config.Apis.Count.ShouldBe(1);
        var api = config.Apis[0];
        api.Id.ShouldBe("acme");
        api.DisplayName.ShouldBe("Acme Platform API");
        api.ServiceUrl.ShouldNotBeNull();
        api.ServiceUrl!.Host.ShouldBe("localhost");
        api.ServiceUrl.Port.ShouldBe(8081);

        api.Operations.Select(o => o.Id).ShouldBe(
            ["listCatalogItems", "createCatalogItem", "getCatalogItem"], ignoreOrder: true);

        config.Backends.Count.ShouldBe(1);
        config.Backends[0].Id.ShouldBe("acme-backend");
        config.Backends[0].Url.ShouldBe(new Uri("http://localhost:8081"));

        config.GlobalPolicy.ShouldNotBeNull();
        config.GlobalPolicy!.Inbound.ShouldContain(n => n.Name == "base");
    }

    [Fact]
    public async Task Attaches_scope_policies_by_folder_convention()
    {
        IConfigLoader loader = new ApiOpsConfigLoader();

        var config = await loader.LoadAsync(ApiOpsDir());
        var api = config.Apis[0];

        api.Policy.ShouldNotBeNull();                                                    // apis/acme/policy.xml
        api.Operations.Single(o => o.Id == "getCatalogItem").Policy.ShouldNotBeNull();   // operations/getCatalogItem/policy.xml
        api.Operations.Single(o => o.Id == "listCatalogItems").Policy.ShouldBeNull();    // no operation policy file
    }

    [Fact]
    public async Task Derives_product_links_named_values_from_artifacts()
    {
        IConfigLoader loader = new ApiOpsConfigLoader();

        var config = await loader.LoadAsync(ApiOpsDir());

        config.Apis[0].SubscriptionRequired.ShouldBeTrue();
        config.Apis[0].ProductIds.ShouldBe(["acme-standard"]);   // reciprocal of the product->api link

        var product = config.Products.ShouldHaveSingleItem();
        product.Id.ShouldBe("acme-standard");
        product.DisplayName.ShouldBe("Acme Standard");
        product.RequiresSubscription.ShouldBeTrue();
        product.ApiIds.ShouldBe(["acme"]);                       // products/acme-standard/apis/acme/
        product.Policy.ShouldNotBeNull();

        var namedValue = config.NamedValues.ShouldHaveSingleItem();
        namedValue.Name.ShouldBe("backend-host");                // mapped from properties.displayName
        namedValue.Value.ShouldBe("localhost:8081");
        namedValue.Secret.ShouldBeFalse();
    }

    [Fact]
    public async Task Overrides_supply_subscription_keys_the_extractor_cannot_export()
    {
        IConfigLoader loader = new ApiOpsConfigLoader();

        var config = await loader.LoadAsync(ApiOpsDir());

        var subscription = config.Subscriptions.ShouldHaveSingleItem();
        subscription.Id.ShouldBe("acme-standard-sub");
        subscription.DisplayName.ShouldBe("Acme Standard Subscription");
        subscription.PrimaryKey.ShouldBe("acme-standard-primary-key");
        subscription.SecondaryKey.ShouldBe("acme-standard-secondary-key");
        subscription.Scope.ShouldBe(SubscriptionScope.Product);
        subscription.ProductId.ShouldBe("acme-standard");
        subscription.State.ShouldBe(SubscriptionState.Active);
    }

    [Fact]
    public async Task Service_url_from_api_information_overrides_the_spec_server()
    {
        var root = NewTempDir();
        try
        {
            var apiDir = Path.Combine(root, "apis", "widget");
            Directory.CreateDirectory(apiDir);
            await File.WriteAllTextAsync(
                Path.Combine(apiDir, "apiInformation.json"),
                """{ "properties": { "displayName": "Widget", "path": "widgets", "serviceUrl": "https://override.example:9443" } }""");
            await File.WriteAllTextAsync(
                Path.Combine(apiDir, "specification.yaml"),
                """
                openapi: 3.0.3
                info:
                  title: Widget
                  version: "1.0"
                servers:
                  - url: https://from-spec.example:1234
                paths:
                  /ping:
                    get:
                      operationId: ping
                      responses:
                        "200":
                          description: OK
                """);

            var config = await new ApiOpsConfigLoader().LoadAsync(root);

            // properties.serviceUrl wins over the spec's servers[0] (APIM "Web service URL" semantics).
            config.Apis.ShouldHaveSingleItem().ServiceUrl.ShouldBe(new Uri("https://override.example:9443"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Fails_loud_when_a_secret_named_value_has_no_value_or_override()
    {
        var root = NewTempDir();
        try
        {
            var nvDir = Path.Combine(root, "namedValues", "signing-key");
            Directory.CreateDirectory(nvDir);
            await File.WriteAllTextAsync(
                Path.Combine(nvDir, "namedValueInformation.json"),
                """{ "properties": { "displayName": "signing-key", "secret": true } }""");

            var ex = await Should.ThrowAsync<InvalidOperationException>(
                () => new ApiOpsConfigLoader().LoadAsync(root));
            ex.Message.ShouldContain("signing-key");
            ex.Message.ShouldContain("heimdall.overrides.json");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Fails_loud_on_a_pre_v6_layout()
    {
        var root = NewTempDir();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "named values"));   // the v4/v5 space-named folder

            var ex = await Should.ThrowAsync<InvalidOperationException>(
                () => new ApiOpsConfigLoader().LoadAsync(root));
            ex.Message.ShouldContain("v6");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "heimdall-apiops-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    // Walk up from the test output dir to the repo root (marked by Heimdall.slnx), then to the fixture.
    private static string ApiOpsDir()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "Heimdall.slnx")))
        {
            dir = Directory.GetParent(dir)?.FullName;
        }

        dir.ShouldNotBeNull("Could not locate the repo root (Heimdall.slnx).");
        return Path.Combine(dir!, "samples", "apiops-layout");
    }
}
