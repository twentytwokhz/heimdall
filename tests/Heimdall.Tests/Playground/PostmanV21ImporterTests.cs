using Heimdall.Application;
using Heimdall.Infrastructure.Playground;
using Shouldly;
using Xunit;

namespace Heimdall.Tests.Playground;

public class PostmanV21ImporterTests
{
    private static readonly Uri Gateway = new("http://localhost:5000");
    private readonly PostmanV21Importer _importer = new();

    // Plain (non-interpolated) raw strings: the literal {{vars}} below are Postman placeholders, not C# holes.
    private CollectionImportResult Import(string json) =>
        _importer.Import("acme.postman_collection.json", json, environmentContent: null, Gateway);

    [Fact]
    public void Flattens_folders_into_breadcrumb_names_and_rebases_urls()
    {
        var json = """
        {
          "info": { "name": "Acme", "schema": "https://schema.getpostman.com/json/collection/v2.1.0/collection.json" },
          "item": [
            { "name": "Catalog", "item": [
              { "name": "List items", "request": {
                  "method": "GET",
                  "url": { "raw": "https://contoso.azure-api.net/catalog/items?page=1" } } }
            ] }
          ]
        }
        """;

        var result = Import(json);

        var req = result.Requests.ShouldHaveSingleItem();
        req.Name.ShouldBe("Catalog / List items");
        req.Method.ShouldBe("GET");
        req.Url.ShouldBe("http://localhost:5000/catalog/items?page=1");
        req.OriginalUrl.ShouldBe("https://contoso.azure-api.net/catalog/items?page=1");
    }

    [Fact]
    public void Resolves_collection_variables_and_flags_unresolved_ones()
    {
        var json = """
        {
          "info": { "name": "Acme", "schema": "https://schema.getpostman.com/json/collection/v2.1.0/collection.json" },
          "variable": [ { "key": "base", "value": "https://contoso.azure-api.net" } ],
          "item": [
            { "name": "Get item", "request": {
                "method": "GET",
                "header": [ { "key": "X-Trace", "value": "{{missing}}" } ],
                "url": { "raw": "{{base}}/catalog/items/1" } } }
          ]
        }
        """;

        var req = Import(json).Requests.ShouldHaveSingleItem();

        req.Url.ShouldBe("http://localhost:5000/catalog/items/1");
        req.Headers.ShouldContain(h => h.Name == "X-Trace" && h.Value == "{{missing}}");
        req.Notes.ShouldContain(n => n.Contains("missing"));
    }

    [Fact]
    public void Reads_raw_body_with_media_type_from_language()
    {
        var json = """
        {
          "info": { "name": "Acme", "schema": "https://schema.getpostman.com/json/collection/v2.1.0/collection.json" },
          "item": [
            { "name": "Create", "request": {
                "method": "POST",
                "body": { "mode": "raw", "raw": "{\"sku\":\"ACME-9\"}", "options": { "raw": { "language": "json" } } },
                "url": { "raw": "https://contoso.azure-api.net/catalog/items" } } }
          ]
        }
        """;

        var req = Import(json).Requests.ShouldHaveSingleItem();

        req.Body.ShouldBe("{\"sku\":\"ACME-9\"}");
        req.BodyMediaType.ShouldBe("application/json");
    }

    [Fact]
    public void Encodes_urlencoded_body()
    {
        var json = """
        {
          "info": { "name": "Acme", "schema": "https://schema.getpostman.com/json/collection/v2.1.0/collection.json" },
          "item": [
            { "name": "Form", "request": {
                "method": "POST",
                "body": { "mode": "urlencoded", "urlencoded": [
                  { "key": "name", "value": "Wid get" }, { "key": "qty", "value": "2" } ] },
                "url": { "raw": "https://contoso.azure-api.net/catalog/items" } } }
          ]
        }
        """;

        var req = Import(json).Requests.ShouldHaveSingleItem();

        req.BodyMediaType.ShouldBe("application/x-www-form-urlencoded");
        req.Body.ShouldBe("name=Wid+get&qty=2");
    }

    [Fact]
    public void Includes_formdata_text_fields_and_flags_file_fields()
    {
        var json = """
        {
          "info": { "name": "Acme", "schema": "https://schema.getpostman.com/json/collection/v2.1.0/collection.json" },
          "item": [
            { "name": "Upload", "request": {
                "method": "POST",
                "body": { "mode": "formdata", "formdata": [
                  { "key": "title", "value": "Hi", "type": "text" },
                  { "key": "file", "type": "file", "src": "/x.png" } ] },
                "url": { "raw": "https://contoso.azure-api.net/catalog/items" } } }
          ]
        }
        """;

        var req = Import(json).Requests.ShouldHaveSingleItem();

        req.BodyMediaType.ShouldNotBeNull();
        req.BodyMediaType.ShouldStartWith("multipart/form-data; boundary=");
        req.Body.ShouldNotBeNull();
        req.Body.ShouldContain("name=\"title\"");
        req.Body.ShouldContain("Hi");
        req.Body.ShouldNotContain("x.png");
        req.Notes.ShouldContain(n => n.Contains("file", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Flags_scripts_but_does_not_run_them()
    {
        var json = """
        {
          "info": { "name": "Acme", "schema": "https://schema.getpostman.com/json/collection/v2.1.0/collection.json" },
          "item": [
            { "name": "Scripted", "request": {
                "method": "GET", "url": { "raw": "https://contoso.azure-api.net/catalog/items" } },
              "event": [
                { "listen": "prerequest", "script": { "exec": [ "pm.environment.set('x', 1)" ] } },
                { "listen": "test", "script": { "exec": [ "pm.test('ok', () => {})" ] } } ] }
          ]
        }
        """;

        var req = Import(json).Requests.ShouldHaveSingleItem();

        req.Notes.ShouldContain(n => n.Contains("script", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Rejects_non_v21_schema_naming_the_detected_version()
    {
        var json = """
        { "info": { "name": "Old", "schema": "https://schema.getpostman.com/json/collection/v2.0.0/collection.json" }, "item": [] }
        """;

        var ex = Should.Throw<NotSupportedException>(() => Import(json));
        ex.Message.ShouldContain("v2.0.0");
        ex.Message.ShouldContain("2.1");
    }

    [Fact]
    public void CanImport_recognises_postman_json_but_not_other_json()
    {
        const string postman = """{ "info": { "schema": "https://schema.getpostman.com/json/collection/v2.1.0/collection.json" } }""";

        _importer.CanImport("acme.postman_collection.json", postman).ShouldBeTrue();
        _importer.CanImport("openapi.json", """{ "openapi": "3.0.0" }""").ShouldBeFalse();
        _importer.CanImport("requests.http", "GET https://x").ShouldBeFalse();
    }
}
