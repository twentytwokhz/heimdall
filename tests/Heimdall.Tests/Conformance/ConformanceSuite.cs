using Heimdall.Api.Configuration;
using Heimdall.Tests.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace Heimdall.Tests.Conformance;

/// <summary>
/// Table-driven conformance suite: every Cases/*.xml row runs through the real effective-policy build
/// and four-stage pipeline against an in-process stub backend, asserting parity with documented APIM
/// semantics. This is the regression net for fidelity across later phases.
/// </summary>
[Collection("gateway-e2e")]
public class ConformanceSuite
{
    public static IEnumerable<object[]> Cases() =>
        Directory.EnumerateFiles(RepoPaths.ConformanceCasesDir(), "*.xml")
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => new object[] { Path.GetFileNameWithoutExtension(f), f });

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Conforms(string id, string path)
    {
        _ = id; // shown in the test name for triage
        var test = ConformanceCase.Load(path);

        using var backend = test.Backend is null ? null : WireMockServer.Start();
        backend?
            .Given(Request.Create().WithPath(test.Request.Path))
            .RespondWith(Response.Create().WithStatusCode(test.Backend!.Status).WithBody(test.Backend.Body ?? string.Empty));

        await using var factory = new TestAppFactory();
        var client = factory.CreateClient();

        var holder = factory.Services.GetRequiredService<GatewayConfigHolder>();
        var serviceUrl = backend is not null ? new Uri(backend.Url!) : new Uri("http://127.0.0.1:1");
        var api = holder.Current.Apis[0] with { ServiceUrl = serviceUrl, SubscriptionRequired = false };
        holder.Current = holder.Current with { Apis = [api], GlobalPolicy = test.Policy, NamedValues = test.NamedValues };

        // A fresh request per send (HttpRequestMessage is single-use); rate-limit/quota rows set repeat>1
        // and we assert on the final response.
        HttpResponseMessage response = null!;
        for (var i = 0; i < Math.Max(1, test.Request.Repeat); i++)
        {
            var request = new HttpRequestMessage(new HttpMethod(test.Request.Method), test.Request.Path);
            if (test.Request.Body is not null)
            {
                request.Content = new StringContent(test.Request.Body);
            }
            foreach (var (name, value) in test.Request.Headers)
            {
                request.Headers.TryAddWithoutValidation(name, value);
            }

            response = await client.SendAsync(request);
        }

        var body = await response.Content.ReadAsStringAsync();

        if (test.Expect.Status is int status)
        {
            ((int)response.StatusCode).ShouldBe(status, $"[{id}] status; body was: {body}");
        }

        foreach (var fragment in test.Expect.BodyContains)
        {
            body.ShouldContain(fragment);
        }

        foreach (var header in test.Expect.Headers)
        {
            var values = ResponseHeaderValues(response, header.Name);
            values.ShouldNotBeEmpty($"[{id}] expected response header '{header.Name}'");
            if (header.Contains is not null)
            {
                string.Join(",", values).ShouldContain(header.Contains);
            }
        }

        foreach (var header in test.Expect.BackendHeaders)
        {
            // Last(): rate-limit/quota rows may forward more than once before short-circuiting.
            var received = backend!.LogEntries.Last().RequestMessage!.Headers!;
            received.ShouldContainKey(header.Name);
            if (header.Contains is not null)
            {
                string.Join(",", received[header.Name]).ShouldContain(header.Contains);
            }
        }

        foreach (var fragment in test.Expect.BackendBodyContains)
        {
            var received = backend!.LogEntries.Last().RequestMessage!;
            (received.Body ?? string.Empty).ShouldContain(fragment);
        }
    }

    private static IReadOnlyList<string> ResponseHeaderValues(HttpResponseMessage response, string name)
    {
        if (response.Headers.TryGetValues(name, out var values))
        {
            return values.ToArray();
        }

        return response.Content.Headers.TryGetValues(name, out var contentValues) ? contentValues.ToArray() : [];
    }
}
