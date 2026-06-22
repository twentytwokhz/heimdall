using System.Net;
using Heimdall.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;
using Yarp.ReverseProxy.Forwarder;

namespace Heimdall.Tests.Forwarding;

/// <summary>
/// Phase 3 Batch 0 spike: confirms the buffered, policy-aware forwarding model.
/// A custom <see cref="HttpTransformer"/> whose <c>TransformResponseAsync</c> reads the backend
/// response into local state and returns <c>false</c> must (a) capture status/headers/body and
/// (b) suppress YARP's auto-copy, leaving <c>HttpContext.Response</c> unstarted so the gateway can
/// run outbound policies and write its own response. De-risks the executor's backend stage before
/// the policy batches build on it. Fallback if this fails: capture via HttpMessageInvoker.SendAsync.
/// </summary>
public class ResponseCaptureSpikeTests
{
    private sealed class CapturingTransformer : HttpTransformer
    {
        public int CapturedStatus { get; private set; }
        public string? CapturedBody { get; private set; }
        public bool CapturedBackendHeader { get; private set; }

        public override async ValueTask<bool> TransformResponseAsync(
            HttpContext httpContext, HttpResponseMessage? proxyResponse, CancellationToken cancellationToken)
        {
            CapturedStatus = (int)proxyResponse!.StatusCode;
            CapturedBackendHeader = proxyResponse.Headers.TryGetValues("X-Backend", out _);
            CapturedBody = await proxyResponse.Content.ReadAsStringAsync(cancellationToken);
            // Returning false (without calling base) tells YARP not to copy the response to the client.
            return false;
        }
    }

    [Fact]
    public async Task Transformer_returning_false_captures_response_and_lets_gateway_own_the_write()
    {
        using var backend = WireMockServer.Start();
        backend
            .Given(Request.Create().WithPath("/catalog/items").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(201)
                .WithHeader("X-Backend", "yes")
                .WithBody("backend-body"));
        var backendPrefix = backend.Url!;

        var transformer = new CapturingTransformer();
        var responseStartedBeforeWrite = true;

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddGateway();
        await using var app = builder.Build();
        app.Run(async ctx =>
        {
            var forwarder = app.Services.GetRequiredService<IHttpForwarder>();
            var invoker = app.Services.GetRequiredService<HttpMessageInvoker>();
            await forwarder.SendAsync(ctx, backendPrefix, invoker, ForwarderRequestConfig.Empty, transformer);

            // The whole point: outbound policies can still run because nothing was flushed yet.
            responseStartedBeforeWrite = ctx.Response.HasStarted;
            ctx.Response.StatusCode = 200;
            ctx.Response.Headers["X-Gateway"] = "owned";
            await ctx.Response.WriteAsync(transformer.CapturedBody!.ToUpperInvariant());
        });
        await app.StartAsync();

        var response = await app.GetTestClient().GetAsync("/catalog/items");

        // The gateway fully owns the client-facing response.
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.GetValues("X-Gateway").ShouldContain("owned");
        (await response.Content.ReadAsStringAsync()).ShouldBe("BACKEND-BODY");
        responseStartedBeforeWrite.ShouldBeFalse();

        // The transformer saw the real backend response.
        transformer.CapturedStatus.ShouldBe(201);
        transformer.CapturedBackendHeader.ShouldBeTrue();
        transformer.CapturedBody.ShouldBe("backend-body");
        backend.LogEntries.Count().ShouldBe(1);

        await app.StopAsync();
    }
}
