using Heimdall.Api.Configuration;
using Heimdall.Api.Playground;
using Heimdall.Application;
using Heimdall.Domain;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;

namespace Heimdall.Api.Console;

/// <summary>
/// The read-only console admin API under <c>/_apim/*</c>: the data the embedded SPA renders. Mapped
/// before the gateway fallback so these reserved routes win over proxied traffic; the gateway also
/// guards the <c>/_apim</c> prefix so unmatched admin paths are never forwarded.
/// </summary>
public static class ConsoleEndpoints
{
    // Shared with the SignalR hub protocol so streamed and polled traces are byte-identical.
    private static readonly System.Text.Json.JsonSerializerOptions Json = ConsoleJson.Options;

    public static void MapConsoleApi(this WebApplication app)
    {
        app.MapGet("/_apim/config", (GatewayConfigHolder holder) =>
            Results.Json(ConfigView.From(holder.Current), Json));

        // The flattened effective policy for one operation (global + api + op scopes; product scope
        // is not applied here). 404 when the api or operation id is unknown.
        app.MapGet("/_apim/policies/{apiId}/{operationId}",
            (string apiId, string operationId, GatewayConfigHolder holder, EffectivePolicyBuilder builder) =>
            {
                var config = holder.Current;
                var api = config.Apis.FirstOrDefault(a => a.Id == apiId);
                var operation = api?.Operations.FirstOrDefault(o => o.Id == operationId);
                if (api is null || operation is null)
                {
                    return Results.NotFound();
                }

                var effective = builder.Build([config.GlobalPolicy, api.Policy, operation.Policy], config.Fragments);
                return Results.Json(effective, Json);
            });

        // The recent-traces feed (newest first); the SPA polls this until 7b-2 adds the live SignalR push.
        app.MapGet("/_apim/traces", (ITraceSink sink, int? limit) =>
            Results.Json(sink.Recent(limit ?? 100), Json));

        // One trace by id. The {id:guid} constraint means a non-Guid path never matches here: it falls to
        // the gateway, whose /_apim guard returns 404 (so a malformed id reads as not-found, not a 400).
        app.MapGet("/_apim/traces/{id:guid}", (Guid id, ITraceSink sink) =>
            sink.Get(id) is { } trace ? Results.Json(trace, Json) : Results.NotFound());

        // Import a Postman v2.1 export or a .http file (multipart) into replayable playground requests.
        // The right importer is chosen by CanImport; an unsupported format or schema version fails loud (400).
        app.MapPost("/_apim/playground/import", async (
            HttpRequest request, IServer server, IEnumerable<ICollectionImporter> importers, CancellationToken ct) =>
        {
            if (!request.HasFormContentType)
            {
                return Results.BadRequest(new { error = "Upload the collection as multipart/form-data." });
            }

            var form = await request.ReadFormAsync(ct);
            var file = form.Files.GetFile("collection") ?? form.Files.FirstOrDefault();
            if (file is null)
            {
                return Results.BadRequest(new { error = "No collection file was uploaded." });
            }

            var content = await ReadFileAsync(file, ct);
            var environment = form.Files.GetFile("environment");
            var environmentContent = environment is null ? null : await ReadFileAsync(environment, ct);

            var importer = importers.FirstOrDefault(i => i.CanImport(file.FileName, content));
            if (importer is null)
            {
                return Results.BadRequest(new
                {
                    error = $"Unsupported collection format: '{file.FileName}'. Upload a Postman v2.1 export or a .http file.",
                });
            }

            var gatewayOrigin = LoopbackOrigin.Resolve(server);
            try
            {
                return Results.Json(importer.Import(file.FileName, content, environmentContent, gatewayOrigin), Json);
            }
            catch (NotSupportedException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        }).DisableAntiforgery();

        // Replay one request through the gateway (loopback). The recorded trace is correlated by the
        // returned RequestId (GET /_apim/traces/{id} or the live feed).
        app.MapPost("/_apim/playground", async (
            PlaygroundRequest playgroundRequest, IGatewayReplayClient replay, IServer server, CancellationToken ct) =>
        {
            // Loop back to this process's own address, never the caller-controlled Host header (SSRF).
            var gatewayOrigin = LoopbackOrigin.Resolve(server);
            try
            {
                return Results.Json(await replay.ReplayAsync(playgroundRequest, gatewayOrigin, ct), Json);
            }
            catch (InvalidOperationException ex)
            {
                // Fail loud on a bad upload (invalid base64, oversized file) as a 400 the UI can show,
                // not a 500. The replay client raises these before any traffic leaves the process.
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        // The current source XML for one policy scope, serialized from the live config (the config keeps
        // parsed documents, not raw text). 400 for a bad/incomplete scope, 404 for an unknown id.
        app.MapGet("/_apim/authoring/policy",
            (string? scope, string? apiId, string? operationId, string? productId, GatewayConfigHolder holder) =>
            {
                var (outcome, policy) = ReadScope(holder.Current, scope, apiId, operationId, productId);
                return outcome switch
                {
                    ScopeOutcome.Ok => Results.Json(
                        new { scope, apiId, operationId, productId, xml = PolicyXmlWriter.Write(policy) }, Json),
                    ScopeOutcome.NotFound => Results.NotFound(),
                    _ => Results.BadRequest(new { error = $"Unknown or incomplete policy scope: '{scope}'." }),
                };
            });

        // Save a policy: validate then hot-swap it into the live config in-memory (no disk write; a
        // restart or /admin/reload resets to the on-disk config). Fails loud on malformed XML or an
        // unsupported policy without swapping. Expression errors are not checked here - they surface on
        // the request trace. The single volatile reference assignment needs no lock (as /admin/reload).
        app.MapPost("/_apim/authoring/policy",
            (AuthoringPolicyRequest request, GatewayConfigHolder holder, IPolicyRegistry registry) =>
            {
                if (string.IsNullOrWhiteSpace(request.Xml))
                {
                    return Results.BadRequest(new { error = "Policy XML is required." });
                }

                PolicyDocument parsed;
                try
                {
                    parsed = PolicyXmlParser.Parse(request.Xml);
                }
                catch (System.Xml.XmlException ex)
                {
                    return Results.BadRequest(new { error = "The policy XML is not well-formed.", detail = ex.Message });
                }

                try
                {
                    PolicyValidation.Validate(parsed, registry);
                }
                catch (UnsupportedPolicyException ex)
                {
                    return Results.BadRequest(new
                    {
                        error = $"Unsupported policy '{ex.ElementName}'. Remove it or use a supported policy.",
                        detail = ex.Message,
                    });
                }

                var (outcome, updated) = WriteScope(
                    holder.Current, request.Scope, request.ApiId, request.OperationId, request.ProductId, parsed);
                switch (outcome)
                {
                    case ScopeOutcome.Ok:
                        holder.Current = updated!;
                        return Results.Json(new { ok = true }, Json);
                    case ScopeOutcome.NotFound:
                        return Results.NotFound();
                    default:
                        return Results.BadRequest(new { error = $"Unknown or incomplete policy scope: '{request.Scope}'." });
                }
            }).DisableAntiforgery();
    }

    private enum ScopeOutcome { Ok, BadScope, NotFound }

    // Locate a scope's current policy for reading. BadScope = unknown scope name or a missing required id.
    private static (ScopeOutcome Outcome, PolicyDocument? Policy) ReadScope(
        GatewayConfig config, string? scope, string? apiId, string? operationId, string? productId)
    {
        switch (scope)
        {
            case "global":
                return (ScopeOutcome.Ok, config.GlobalPolicy);
            case "api":
                if (string.IsNullOrEmpty(apiId))
                {
                    return (ScopeOutcome.BadScope, null);
                }

                var api = config.Apis.FirstOrDefault(a => a.Id == apiId);
                return api is null ? (ScopeOutcome.NotFound, null) : (ScopeOutcome.Ok, api.Policy);
            case "operation":
                if (string.IsNullOrEmpty(apiId) || string.IsNullOrEmpty(operationId))
                {
                    return (ScopeOutcome.BadScope, null);
                }

                var op = config.Apis.FirstOrDefault(a => a.Id == apiId)?
                    .Operations.FirstOrDefault(o => o.Id == operationId);
                return op is null ? (ScopeOutcome.NotFound, null) : (ScopeOutcome.Ok, op.Policy);
            case "product":
                if (string.IsNullOrEmpty(productId))
                {
                    return (ScopeOutcome.BadScope, null);
                }

                var product = config.Products.FirstOrDefault(p => p.Id == productId);
                return product is null ? (ScopeOutcome.NotFound, null) : (ScopeOutcome.Ok, product.Policy);
            default:
                return (ScopeOutcome.BadScope, null);
        }
    }

    // Rebuild the config with one scope's policy replaced (records are immutable, so the containing list
    // is rebuilt too). Returns the new config; the caller swaps it into the holder.
    private static (ScopeOutcome Outcome, GatewayConfig? Config) WriteScope(
        GatewayConfig config, string? scope, string? apiId, string? operationId, string? productId, PolicyDocument policy)
    {
        switch (scope)
        {
            case "global":
                return (ScopeOutcome.Ok, config with { GlobalPolicy = policy });
            case "api":
            {
                if (string.IsNullOrEmpty(apiId))
                {
                    return (ScopeOutcome.BadScope, null);
                }

                if (config.Apis.All(a => a.Id != apiId))
                {
                    return (ScopeOutcome.NotFound, null);
                }

                var apis = config.Apis.Select(a => a.Id == apiId ? a with { Policy = policy } : a).ToList();
                return (ScopeOutcome.Ok, config with { Apis = apis });
            }

            case "operation":
            {
                if (string.IsNullOrEmpty(apiId) || string.IsNullOrEmpty(operationId))
                {
                    return (ScopeOutcome.BadScope, null);
                }

                var api = config.Apis.FirstOrDefault(a => a.Id == apiId);
                if (api is null || api.Operations.All(o => o.Id != operationId))
                {
                    return (ScopeOutcome.NotFound, null);
                }

                var ops = api.Operations.Select(o => o.Id == operationId ? o with { Policy = policy } : o).ToList();
                var apis = config.Apis.Select(a => a.Id == apiId ? a with { Operations = ops } : a).ToList();
                return (ScopeOutcome.Ok, config with { Apis = apis });
            }

            case "product":
            {
                if (string.IsNullOrEmpty(productId))
                {
                    return (ScopeOutcome.BadScope, null);
                }

                if (config.Products.All(p => p.Id != productId))
                {
                    return (ScopeOutcome.NotFound, null);
                }

                var products = config.Products.Select(p => p.Id == productId ? p with { Policy = policy } : p).ToList();
                return (ScopeOutcome.Ok, config with { Products = products });
            }

            default:
                return (ScopeOutcome.BadScope, null);
        }
    }

    private static async Task<string> ReadFileAsync(IFormFile file, CancellationToken ct)
    {
        using var reader = new StreamReader(file.OpenReadStream());
        return await reader.ReadToEndAsync(ct);
    }
}
