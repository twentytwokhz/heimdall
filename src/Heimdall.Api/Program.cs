using Heimdall.Api;
using Heimdall.Api.Configuration;
using Heimdall.Api.Console;
using Heimdall.Api.Middleware;
using Heimdall.Application;
using Heimdall.Infrastructure;
using Heimdall.Infrastructure.ApiOpsLoader;
using Heimdall.Infrastructure.XmlOpenApiLoader;
using Microsoft.Extensions.FileProviders;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // A transparent gateway streams bodies it does not read (opt-in buffering), so it should not impose
    // Kestrel's default 30 MB request-body cap: the backend enforces its own limit. null = unbounded.
    builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = null);

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .WriteTo.Console());

    builder.Services.AddInfrastructure(builder.Configuration);

    // Provider-selection-by-config: pick the IConfigLoader by name (fail loud on an unknown value).
    var loaderName = builder.Configuration["Heimdall:ConfigLoader"] ?? "XmlOpenApi";
    switch (loaderName)
    {
        case "XmlOpenApi":
            builder.Services.AddXmlOpenApiLoader(builder.Configuration);
            break;
        case "ApiOps":
            builder.Services.AddApiOpsLoader(builder.Configuration);
            break;
        default:
            throw new InvalidOperationException(
                $"Unknown Heimdall:ConfigLoader '{loaderName}'. Use 'XmlOpenApi' or 'ApiOps'.");
    }

    builder.Services.AddGateway();
    builder.Services.AddHostedService<ConfigLoaderHostedService>();
    builder.Services.AddHostedService<ExpressionWarmupHostedService>();

    // The embedded console, on by default. Set Heimdall:EnableConsole=false to run the gateway headless
    // (no console SPA, admin API, or SignalR hub) for CI and data-plane-only deployments; the gateway,
    // policies, and tracing-to-sink are unaffected. SignalR powers the live trace feed; its JSON protocol
    // matches the REST endpoints so streamed and polled traces agree.
    var consoleEnabled = builder.Configuration.GetValue("Heimdall:EnableConsole", true);
    if (consoleEnabled)
    {
        builder.Services
            .AddSignalR()
            .AddJsonProtocol(o =>
            {
                // Derive from the console's options (not a parallel copy) so parity holds if they change.
                o.PayloadSerializerOptions.PropertyNamingPolicy = ConsoleJson.Options.PropertyNamingPolicy;
                foreach (var converter in ConsoleJson.Options.Converters)
                {
                    o.PayloadSerializerOptions.Converters.Add(converter);
                }
            });
        builder.Services.AddHostedService<TraceBroadcaster>();
    }

    var app = builder.Build();

    app.UseSerilogRequestLogging();

    // Serve the embedded console SPA (built by src/Heimdall.Ui into wwwroot/console) under the /_apim
    // namespace. Guarded on the build output existing, so an API-only run and the browserless test host
    // are unaffected when no frontend has been built. Static assets are served here; SPA client-side
    // routes fall back to index.html via MapFallbackToFile below (scoped to /_apim only).
    var consoleRoot = Path.Combine(app.Environment.ContentRootPath, "wwwroot", "console");
    var serveConsole = consoleEnabled && Directory.Exists(consoleRoot);
    PhysicalFileProvider? consoleFiles = null;
    if (serveConsole)
    {
        consoleFiles = new PhysicalFileProvider(consoleRoot);
        app.Lifetime.ApplicationStopped.Register(() => consoleFiles!.Dispose());
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = consoleFiles,
            RequestPath = "/_apim",
        });
    }

    // Liveness probe (a reserved route, matched before the gateway fallback).
    app.MapGet("/health", () => Results.Json(new { status = "healthy" }));

    // Opt-in admin API (off by default): /admin/* would otherwise shadow a real API path, so it is
    // only mapped when explicitly enabled. Reserved routes, matched before the gateway fallback.
    if (builder.Configuration.GetValue("Heimdall:EnableAdminApi", false))
    {
        app.MapGet("/admin/status", (GatewayConfigHolder holder, IConfiguration cfg) =>
            Results.Json(Status(cfg, holder.Current)));

        app.MapPost("/admin/reload", async (
            IConfigLoader loader, GatewayConfigHolder holder, IConfiguration cfg,
            ILoggerFactory loggerFactory, CancellationToken ct) =>
        {
            // Concurrent reloads are benign for a local emulator: each is a single volatile reference
            // assignment of a freshly-built immutable GatewayConfig, so no locking is needed.
            var reloaded = await loader.LoadAsync(cfg["Heimdall:ConfigPath"] ?? "samples", ct);
            var overrides = BackendOverrides.Read(cfg);
            holder.Current = BackendOverrides.Apply(reloaded, overrides);
            var logger = loggerFactory.CreateLogger("Heimdall.Reload");
            foreach (var (apiId, url) in overrides)
            {
                logger.LogInformation("Backend override applied: API '{ApiId}' forwards to {Url}", apiId, url);
            }
            return Results.Json(Status(cfg, holder.Current));
        });
    }

    // Embedded console admin API + live trace hub. Reserved routes under /_apim, mapped before the
    // gateway fallback (so SignalR's negotiate/connect endpoints win over the /_apim seam guard).
    if (consoleEnabled)
    {
        app.MapConsoleApi();
        app.MapHub<TraceHub>("/_apim/hub/traces");

        // SPA client-side routes (e.g. /_apim/apis) fall back to the app shell, but only for browser
        // navigations (Accept: text/html). Non-HTML calls to an unmatched /_apim path keep clean 404
        // semantics instead of receiving the shell HTML, preserving the reserved-namespace guard. This
        // catch-all is scoped to /_apim, so the more specific console API routes win and the global
        // gateway fallback still owns every other path.
        if (serveConsole)
        {
            // {**path:nonfile} excludes asset paths (anything with an extension) so static files are
            // served by UseStaticFiles instead of being swallowed by this catch-all. Without :nonfile,
            // routing matches this endpoint first and the static file middleware skips serving.
            app.MapFallback("/_apim/{**path:nonfile}", async context =>
            {
                var acceptsHtml = context.Request.Headers.Accept
                    .ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase);
                if (HttpMethods.IsGet(context.Request.Method) && acceptsHtml)
                {
                    context.Response.ContentType = "text/html";
                    await context.Response.SendFileAsync(consoleFiles!.GetFileInfo("index.html"));
                    return;
                }
                context.Response.StatusCode = StatusCodes.Status404NotFound;
            });
        }
    }

    // Everything else is gateway traffic: route, flatten, forward.
    // Config is loaded at host start by ConfigLoaderHostedService (keeps the entry point synchronous).
    app.MapFallback((HttpContext context, GatewayMiddleware gateway) => gateway.InvokeAsync(context));

    app.Run();

    // Shared shape for /admin/status and /admin/reload: the active loader + the loaded resource counts.
    static object Status(IConfiguration cfg, Heimdall.Domain.GatewayConfig c) => new
    {
        loader = cfg["Heimdall:ConfigLoader"] ?? "XmlOpenApi",
        configPath = cfg["Heimdall:ConfigPath"] ?? "samples",
        apis = c.Apis.Count,
        products = c.Products.Count,
        subscriptions = c.Subscriptions.Count,
        namedValues = c.NamedValues.Count,
        backends = c.Backends.Count,
        fragments = c.Fragments?.Count ?? 0,
    };
}
catch (Exception ex)
{
    Log.Fatal(ex, "Heimdall terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

// Exposed so WebApplicationFactory<Program> can host the app in tests.
public partial class Program { }
