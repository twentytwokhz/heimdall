using Microsoft.Playwright;
using Shouldly;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace Heimdall.Tests.E2e.Browser;

/// <summary>
/// The collection-wide variables panel: a token the importer could not resolve (an
/// <c>{{access_token}}</c> a Postman script would set at runtime) is left as a literal and surfaced
/// for the user to fill once. Filling it substitutes into the request on replay; a blank one stays
/// literal. Asserted on the outgoing replay request (intercepted), so it holds regardless of the
/// gateway's response.
/// </summary>
[Collection("console-app-e2e")]
public class PlaygroundVariablesSpec(ConsoleAppFixture app)
{
    [E2eFact]
    public async Task A_filled_variable_substitutes_into_the_replayed_request()
    {
        await using var session = await app.OpenAsync("/playground");
        var page = session.Page;

        var path = Path.Combine(Directory.GetParent(Fixtures.RepoPaths.SamplesDir())!.FullName,
            "samples", "collections", "acme-token.http");
        await page.GetByLabel("Collection file").SetInputFilesAsync(path);
        await page.GetByRole(AriaRole.Button, new() { Name = "Import", Exact = true }).ClickAsync();

        // The importer leaves {{access_token}} unresolved; the panel lists it for the user to fill.
        var tokenInput = page.GetByLabel("Value for access_token");
        await Expect(tokenInput).ToBeVisibleAsync();
        await tokenInput.FillAsync("E2E-TOKEN-123");

        // Capture the replay request body to confirm the token reached it before it was sent.
        string? sentBody = null;
        await page.RouteAsync("**/_apim/playground", async route =>
        {
            sentBody ??= route.Request.PostData;
            await route.ContinueAsync();
        });

        await page.GetByRole(AriaRole.Button, new() { Name = "Replay", Exact = true }).ClickAsync();
        await Expect(page.GetByText("Response").First).ToBeVisibleAsync();

        // The filled value substituted into the Authorization header (the imported request carried
        // "Bearer {{access_token}}"). The unresolved-token note still echoes the literal placeholder for
        // reference, so assert on the header value, not the whole payload.
        sentBody.ShouldNotBeNull();
        sentBody.ShouldContain("Bearer E2E-TOKEN-123");

        session.ConsoleErrors.ShouldBeEmpty();
    }
}
