using Heimdall.Api;
using Heimdall.Api.Forwarding;
using Heimdall.Application;
using Heimdall.Infrastructure.Context;
using Heimdall.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace Heimdall.Tests.Forwarding;

/// <summary>
/// Confirms <see cref="YarpForwarder"/> forwards mid-pipeline under TestServer and captures the
/// backend response into the policy context (the buffered backend stage), rather than streaming it
/// straight to the client.
/// </summary>
public class ForwarderSpikeTests
{
    [Fact]
    public async Task Forwards_to_a_real_backend_and_captures_the_response_into_the_context()
    {
        using var backend = WireMockServer.Start();
        backend
            .Given(Request.Create().WithPath("/catalog/items").UsingGet())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody("""{"items":["widget"]}"""));

        var backendUri = new Uri(backend.Url!);
        IPolicyContext? captured = null;

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddGateway();
        await using var app = builder.Build();
        app.Run(async ctx =>
        {
            var context = NewContext();
            await app.Services.GetRequiredService<IForwarder>().ForwardAsync(ctx, context, backendUri, ctx.RequestAborted);
            captured = context;
            await ctx.Response.WriteAsync("done");
        });
        await app.StartAsync();

        await app.GetTestClient().GetAsync("/catalog/items");

        captured.ShouldNotBeNull();
        captured!.Response.StatusCode.ShouldBe(200);
        captured.Response.Body!.As<string>().ShouldContain("widget");
        backend.LogEntries.Count().ShouldBe(1);

        await app.StopAsync();
    }

    private static IPolicyContext NewContext() => new PolicyContext
    {
        Request = new EmuRequest
        {
            Method = "GET",
            Url = new Uri("http://localhost/catalog/items"),
            Headers = new Dictionary<string, string[]>(),
            Body = new HttpEmuBody(""),
        },
        Api = new ApiInfo("a", "a", ""),
        Operation = new OperationInfo("o", "GET", "/"),
    };
}
