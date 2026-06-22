using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Shouldly;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace Heimdall.Tests.E2e.Browser;

/// <summary>
/// Drives the live trace canvas: fire a request through the gateway and confirm it streams onto the
/// Frontend/Inbound/Backend/Outbound canvas over SignalR, with the client-computed metrics strip. The
/// trace is produced by the real pipeline (M2/M3); the canvas + metrics are the M7 console wiring.
/// </summary>
[Collection("console-app-e2e")]
public class LiveTraceSpec(ConsoleAppFixture app)
{
    [E2eFact]
    public async Task A_fired_request_streams_onto_the_canvas()
    {
        await using var session = await app.OpenAsync("/live");
        var page = session.Page;

        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Live traffic" })).ToBeVisibleAsync();
        // The metrics strip is always present (computed client-side from the trace buffer).
        await Expect(page.GetByText("Requests · last 60s")).ToBeVisibleAsync();

        // Fire one keyed request straight at the gateway; the host traces it and pushes over SignalR to
        // the open page, where the feed auto-follows the newest trace onto the canvas.
        var response = await page.APIRequest.GetAsync($"{app.BaseUrl}/catalog/items", new()
        {
            Headers = new Dictionary<string, string>
            {
                ["Ocp-Apim-Subscription-Key"] = ConsoleAppFixture.SubscriptionKey,
            },
        });
        response.Status.ShouldBe(200);

        // The feed row carries the request in its accessible name; the canvas backend stage lights up.
        await Expect(page.GetByLabel(new Regex("HTTP 200 GET /catalog/items")).First).ToBeVisibleAsync();
        await Expect(page.GetByText("Backend").First).ToBeVisibleAsync();

        session.ConsoleErrors.ShouldBeEmpty();
    }
}
