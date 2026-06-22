using Heimdall.Application;
using Heimdall.Domain;
using Heimdall.Infrastructure.XmlOpenApiLoader;
using Shouldly;
using Xunit;

namespace Heimdall.Tests.Loader;

public class XmlOpenApiConfigLoaderTests
{
    [Fact]
    public async Task Loads_sample_directory_into_gateway_config()
    {
        IConfigLoader loader = new XmlOpenApiConfigLoader();

        var config = await loader.LoadAsync(SamplesDir());

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
        config.Backends[0].Url.ShouldBe(new Uri("http://localhost:8081"));

        config.GlobalPolicy.ShouldNotBeNull();
        config.GlobalPolicy!.Inbound.ShouldContain(n => n.Name == "base");
    }

    [Fact]
    public async Task Attaches_scope_policies_by_file_convention()
    {
        IConfigLoader loader = new XmlOpenApiConfigLoader();

        var config = await loader.LoadAsync(SamplesDir());
        var api = config.Apis[0];

        api.Policy.ShouldNotBeNull();                                          // acme.api.xml
        api.Operations.Single(o => o.Id == "getCatalogItem").Policy.ShouldNotBeNull();   // acme.getCatalogItem.op.xml
        api.Operations.Single(o => o.Id == "listCatalogItems").Policy.ShouldBeNull();    // no matching file
    }

    [Fact]
    public async Task Loads_products_subscriptions_and_named_values()
    {
        IConfigLoader loader = new XmlOpenApiConfigLoader();

        var config = await loader.LoadAsync(SamplesDir());

        config.Apis[0].SubscriptionRequired.ShouldBeTrue();
        config.Apis[0].ProductIds.ShouldBe(["acme-standard"]);

        var product = config.Products.ShouldHaveSingleItem();
        product.Id.ShouldBe("acme-standard");
        product.RequiresSubscription.ShouldBeTrue();
        product.ApiIds.ShouldBe(["acme"]);
        product.Policy.ShouldNotBeNull();   // acme-standard.product.xml

        var subscription = config.Subscriptions.ShouldHaveSingleItem();
        subscription.Id.ShouldBe("acme-standard-sub");
        subscription.DisplayName.ShouldBe("Acme Standard Subscription");
        subscription.PrimaryKey.ShouldBe("acme-standard-primary-key");
        subscription.SecondaryKey.ShouldBe("acme-standard-secondary-key");
        subscription.Scope.ShouldBe(SubscriptionScope.Product);
        subscription.ProductId.ShouldBe("acme-standard");
        subscription.State.ShouldBe(SubscriptionState.Active);

        var namedValue = config.NamedValues.ShouldHaveSingleItem();
        namedValue.Name.ShouldBe("backend-host");
        namedValue.Value.ShouldBe("localhost:8081");
    }

    // Walk up from the test output dir to the repo root (marked by Heimdall.slnx), then to samples/.
    private static string SamplesDir()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "Heimdall.slnx")))
        {
            dir = Directory.GetParent(dir)?.FullName;
        }

        dir.ShouldNotBeNull("Could not locate the repo root (Heimdall.slnx).");
        return Path.Combine(dir!, "samples");
    }
}
