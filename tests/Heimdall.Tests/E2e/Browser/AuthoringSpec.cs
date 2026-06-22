using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Shouldly;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace Heimdall.Tests.E2e.Browser;

/// <summary>
/// In-browser policy authoring (M7): pick a scope, edit its policy XML, and save to validate and
/// hot-reload it into the live gateway in memory. A supported policy saves and reloads; an unsupported
/// one is rejected loudly without swapping (fail-loud, via the same IPolicyRegistry the engine uses).
/// A built-in replay then fires a request and links its trace into Live.
/// </summary>
[Collection("console-app-e2e")]
public class AuthoringSpec(ConsoleAppFixture app)
{
    private const string GoodPolicy = """
        <policies>
          <inbound>
            <base />
            <set-header name="X-E2E" exists-action="override">
              <value>1</value>
            </set-header>
          </inbound>
          <backend><base /></backend>
          <outbound><base /></outbound>
          <on-error><base /></on-error>
        </policies>
        """;

    private const string UnsupportedPolicy = """
        <policies>
          <inbound><frobnicate /></inbound>
          <backend><base /></backend>
          <outbound><base /></outbound>
          <on-error><base /></on-error>
        </policies>
        """;

    [E2eFact]
    public async Task Edit_save_hot_reload_then_reject_an_unsupported_policy()
    {
        await using var session = await app.OpenAsync("/authoring");
        var page = session.Page;

        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Policy authoring" })).ToBeVisibleAsync();

        // Edit the operation scope (exercises the API + operation pickers).
        await page.GetByLabel("Policy scope").SelectOptionAsync(new SelectOptionValue { Value = "operation" });

        var save = page.GetByRole(AriaRole.Button, new() { Name = "Save & hot-reload" });
        var editor = page.GetByLabel("Policy XML");

        // Wait for the scope's source load to finish (Save is disabled while loading) so the loaded XML
        // does not clobber the value we type next.
        await Expect(save).ToBeEnabledAsync();
        await editor.FillAsync(GoodPolicy);
        await save.ClickAsync();
        await Expect(page.GetByText("Saved and hot-reloaded.")).ToBeVisibleAsync();

        // An unsupported policy is rejected loudly; the success note clears and no swap happens.
        await editor.FillAsync(UnsupportedPolicy);
        await save.ClickAsync();
        await Expect(page.GetByText(new Regex("Unsupported policy"))).ToBeVisibleAsync();
        await Expect(page.GetByText("Saved and hot-reloaded.")).ToBeHiddenAsync();

        session.ConsoleErrors.ShouldBeEmpty();
    }

    [E2eFact]
    public async Task Replay_from_authoring_produces_a_response_and_links_into_live()
    {
        await using var session = await app.OpenAsync("/authoring");
        var page = session.Page;

        // The integrated replay reuses the playground replay path; here we only need a response back,
        // so the assertion is status-agnostic (it stays within the suite's rate-limit budget).
        await page.GetByRole(AriaRole.Button, new() { Name = "Replay", Exact = true }).ClickAsync();
        await Expect(page.GetByLabel("Response body")).ToBeVisibleAsync();

        await page.GetByRole(AriaRole.Button, new() { Name = "View in Live" }).ClickAsync();
        await Expect(page).ToHaveURLAsync(new Regex(@"/_apim/live$"));

        session.ConsoleErrors.ShouldBeEmpty();
    }
}
