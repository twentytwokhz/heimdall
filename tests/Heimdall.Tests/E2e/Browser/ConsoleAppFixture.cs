using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Heimdall.Tests.Fixtures;
using Microsoft.Playwright;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace Heimdall.Tests.E2e.Browser;

/// <summary>
/// The harness for the browser-driven console E2E suite. Unlike the HTTP-level e2e tests (which use an
/// in-memory <c>TestServer</c> a real browser cannot connect to), this boots the Api as a real
/// out-of-process Kestrel on a free loopback port, serving the built console SPA, and launches a real
/// Chromium against it. A WireMock server stands in for the acme backend - wired via the host's own
/// <c>Heimdall:BackendOverrides</c> hook - so a playground/authoring replay gets a deterministic 200
/// without depending on any external service. Shared as a collection fixture so the single host + one
/// browser are reused across specs.
/// </summary>
public sealed class ConsoleAppFixture : IAsyncLifetime
{
    private readonly StringBuilder _hostLog = new();
    private WireMockServer? _backend;
    private Process? _host;
    private IPlaywright? _playwright;

    public string BaseUrl { get; private set; } = "";
    public IBrowser Browser { get; private set; } = null!;

    /// <summary>The subscription key the acme samples ship with (Product scope, Active).</summary>
    public const string SubscriptionKey = "acme-standard-primary-key";

    public async Task InitializeAsync()
    {
        var repoRoot = Directory.GetParent(RepoPaths.SamplesDir())!.FullName;
        EnsureConsoleBuilt(repoRoot);

        // A stub backend for the acme API. The samples forward acme to http://localhost:8081 (the
        // OpenAPI server url); point it at WireMock instead so replays succeed offline. A wildcard GET
        // returns 200 and POST returns 201, which is all the playground/authoring journeys need.
        _backend = WireMockServer.Start();
        _backend.Given(Request.Create().WithPath("/*").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithHeader("Content-Type", "application/json").WithBody("""[{"sku":"ACME-100","name":"Sprocket"}]"""));
        _backend.Given(Request.Create().WithPath("/*").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(201)
                .WithHeader("Content-Type", "application/json").WithBody("""{"sku":"ACME-100","name":"Sprocket"}"""));

        var port = FreeLoopbackPort();
        BaseUrl = $"http://127.0.0.1:{port}";
        await StartHostAsync(repoRoot, port);

        _playwright = await Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
    }

    /// <summary>
    /// Opens a fresh browser page on the given console route (e.g. "/playground") and records any
    /// <c>console.error</c> it emits, so a spec can assert the surface ran clean. The console lives
    /// under the /_apim basename, so routes are joined onto it.
    /// </summary>
    public async Task<ConsolePage> OpenAsync(string route = "/")
    {
        var context = await Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.Console += (_, msg) =>
        {
            // "Failed to load resource" is the browser noting a 4xx/5xx network response - several specs
            // exercise those deliberately (a 401 without a key, a rejected v2.0 import) and the UI
            // surfaces them itself. Only real JS console errors count as a clean-run violation.
            if (msg.Type == "error" && !msg.Text.StartsWith("Failed to load resource")) errors.Add(msg.Text);
        };
        page.PageError += (_, err) => errors.Add(err);

        var url = $"{BaseUrl}/_apim{(route == "/" ? "/" : route)}";
        await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        return new ConsolePage(context, page, errors);
    }

    private static void EnsureConsoleBuilt(string repoRoot)
    {
        var index = Path.Combine(repoRoot, "src", "Heimdall.Api", "wwwroot", "console", "index.html");
        if (!File.Exists(index))
        {
            throw new InvalidOperationException(
                $"The console SPA is not built ({index} is missing). Run `cd src/Heimdall.Ui && npm run build` " +
                "before the browser E2E suite.");
        }
    }

    private async Task StartHostAsync(string repoRoot, int port)
    {
        // Derive the build configuration + target framework from where this test assembly runs, then
        // run the matching Api build output. The working directory is the Api project dir so the host's
        // ContentRootPath resolves wwwroot/console.
        var baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var tfm = Path.GetFileName(baseDir);                                  // e.g. net10.0
        var config = Path.GetFileName(Path.GetDirectoryName(baseDir)!);        // e.g. Debug
        var apiDir = Path.Combine(repoRoot, "src", "Heimdall.Api");
        var apiDll = Path.Combine(apiDir, "bin", config, tfm, "Heimdall.Api.dll");
        if (!File.Exists(apiDll))
        {
            throw new InvalidOperationException($"Api build output not found at {apiDll}. Build the solution first.");
        }

        var startInfo = new ProcessStartInfo("dotnet", $"\"{apiDll}\"")
        {
            WorkingDirectory = apiDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        // Fail loud if WireMock never bound (Url is null) rather than passing an empty override the host
        // would reject as a bad URL with a confusing downstream error.
        var backendUrl = _backend!.Url
            ?? throw new InvalidOperationException("The WireMock backend did not start (Url is null).");

        startInfo.Environment["ASPNETCORE_URLS"] = BaseUrl;
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        startInfo.Environment["Heimdall__ConfigPath"] = RepoPaths.SamplesDir();
        startInfo.Environment["Heimdall__EnableConsole"] = "true";
        startInfo.Environment["Heimdall__BackendOverrides__acme"] = backendUrl;

        var host = new Process { StartInfo = startInfo };
        host.OutputDataReceived += (_, e) => { if (e.Data != null) lock (_hostLog) _hostLog.AppendLine(e.Data); };
        host.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (_hostLog) _hostLog.AppendLine(e.Data); };
        host.Start();
        host.BeginOutputReadLine();
        host.BeginErrorReadLine();
        _host = host;

        await WaitForReadyAsync(port);
    }

    private async Task WaitForReadyAsync(int port)
    {
        using var client = new HttpClient { BaseAddress = new Uri(BaseUrl), Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(40);
        while (DateTime.UtcNow < deadline)
        {
            if (_host!.HasExited)
            {
                throw new InvalidOperationException($"The console host exited early (code {_host.ExitCode}).\n{HostLog()}");
            }

            try
            {
                var response = await client.GetAsync("/_apim/config");
                if (response.StatusCode == HttpStatusCode.OK) return;
            }
            catch (HttpRequestException) { /* not listening yet */ }
            catch (TaskCanceledException) { /* request timed out, retry */ }

            await Task.Delay(250);
        }

        throw new InvalidOperationException($"The console host did not become ready on {BaseUrl}.\n{HostLog()}");
    }

    private string HostLog()
    {
        lock (_hostLog) return _hostLog.ToString();
    }

    private static int FreeLoopbackPort()
    {
        // TOCTOU: the port is released here and the host binds it a moment later, so another process
        // could claim it in between. Acceptable for this single-host, local/CI usage - the readiness
        // probe surfaces a bind failure loudly. A full fix would read the OS-assigned port back from the
        // child (ASPNETCORE_URLS=...:0), which complicates the probe URL for no real gain here.
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public async Task DisposeAsync()
    {
        if (Browser != null) await Browser.DisposeAsync();
        _playwright?.Dispose();

        if (_host != null)
        {
            try
            {
                if (!_host.HasExited) _host.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) { /* already gone */ }
            _host.Dispose();
        }

        _backend?.Stop();
        _backend?.Dispose();
    }
}

/// <summary>A browser page plus the context that owns it and the console errors it has emitted.</summary>
public sealed class ConsolePage(IBrowserContext context, IPage page, IReadOnlyList<string> errors) : IAsyncDisposable
{
    public IPage Page => page;
    public IReadOnlyList<string> ConsoleErrors => errors;

    public async ValueTask DisposeAsync() => await context.DisposeAsync();
}

[CollectionDefinition("console-app-e2e")]
public sealed class ConsoleAppCollection : ICollectionFixture<ConsoleAppFixture>;
