using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Shouldly;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace Heimdall.Tests.E2e.Browser;

/// <summary>
/// The playground import boundary, driven through the browser: a <c>.http</c> file imports into a
/// replayable request list; a script-bearing Postman collection imports with the scripts flagged as
/// not executed; and a non-v2.1 (v2.0) collection is rejected loudly with a version message. This is
/// the fail-loud import contract (no silent "support"). No replay - so no gateway/rate-limit budget.
/// </summary>
[Collection("console-app-e2e")]
public class PlaygroundImportFormatsSpec(ConsoleAppFixture app)
{
    [E2eFact]
    public async Task Http_file_imports_into_a_replayable_request_list()
    {
        await using var session = await app.OpenAsync("/playground");
        await ImportAsync(session.Page, "acme-catalog.http");

        await Expect(session.Page.GetByRole(AriaRole.Button, new() { Name = "List catalog items" })).ToBeVisibleAsync();
        await Expect(session.Page.GetByRole(AriaRole.Button, new() { Name = "Create catalog item" })).ToBeVisibleAsync();

        session.ConsoleErrors.ShouldBeEmpty();
    }

    [E2eFact]
    public async Task Scripted_collection_imports_with_scripts_flagged_not_executed()
    {
        await using var session = await app.OpenAsync("/playground");
        await ImportAsync(session.Page, "acme-scripts.postman_collection.json");

        // The importer parses but never runs scripts; their presence is surfaced, never hidden.
        await Expect(session.Page.GetByText(new Regex("Scripts present.*were not executed"))).ToBeVisibleAsync();

        session.ConsoleErrors.ShouldBeEmpty();
    }

    [E2eFact]
    public async Task Non_v21_collection_is_rejected_with_a_version_message()
    {
        await using var session = await app.OpenAsync("/playground");
        await ImportAsync(session.Page, "acme-v2.0.postman_collection.json");

        await Expect(session.Page.GetByText(new Regex("Only v2.1 is supported"))).ToBeVisibleAsync();

        session.ConsoleErrors.ShouldBeEmpty();
    }

    private static async Task ImportAsync(IPage page, string fileName)
    {
        var path = Path.Combine(Directory.GetParent(Fixtures.RepoPaths.SamplesDir())!.FullName,
            "samples", "collections", fileName);
        await page.GetByLabel("Collection file").SetInputFilesAsync(path);
        await page.GetByRole(AriaRole.Button, new() { Name = "Import", Exact = true }).ClickAsync();
    }
}
