using Heimdall.Application;
using Heimdall.Infrastructure.Playground;
using Shouldly;
using Xunit;

namespace Heimdall.Tests.Playground;

public class HttpFileImporterTests
{
    private static readonly Uri Gateway = new("http://localhost:5000");
    private readonly HttpFileImporter _importer = new();

    private CollectionImportResult Import(string content) =>
        _importer.Import("acme.http", content, environmentContent: null, Gateway);

    [Fact]
    public void Parses_requests_split_by_separator_with_names_and_file_variables()
    {
        var content = """
        @base = https://contoso.azure-api.net

        ### List items
        GET {{base}}/catalog/items
        Accept: application/json

        ### Create item
        POST {{base}}/catalog/items
        Content-Type: application/json

        {
          "sku": "ACME-9"
        }
        """;

        var result = Import(content);

        result.Requests.Count.ShouldBe(2);

        var list = result.Requests[0];
        list.Name.ShouldBe("List items");
        list.Method.ShouldBe("GET");
        list.Url.ShouldBe("http://localhost:5000/catalog/items");
        list.Headers.ShouldContain(h => h.Name == "Accept" && h.Value == "application/json");

        var create = result.Requests[1];
        create.Name.ShouldBe("Create item");
        create.Method.ShouldBe("POST");
        create.BodyMediaType.ShouldBe("application/json");
        create.Body.ShouldNotBeNull();
        create.Body.ShouldContain("\"sku\": \"ACME-9\"");
        create.Headers.ShouldNotContain(h => h.Name == "Content-Type");
    }

    [Fact]
    public void Flags_response_handler_scripts_but_does_not_run_them()
    {
        var content = """
        ### Scripted
        GET https://contoso.azure-api.net/catalog/items

        > {%
        client.test("status is 200", () => client.assert(response.status === 200));
        %}
        """;

        var req = Import(content).Requests.ShouldHaveSingleItem();

        req.Name.ShouldBe("Scripted");
        req.Body.ShouldBeNull();
        req.Notes.ShouldContain(n => n.Contains("script", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Flags_unresolved_variables()
    {
        var content = """
        GET https://contoso.azure-api.net/catalog/items/{{id}}
        """;

        var req = Import(content).Requests.ShouldHaveSingleItem();

        req.Url.ShouldBe("http://localhost:5000/catalog/items/{{id}}");
        req.Notes.ShouldContain(n => n.Contains("id"));
    }

    [Fact]
    public void Defaults_method_to_get_when_omitted()
    {
        var content = """
        ### Bare
        https://contoso.azure-api.net/catalog/items
        """;

        var req = Import(content).Requests.ShouldHaveSingleItem();

        req.Method.ShouldBe("GET");
        req.Url.ShouldBe("http://localhost:5000/catalog/items");
    }

    [Fact]
    public void CanImport_recognises_http_and_rest_files()
    {
        _importer.CanImport("acme.http", "GET https://x").ShouldBeTrue();
        _importer.CanImport("acme.rest", "GET https://x").ShouldBeTrue();
        _importer.CanImport("acme.postman_collection.json", "{}").ShouldBeFalse();
    }
}
