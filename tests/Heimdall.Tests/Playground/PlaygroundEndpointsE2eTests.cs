using System.Net;
using System.Net.Http.Json;
using Heimdall.Api.Configuration;
using Heimdall.Api.Playground;
using Heimdall.Application;
using Heimdall.Domain;
using Heimdall.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace Heimdall.Tests.Playground;

/// <summary>
/// End-to-end coverage for the playground endpoints: importing a collection into replayable requests,
/// and replaying one through the live gateway so a correlated trace is recorded (including the auth-reject
/// path) without the internal correlation header reaching the backend.
/// </summary>
[Collection("gateway-e2e")]
public class PlaygroundEndpointsE2eTests
{
    private static readonly PlaygroundRequest ListItems = new(
        "List catalog items", "GET", "http://localhost/catalog/items",
        "https://acme.example.azure-api.net/catalog/items", [], Body: null, BodyMediaType: null, Notes: []);

    [Fact]
    public async Task Replaying_a_request_runs_the_gateway_and_records_a_correlated_trace()
    {
        using var backend = WireMockServer.Start();
        backend
            .Given(Request.Create().WithPath("/catalog/items").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("ok"));

        await using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        PointBackend(factory, backend);

        var post = await client.PostAsJsonAsync("/_apim/playground", ListItems);
        post.StatusCode.ShouldBe(HttpStatusCode.OK);

        var result = await post.Content.ReadFromJsonAsync<PlaygroundResponse>();
        result.ShouldNotBeNull();
        result.StatusCode.ShouldBe(200);
        result.Body.ShouldBe("ok");

        // The trace is correlated by the returned id, proving the replay-id header drove context.RequestId.
        var trace = factory.Services.GetRequiredService<ITraceSink>().Get(result.RequestId);
        trace.ShouldNotBeNull();
        trace.Outcome.ShouldBe(TraceOutcome.Completed);

        // The internal correlation header never reached the backend.
        var received = backend.LogEntries.ShouldHaveSingleItem().RequestMessage;
        received.Headers!.Keys.ShouldNotContain(ReplayCorrelation.HeaderName);
    }

    [Fact]
    public async Task Replaying_without_a_valid_key_records_a_rejected_trace_under_the_returned_id()
    {
        // The sample 'acme' API requires a key; the replay carries none, so it is rejected before the pipeline.
        await using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var post = await client.PostAsJsonAsync("/_apim/playground", ListItems);
        var result = await post.Content.ReadFromJsonAsync<PlaygroundResponse>();

        result.ShouldNotBeNull();
        result.StatusCode.ShouldBe(401);

        var trace = factory.Services.GetRequiredService<ITraceSink>().Get(result.RequestId);
        trace.ShouldNotBeNull();
        trace.Outcome.ShouldBe(TraceOutcome.Rejected);
    }

    [Fact]
    public async Task Import_endpoint_parses_an_uploaded_postman_collection_with_breadcrumbs()
    {
        await using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        using var form = await CollectionUpload("acme-catalog.postman_collection.json");
        var response = await client.PostAsync("/_apim/playground/import", form);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<CollectionImportResult>();
        result.ShouldNotBeNull();
        result.Requests.ShouldContain(r => r.Name == "Catalog / List catalog items");
        result.Requests.ShouldAllBe(r => r.Url.StartsWith("http://localhost/catalog/items"));
    }

    [Fact]
    public async Task Import_endpoint_rejects_a_non_v21_collection_with_a_clear_version_message()
    {
        await using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        using var form = await CollectionUpload("acme-v2.0.postman_collection.json");
        var response = await client.PostAsync("/_apim/playground/import", form);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).ShouldContain("v2.0.0");
    }

    private static async Task<MultipartFormDataContent> CollectionUpload(string fileName)
    {
        var path = Path.Combine(RepoPaths.SamplesDir(), "collections", fileName);
        var content = new StringContent(await File.ReadAllTextAsync(path));
        return new MultipartFormDataContent { { content, "collection", fileName } };
    }

    private static void PointBackend(TestAppFactory factory, WireMockServer backend)
    {
        var holder = factory.Services.GetRequiredService<GatewayConfigHolder>();
        var api = holder.Current.Apis[0] with { ServiceUrl = new Uri(backend.Url!), SubscriptionRequired = false };
        holder.Current = holder.Current with { Apis = [api], GlobalPolicy = new PolicyDocument([], [], [], []) };
    }
}
