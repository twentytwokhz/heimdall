using System.Net;
using System.Text.Json;
using Heimdall.Tests.Fixtures;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Shouldly;
using Xunit;

namespace Heimdall.Tests.E2e;

/// <summary>
/// End-to-end proof that the host can be pointed at the APIOps loader by config, and that the opt-in
/// admin API reports and reloads the active config (and stays off by default).
/// </summary>
[Collection("gateway-e2e")]
public class AdminApiE2eTests
{
    // Layer the APIOps loader + the apiops fixture + the admin API on top of the default test host.
    private static WebApplicationFactory<Program> ApiOpsWithAdmin() =>
        new TestAppFactory().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Heimdall:ConfigLoader", "ApiOps");
            builder.UseSetting("Heimdall:ConfigPath", RepoPaths.ApiOpsLayoutDir());
            builder.UseSetting("Heimdall:EnableAdminApi", "true");
        });

    [Fact]
    public async Task Admin_status_reports_the_apiops_loaded_config()
    {
        await using var factory = ApiOpsWithAdmin();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/admin/status");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("loader").GetString().ShouldBe("ApiOps");
        doc.RootElement.GetProperty("apis").GetInt32().ShouldBe(1);
        doc.RootElement.GetProperty("subscriptions").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task Admin_reload_reloads_and_reports_counts()
    {
        await using var factory = ApiOpsWithAdmin();
        var client = factory.CreateClient();

        var response = await client.PostAsync("/admin/reload", content: null);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("apis").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task Admin_api_is_off_by_default()
    {
        await using var factory = new TestAppFactory();   // EnableAdminApi defaults to false
        var client = factory.CreateClient();

        // Admin disabled: /admin/status is not a reserved route, so it falls through to the gateway,
        // which matches no API operation for that path and returns 404 (not the admin payload).
        var response = await client.GetAsync("/admin/status");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
