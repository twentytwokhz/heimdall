using Microsoft.Playwright;
using Shouldly;
using Xunit;
using static Microsoft.Playwright.Assertions;

namespace Heimdall.Tests.E2e.Browser;

/// <summary>
/// Drives the read-only config explorer surfaces in a real browser. Covers the loaded routing model
/// (M1), the resource model - products, subscriptions, named values (M4) - and the flattened
/// effective policy the gateway computes per operation, all read straight from the live config.
/// </summary>
[Collection("console-app-e2e")]
public class ConfigExplorerSpec(ConsoleAppFixture app)
{
    [E2eFact]
    public async Task Overview_shows_loaded_config_counts()
    {
        await using var session = await app.OpenAsync("/");

        await Expect(session.Page.GetByRole(AriaRole.Heading, new() { Name = "Overview" })).ToBeVisibleAsync();
        // The acme samples load exactly one API; the count tile's accessible name is "APIs <count>"
        // (the sidebar nav link is just "APIs", so this targets the tile, not the nav entry).
        await Expect(session.Page.GetByRole(AriaRole.Link, new() { Name = "APIs 1", Exact = true })).ToBeVisibleAsync();

        session.ConsoleErrors.ShouldBeEmpty();
    }

    [E2eFact]
    public async Task Apis_drill_down_shows_the_flattened_effective_policy()
    {
        await using var session = await app.OpenAsync("/apis");
        var page = session.Page;

        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "APIs" })).ToBeVisibleAsync();
        await Expect(page.GetByText("Acme Platform API")).ToBeVisibleAsync();

        // Expand the API row, then select an operation to fetch its effective policy.
        await page.GetByRole(AriaRole.Button, new() { Name = "Acme Platform API" }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "listCatalogItems" }).ClickAsync();

        // The api-scope inbound policy (set-header + rate-limit, from acme.api.xml) flattens into the
        // effective policy the gateway computes - so the panel shows real policy elements, not a stub.
        await Expect(page.GetByText("Inbound").First).ToBeVisibleAsync();
        await Expect(page.GetByText("rate-limit").First).ToBeVisibleAsync();

        session.ConsoleErrors.ShouldBeEmpty();
    }

    [E2eFact]
    public async Task Resource_surfaces_render_their_loaded_rows()
    {
        // Named values: the sample backend-host value is loaded and shown.
        await using (var nv = await app.OpenAsync("/named-values"))
        {
            await Expect(nv.Page.GetByRole(AriaRole.Heading, new() { Name = "Named values" })).ToBeVisibleAsync();
            await Expect(nv.Page.GetByText("backend-host")).ToBeVisibleAsync();
            nv.ConsoleErrors.ShouldBeEmpty();
        }

        // Products + subscriptions render the acme-standard product and its subscription.
        await using (var products = await app.OpenAsync("/products"))
        {
            await Expect(products.Page.GetByText("Acme Standard")).ToBeVisibleAsync();
            products.ConsoleErrors.ShouldBeEmpty();
        }

        await using (var subs = await app.OpenAsync("/subscriptions"))
        {
            await Expect(subs.Page.GetByText("Acme Standard Subscription")).ToBeVisibleAsync();
            subs.ConsoleErrors.ShouldBeEmpty();
        }
    }
}
