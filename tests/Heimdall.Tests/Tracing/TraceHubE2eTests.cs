using System.Text.Json;
using System.Text.Json.Serialization;
using Heimdall.Api.Configuration;
using Heimdall.Application;
using Heimdall.Tests.Fixtures;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace Heimdall.Tests.Tracing;

/// <summary>
/// End-to-end proof of the live feed: a SignalR client connected to /_apim/hub/traces receives each
/// new RequestTrace the gateway records, with the same shape as the polled /_apim/traces endpoint.
/// </summary>
[Collection("gateway-e2e")]
public class TraceHubE2eTests
{
    [Fact]
    public async Task A_connected_client_receives_a_pushed_trace()
    {
        using var backend = WireMockServer.Start();
        backend
            .Given(Request.Create().WithPath("/catalog/items").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("ok"));

        await using var factory = new TestAppFactory();
        var holder = factory.Services.GetRequiredService<GatewayConfigHolder>();
        var api = holder.Current.Apis[0] with { ServiceUrl = new Uri(backend.Url!), SubscriptionRequired = false };
        holder.Current = holder.Current with { Apis = [api] };

        var received = new TaskCompletionSource<RequestTrace>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var connection = new HubConnectionBuilder()
            .WithUrl("http://localhost/_apim/hub/traces", o =>
            {
                o.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                o.Transports = HttpTransportType.LongPolling; // the transport the in-memory test server supports
            })
            .AddJsonProtocol(o =>
            {
                o.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter());
            })
            .Build();
        connection.On<RequestTrace>("trace", t => received.TrySetResult(t));
        await connection.StartAsync();

        await factory.CreateClient().GetAsync("/catalog/items");

        var trace = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));
        trace.Path.ShouldBe("/catalog/items");
        trace.Outcome.ShouldBe(TraceOutcome.Completed);
    }
}
