using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Shouldly;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace Heimdall.Tests.E2e.Browser;

/// <summary>
/// Drives the request playground through a real browser: import a Postman v2.1 collection, replay a
/// request against the live gateway (forwarded to the stub backend for a deterministic 200), follow
/// the correlated trace into Live, and confirm the subscription-key gate (401) when the key is removed.
/// Covers the forward path (M1), subscription-key auth (M4), and the playground/trace wiring (M7).
///
/// Rate-limit note: the acme API caps at 5 calls/60s per subscription (shared across this host). The
/// suite keeps its keyed 200s well under that, so enforcement is verified by the data-plane tests, not
/// here; the policy's presence is verified through the config explorer (see ConfigExplorerSpec).
/// </summary>
[Collection("console-app-e2e")]
public class PlaygroundReplaySpec(ConsoleAppFixture app)
{
    [E2eFact]
    public async Task Import_replay_and_follow_the_trace_into_live()
    {
        await using var session = await app.OpenAsync("/playground");
        var page = session.Page;

        await ImportCollectionAsync(page, "acme-catalog.postman_collection.json");

        // Pick the GET and replay it through the gateway; the stub backend answers 200.
        await page.GetByRole(AriaRole.Button, new() { Name = "List catalog items" }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Replay", Exact = true }).ClickAsync();

        await Expect(page.GetByText("200").First).ToBeVisibleAsync();
        await Expect(page.GetByText("Sprocket")).ToBeVisibleAsync();   // the stub backend's body

        // Follow the correlated trace onto the live canvas.
        await page.GetByRole(AriaRole.Button, new() { Name = "View in Live" }).ClickAsync();
        await Expect(page).ToHaveURLAsync(new Regex(@"/_apim/live$"));
        await Expect(page.GetByText("Backend").First).ToBeVisibleAsync();

        session.ConsoleErrors.ShouldBeEmpty();
    }

    [E2eFact]
    public async Task Replay_without_a_subscription_key_is_rejected_401()
    {
        await using var session = await app.OpenAsync("/playground");
        var page = session.Page;

        await ImportCollectionAsync(page, "acme-catalog.postman_collection.json");
        await page.GetByRole(AriaRole.Button, new() { Name = "List catalog items" }).ClickAsync();

        // Strip the subscription-key header, then replay: the gateway rejects before the pipeline.
        // (A 401 is rejected pre-pipeline, so it consumes no rate-limit budget.)
        await page.GetByLabel("Remove header Ocp-Apim-Subscription-Key").ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Replay", Exact = true }).ClickAsync();

        await Expect(page.GetByText("401").First).ToBeVisibleAsync();

        session.ConsoleErrors.ShouldBeEmpty();
    }

    // Drive the explicitly-labelled collection picker, then Import. The picker accepts .json and .http.
    private static async Task ImportCollectionAsync(IPage page, string fileName)
    {
        var path = Path.Combine(Directory.GetParent(Fixtures.RepoPaths.SamplesDir())!.FullName,
            "samples", "collections", fileName);
        await page.GetByLabel("Collection file").SetInputFilesAsync(path);
        await page.GetByRole(AriaRole.Button, new() { Name = "Import", Exact = true }).ClickAsync();
    }
}
