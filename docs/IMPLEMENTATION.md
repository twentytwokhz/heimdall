# Heimdall - Implementation Design

> **Heimdall** - the local, offline Azure API Management (APIM) data-plane emulator + console.
> Technical design for the local, offline Azure API Management (APIM) data-plane emulator.
> Companion to `PRD.md`. This document is the engineering source of truth for *how* it's built.

**Stack:** .NET 10 (LTS) / C# · ASP.NET Core · YARP (`Yarp.ReverseProxy`) · Roslyn
(`Microsoft.CodeAnalysis.CSharp.Scripting`) · System.Text.Json (emulator internals) ·
Newtonsoft.Json (policy-expression sandbox only) · Docker.

---

## 1. Design principles

1. **Behavioral indistinguishability (data plane).** For the supported policy set, the app
   cannot tell the emulator from real Azure APIM - same status codes, headers, body transforms,
   key handling, and error shapes.
2. **Fail loud, never silent.** Unsupported policies cause a clear error at load or execution
   time. This is the deliberate opposite of the self-hosted gateway, which silently skips them.
3. **Canonical model + pluggable loaders.** Every input format (XML+OpenAPI, APIOps, Terraform,
   ARM, Bicep, live import) decomposes into one canonical model. New formats are new
   `IConfigLoader` implementations - **zero engine changes**.
4. **The policy engine is the product.** Forwarding (YARP) is one stage among four; everything
   orbits the pipeline and the expression evaluator.
5. **Small, testable units.** Parser, expression engine, each policy, router, and loaders are
   independently testable with clear interfaces.

---

## 2. Architecture overview

```
                       ┌──────────────────────────────────────────────┐
   HTTP request  ──▶   │            Heimdall.Api                   │
   (your app)          │  ┌────────────┐   ┌────────────────────────┐ │
                       │  │  Router     │──▶│   Policy Pipeline       │ │
                       │  │ host/path/  │   │  inbound → backend →    │ │
                       │  │ method →    │   │  outbound → on-error    │ │
                       │  │ Api+Op      │   └─────────┬──────────────┘ │
                       │  └────────────┘             │ (backend stage)  │
                       │                             ▼                  │
                       │                   YARP IHttpForwarder ─────────┼──▶ real backend
                       │  ┌──────────────────────────────────────────┐ │
                       │  │ Auth/Resource layer: sub-keys, products,  │ │
                       │  │ named values                              │ │
                       │  └──────────────────────────────────────────┘ │
                       └──────────────────────────────────────────────┘
                          ▲ loads from
            ┌─────────────┴───────────────────────────────┐
            │  IConfigLoader → Canonical Model              │
            │  v1: XmlOpenApi · later: ApiOps/Terraform/ARM │
            └───────────────────────────────────────────────┘
```

### Request flow (happy path)
1. **Router** matches the incoming host + path + method against the loaded API surface
   (OpenAPI operations) to resolve a concrete **`Api` + `Operation`**. No match → `404`
   (matching APIM's "resource not found" behavior).
2. **Auth/Resource layer** validates the subscription key (header `Ocp-Apim-Subscription-Key`
   first, else query `subscription-key`) against subscriptions/products. Missing/invalid → `401`.
3. **Effective policy** is built by flattening global → product → API → operation, resolving
   `<base/>` (see §5). Cached per (Api, Operation, subscription-scope).
4. **Inbound** policies execute over a mutable `PolicyContext`.
5. **Backend** stage: the `forward-request` policy invokes **YARP `IHttpForwarder`** to the
   destination (default backend or whatever `set-backend-service` set). `return-response` /
   `mock-response` short-circuit and skip this entirely.
6. **Outbound** policies execute over the response.
7. Any exception in 4-6 aborts the current section and jumps to **on-error** (see §6).

---

## 3. Solution / project layout

Built on standard .NET Clean Architecture conventions:
`.slnx` solution, Clean Architecture layers, **provider-plugin projects** for pluggable
integrations, **Central Package Management**, .NET 10, `AddXxx()` DI extensions, Scrutor
auto-registration, xUnit + Moq + Shouldly + NetArchTest.

```
apim-emulator/
  Heimdall.slnx
  Directory.Build.props            # net10.0, Nullable, ImplicitUsings (standard)
  Directory.Packages.props         # Central Package Management - all versions pinned here
  src/
    Heimdall.Domain/                          # canonical model (records), enums - no deps
    Heimdall.Application/                      # contracts + engine: IConfigLoader, IPolicy,
                                                   #  IPolicyContext, IExpressionEvaluator,
                                                   #  ICounterStore, ICacheStore, IClock; pipeline
                                                   #  orchestration; <base/> flattening; registry
    Heimdall.Infrastructure/                   # Roslyn expression engine, in-memory counter/
                                                   #  cache stores, clock, policy implementations
    Heimdall.Infrastructure.XmlOpenApiLoader/  # v1 config loader (provider plugin)
    Heimdall.Infrastructure.ApiOpsLoader/      # fast-follow loader (provider plugin)
    Heimdall.Api/                              # ASP.NET Core host, YARP, router, middleware,
                                                   #  concrete PolicyContext over HttpContext,
                                                   #  auth/resource layer, Program.cs composition
  tests/
    Heimdall.Tests/                            # xUnit + Moq + Shouldly + NetArchTest
  samples/
    policies/ openapi/ backend/                    # runnable sample config + tiny stub backend
  Dockerfile                                       # multi-stage sdk:10.0 → aspnet:10.0, :8080
  docker-compose.yml                               # emulator + sample backend (+ Seq, optional)
```

**Policy implementations** (one class per element) live in `Infrastructure`, depend only on
`IPolicyContext`/`IExpressionEvaluator` from `Application`, and are **auto-registered via
Scrutor** by scanning for `IPolicy` (a common ASP.NET Core pattern). The **provider-selection-by-config**
idiom maps cleanly to ours: `ConfigLoader:
"XmlOpenApi"|"ApiOps"` and `Backend: "Forward"|"Mock"`, wired in `Program.cs` via `AddXxx()`.

Dependency direction (Clean Architecture, **NetArchTest-enforced**):
`Domain` ← `Application` ← `Infrastructure*` ← `Api`. `Domain` references nothing; `Api`
composes everything via DI. Loaders are `Infrastructure.*` plugins depending only on `Application`.

---

## 4. Canonical model (`Heimdall.Domain`)

The single internal representation all loaders produce and the engine consumes.

```csharp
public sealed record GatewayConfig(
    IReadOnlyList<Api> Apis,
    IReadOnlyList<Product> Products,
    IReadOnlyList<Subscription> Subscriptions,
    IReadOnlyList<NamedValue> NamedValues,
    IReadOnlyList<Backend> Backends,
    PolicyDocument? GlobalPolicy);            // global scope

public sealed record Api(
    string Id, string DisplayName, string Path,           // url path prefix
    IReadOnlyList<Operation> Operations,
    PolicyDocument? Policy,                                // API scope
    IReadOnlyList<string> ProductIds,
    Uri? ServiceUrl = null);                              // APIM web service URL: default forward destination

public sealed record Operation(
    string Id, string Method, string UriTemplate,
    PolicyDocument? Policy);                               // operation scope

public sealed record Product(string Id, string DisplayName, bool RequiresSubscription,
    PolicyDocument? Policy, IReadOnlyList<string> ApiIds);

public sealed record Subscription(string Id, string PrimaryKey, string SecondaryKey,
    SubscriptionScope Scope, string? ProductId, string? ApiId, SubscriptionState State);

public enum SubscriptionScope { Product, Api, AllApis, AllAccess }

public sealed record NamedValue(string Name, string Value, bool Secret);
public sealed record Backend(string Id, Uri Url /*, credentials etc. */);

// PolicyDocument is the parsed-but-faithful policy XML for one scope.
public sealed record PolicyDocument(
    IReadOnlyList<PolicyNode> Inbound,
    IReadOnlyList<PolicyNode> Backend,
    IReadOnlyList<PolicyNode> Outbound,
    IReadOnlyList<PolicyNode> OnError);
```

### Core interfaces

```csharp
public interface IConfigLoader {
    Task<GatewayConfig> LoadAsync(string sourcePath, CancellationToken ct);
}

public interface IPolicy {
    string ElementName { get; }                 // e.g. "set-header"
    PolicySection Sections { get; }             // flags: which stages it's valid in
    ValueTask ApplyAsync(IPolicyContext ctx, PolicyNode node);
}

public interface IPolicyContext {                // mirrors APIM's `context`
    EmuRequest Request { get; }
    EmuResponse Response { get; }
    IDictionary<string, object?> Variables { get; }
    SubscriptionInfo? Subscription { get; }
    ProductInfo? Product { get; }
    ApiInfo Api { get; }
    OperationInfo Operation { get; }
    UserInfo? User { get; }
    DeploymentInfo Deployment { get; }
    LastErrorInfo? LastError { get; }
    Uri? BackendServiceUrl { get; set; }        // set by set-backend-service
    bool ShortCircuited { get; }                // set by return/mock-response
    IExpressionEvaluator Expressions { get; }
    INamedValues NamedValues { get; }
}

public interface ICounterStore { /* rate-limit + quota windows, in-memory now */ }
public interface ICacheStore   { /* cache-store/lookup, in-memory now */ }
public interface IClock         { DateTimeOffset UtcNow { get; } }   // injectable for tests
```

`ICounterStore` / `ICacheStore` / `IClock` are interfaces specifically so a Redis-backed
implementation (shared counters across instances) can drop in later without touching policies.

---

## 5. Policy parsing, flattening & the support model (`Heimdall.Application` + `Heimdall.Infrastructure`)

### Parsing
Policy XML is parsed into `PolicyNode` (element name + attributes + children + raw inner text),
preserving structure verbatim. Each top-level section (`<inbound>`, `<backend>`, `<outbound>`,
`<on-error>`) becomes a list of `PolicyNode`.

### `<base/>` flattening (effective policy)
APIM composes policies across four scopes. `<base/>` injects the parent scope's policies at that
position. Effective policy for an operation is computed by walking
**global → product → API → operation** and, at each level, replacing `<base/>` with the
already-flattened parent section.

```
effective(operation) =
    splice(operation.section, base = effective(api))
effective(api) =
    splice(api.section, base = effective(product?))     // product scope only if applicable
effective(product) =
    splice(product.section, base = global.section)
```

**Subscription-scope bypass rule (must implement):** when the request's subscription scope is
**Api**, **AllApis**, or **AllAccess** (i.e., not a product subscription), the **product-scoped
policies are skipped** in the chain. This is a documented APIM quirk and a likely source of
"works in emulator, breaks in Azure" if missed - covered by a dedicated conformance test.

The flattened effective policy is **cached** keyed by `(apiId, operationId, subscriptionScope)`.

### Policy support matrix (v1 = tier 1, breadth-first)

| Category | Policies | Notes |
|---|---|---|
| Control flow | `choose/when/otherwise`, `set-variable`, `include-fragment` | `when` conditions use the expression engine |
| Transform | `set-header`, `set-body`, `set-method`, `rewrite-uri`, `set-query-parameter`, `set-backend-service`, `find-and-replace` | `set-backend-service` sets `ctx.BackendServiceUrl` |
| Auth/security | `validate-jwt`, `check-header`, `ip-filter`, `cors` | `validate-jwt` validates against locally supplied keys/JWKS |
| Subscription key | (built into gateway, not a policy element) | header then query; `401` on miss |
| Rate/quota | `rate-limit`, `rate-limit-by-key`, `quota`, `quota-by-key` | `ICounterStore`; `429` + `Retry-After` |
| Routing/response | `forward-request`, `return-response`, `mock-response`, `set-status` | response policies short-circuit forwarding |
| Caching | `cache-lookup`/`cache-store`, `cache-lookup-value`/`cache-store-value` | `ICacheStore` |
| Named values | `{{name}}` substitution | resolved by `INamedValues` |

**Deferred (tier 2 / out of scope v1):** `validate-content`, `validate-parameters`,
`send-request`, `send-one-way-request`, `retry`, `wait`, managed-identity auth, GraphQL,
WebSockets, LLM/semantic-caching. An unknown/unsupported element throws a clear
`UnsupportedPolicyException` at flatten time (fail loud).

---

## 6. Pipeline execution & error semantics (`Heimdall.Application`; host in `Heimdall.Api`)

Pseudocode for the runtime that drives the four stages:

```csharp
async Task<EmuResponse> ExecuteAsync(IPolicyContext ctx, EffectivePolicy p) {
    try {
        await RunSection(p.Inbound, ctx);
        if (!ctx.ShortCircuited)
            await ForwardToBackend(ctx, p.Backend);   // YARP IHttpForwarder (§7)
        await RunSection(p.Outbound, ctx);
    }
    catch (PolicyException ex) {
        ctx.SetLastError(ex);                          // populates context.LastError
        await RunSection(p.OnError, ctx);              // on-error stage
    }
    return ctx.Response;
}

async Task RunSection(IReadOnlyList<PolicyNode> nodes, IPolicyContext ctx) {
    foreach (var node in nodes) {
        if (ctx.ShortCircuited) break;                 // return/mock-response stops the section
        var policy = _registry.Resolve(node.Name);     // IPolicy by element name
        await policy.ApplyAsync(ctx, node);
    }
}
```

Key semantics to match APIM:
- A policy error **skips the rest of the current section** and routes to `on-error`.
- `return-response` / `mock-response` set `ShortCircuited` → forwarding and remaining policies
  in the section are skipped; outbound still runs (matching APIM).
- `context.LastError` is populated for `on-error` expressions.

---

## 7. YARP integration (the `backend` stage)

YARP is used at the **forwarding** layer, not as the whole pipeline. The gateway maps each
request through the policy pipeline; the `forward-request` policy calls YARP's low-level
`IHttpForwarder.SendAsync` with the destination resolved from `ctx.BackendServiceUrl`
(or the configured default backend).

```csharp
// inside the forward-request policy / backend stage
var destination = ctx.BackendServiceUrl ?? _defaultBackend.Url;
await _forwarder.SendAsync(httpContext, destination.ToString(), _httpClient, transformOpts);
```

Rationale: this is YARP's documented **Direct Forwarding** path (`AddHttpForwarder()` +
`IHttpForwarder.SendAsync`), the integration YARP ships for proxies that own their own routing,
load-balancing, and affinity and only need to forward a chosen request to a chosen destination
(see YARP's `samples/ReverseProxy.Direct.Sample`). It gives production-grade streaming, header
handling, HTTP/2 + gRPC + WebSocket support, and connection management while leaving us full control
over the four-stage semantics and error routing. We deliberately do **not** use YARP's config-driven
route table for matching - our OpenAPI-driven router owns matching, because APIM matching (API path +
operation `uriTemplate` + method) differs from YARP's route model.

Best-practice wiring (per YARP docs):
- Register only the forwarder: `builder.Services.AddHttpForwarder()`.
- Share one long-lived, proxy-tuned `HttpMessageInvoker` (never a default `HttpClient`):
  `new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false,
  AutomaticDecompression = DecompressionMethods.None, UseCookies = false,
  EnableMultipleHttp2Connections = true, ActivityHeadersPropagator =
  new ReverseProxyPropagator(DistributedContextPropagator.Current), ConnectTimeout = 15s }`. These
  settings keep the proxy transparent (no silent redirect-following, decompression, or cookie state).
- Pass `HttpTransformer.Default` when no transform is needed (Phase 1); header/body transforms move
  into a custom `HttpTransformer` (or are applied to `HttpContext` before forwarding) in later phases.
- Inspect the returned `ForwarderError` and `IForwarderErrorFeature` for failures, mapping them to
  APIM-shaped responses.

> Validation spike (M1): this confirms the standard Direct Forwarding wiring works mid-pipeline with a
> destination chosen at call time from `ctx.BackendServiceUrl`, and records the tuned invoker config
> above as the baseline. Fallback (only if a sharp edge appears): a thin `HttpClient` forwarder behind
> the same `ForwardAsync(HttpContext, Uri)` seam - the `forward-request` policy is the only thing that
> changes.

### Buffered, policy-aware forward (M3)

Once policies mutate the request and the response, the forward can no longer stream straight through:
outbound policies must run on the response *before* anything reaches the client. The backend stage is
therefore **buffered**, implemented as a `HeimdallHttpTransformer : HttpTransformer` driven by the
`IPolicyContext`:

- `TransformRequestAsync` calls `base` (method/path/body) then applies the context's request mutations
  (headers today; method/uri/body as those transforms land) onto the proxy request.
- `TransformResponseAsync` reads the backend `HttpResponseMessage` (status, headers, body) into
  `context.Response` and **returns `false`**, which suppresses YARP's auto-copy. The executor then runs
  outbound policies and the gateway writes `context.Response` to `HttpContext.Response` itself.

> Validation spike (M3, Batch 0): confirmed under the WireMock + WebApplicationFactory/TestServer
> harness that a transformer returning `false` (without calling `base`) captures the backend response
> and leaves `HttpContext.Response.HasStarted == false`, so outbound policies can still mutate it. No
> fallback needed (the documented alternative was capturing via `HttpMessageInvoker.SendAsync` behind
> the same `IForwarder` seam). Large-body streaming stays a documented fidelity boundary - tier-1
> buffers, per the Phase 5 note.

---

## 8. Expression engine (`Heimdall.Infrastructure`)

APIM policy expressions are real C#: `@(...)` (single expression) and `@{...}` (statement block
returning a value). We evaluate them with Roslyn scripting.

- **Compilation & cache:** each unique expression text is compiled once to a delegate and cached
  (keyed by text). Repeated requests reuse the compiled delegate → sub-2s loop.
- **Globals = `context`:** scripts run against a globals object exposing the documented
  `context.*` surface (`Request`, `Response`, `Variables`, `User`, `Subscription`, `Product`,
  `Api`, `Operation`, `Deployment`, `LastError`).
- **References (the fidelity seam):** `Newtonsoft.Json` is added to the Roslyn `ScriptOptions`
  **only**, because APIM exposes Newtonsoft (`JObject`/`JArray`/`JToken`/`JsonConvert`) to policy
  authors and `context.Request.Body.As<JObject>()` returns a Newtonsoft `JObject` - real policies
  call these directly, so referencing `System.Text.Json` here instead would fail to compile them.
  Plus the standard BCL subset APIM documents; namespaces are allow-listed. **Everywhere else in
  the codebase uses `System.Text.Json`** (config loaders, DTOs, host plumbing) - Newtonsoft never
  leaks past this seam.
- **Fidelity boundary:** the goal is the documented expression API surface and allowed types.
  Constructs outside that surface are a known fidelity boundary, surfaced as clear errors.

```csharp
public interface IExpressionEvaluator {
    T Evaluate<T>(string expressionText, IPolicyContext ctx);   // compiled+cached
    string Interpolate(string template, IPolicyContext ctx);    // mixed literal + @(...) text
}
```

---

## 9. Config loaders

### v1 - `Loaders.XmlOpenApi`
Input: a directory containing an OpenAPI document per API (defines APIs/operations + the backend
`servers[0].url` -> `Api.ServiceUrl`), policy XML files mapped to scopes by filename convention,
and a `config.json` binding it together. Produces `GatewayConfig`. This *is* the canonical shape -
the simplest possible loader.

**`config.json`** lists `apis` (each: `id`, `displayName`, `path`, `openApiFile`, `productIds`)
and `backends` (`id`, `url`); products, subscriptions, and named values land in Phase 4.

**Policy-file -> scope naming convention** (under `policies/`):
- `global.xml` -> global scope
- `{apiId}.api.xml` -> API scope
- `{apiId}.{operationId}.op.xml` -> operation scope
- `{productId}.product.xml` -> product scope (Phase 4)

A missing file means "no policy at that scope" (not an error).

### Fast-follow - `Loaders.ApiOps`
Reads the APIOps extractor folder layout (`apis/`, `policies/`, `products/`, `named-values/`,
`subscriptions/`) and produces the same `GatewayConfig`. High real-world payoff: teams already
keep this in git.

### Later - Terraform / ARM / Bicep / live import
All are adapters to the same model (PRD Epic 1.3-1.4). Terraform: parse
`azurerm_api_management_*` resources or `terraform show -json`, extracting `xml_content` policies.
**No engine changes** for any of these.

---

## 10. Observability - local policy trace

A per-request trace records which policies ran, branch decisions (`choose`), transforms applied,
the resolved backend, and key/rate-limit decisions. This is the local equivalent of APIM trace -
**without** the Developer/Premium tier gating. Default on/off and surfacing mechanism is PRD
Open Question Q2 (header opt-in vs. always-on log).

---

## 10b. Local console UI (embedded SPA) - Phase 7

A web console served by the gateway itself (same container, no extra service). It mirrors the
mental model of Azure's APIM portal - left-nav resource model + the **Frontend → Inbound →
Backend → Outbound (+ on-error)** policy canvas with policy chips - but with a far better,
modern design, and the thing Azure can't do locally: **live request tracing overlaid on that
same canvas**.

**Capabilities (all four chosen):**
1. **Observability + debug console** - live request feed (SignalR push); click a request → the
   full execution trace rendered on the four-stage canvas: which policy chips fired, branch
   decisions (`choose/when`), header/body transforms (before/after diffs), evaluated `@()`
   expression results, rate-limit/cache hits, resolved backend, final status.
2. **Config explorer (read-only)** - browse APIs/operations/products/subscriptions/named values/
   backends + the **flattened effective policy** per API/operation (with scope provenance).
3. **Request playground** - compose a request (method/path/headers/body, with/without
   subscription key), fire it at the gateway, watch the trace light up live. Includes **collection
   import (replay-only)**: load requests from a Postman Collection v2.1 export or `.http` files and
   replay them against the gateway (no script execution, no write-back). Full spec in
   `IMPLEMENTATION_PLAN.md` (Phase 7 → "Collection import into the playground").
4. **Policy authoring + hot-reload** - edit policy XML in-browser, save to the local config dir,
   hot-reload the gateway, immediately re-trace. **Boundary:** edits the *loaded local config
   only* - NOT the Azure management plane, developer portal, or ARM/Terraform write-back.

**Architecture:**
- **Frontend:** React + Vite + TypeScript SPA in `src/Heimdall.Ui/`, built with the
  `frontend-design` skill (and the team's shadcn/ui + Tailwind tooling) for a distinctive,
  polished look. Built to static assets and served from the Api's `wwwroot` with SPA fallback.
- **Admin/observability API** in `Heimdall.Api` under a reserved prefix (e.g. `/_apim/*`),
  kept strictly separate from proxied API traffic: `GET /_apim/config` (canonical model),
  `GET /_apim/policies/{api}/{op}` (effective policy), `POST /_apim/policies/...` (authoring +
  reload), `POST /_apim/playground` (fire a test request). A **SignalR hub** streams the live
  trace feed to the console.
- **Trace source:** the per-request trace from §10, captured by an in-memory ring-buffer
  `ITraceSink` (bounded; newest-N) that the SignalR hub and `GET /_apim/traces` read from.
- **Disable switch:** the console + admin API can be turned off via config for pure-headless/CI use.

---

## 11. Delivery (Docker)

- Multi-stage `Dockerfile`: a **Node stage** builds the Vite SPA → its output is copied into the
  Api's `wwwroot`; then the .NET SDK stage publishes the Api; runtime stage is `aspnet:10.0`.
- Config supplied via a mounted volume; port via env vars. An API's default forward target
  (`Api.ServiceUrl`, from the OpenAPI `servers[0].url`) is overridable per environment via
  `Heimdall__BackendOverrides__<apiId>` (e.g. `Heimdall__BackendOverrides__acme=http://backend:8081`;
  `<apiId>` matches the API id in config, case-sensitive, and an unknown id or non-http(s) URL fails
  loud at startup), applied host-side after the loader runs so loaders stay pure. This lets the same
  config forward to `localhost` on a host run and to the compose service name inside the network.
- `docker-compose.yml` runs the emulator alongside the `samples/backend` stub (with the backend
  override set) so the whole path works with one command.

```
docker run -p 8080:8080 -v $(pwd)/samples:/config apim-emulator --config /config
```

---

## 12. Testing strategy (`tests/Heimdall.Tests`, xUnit)

1. **Expression engine tests** - given expression text + a fabricated `context`, assert the
   evaluated value (headers, variables, JSON via Newtonsoft, etc.).
2. **Per-policy unit tests** - each `IPolicy` against a fabricated `IPolicyContext`: assert the
   mutation (header set, uri rewritten, `429` on rate-limit, short-circuit on `mock-response`).
3. **Flattening tests** - `<base/>` ordering across scopes + the subscription-scope bypass rule.
4. **End-to-end tests** - ASP.NET Core `TestServer` hosting the gateway against an in-memory
   backend: `curl`-equivalent requests asserting status/headers/body parity, e.g.:
   - no key → `401`; valid key → forwarded.
   - `rate-limit` exceeded → `429` + `Retry-After`.
   - `set-header` / `rewrite-uri` transforms visible at the backend.
   - `mock-response` returns canned body with no backend running.
   - `validate-jwt` rejects an invalid token.
5. **Conformance suite (depth pass)** - table of (policy XML, request) → expected response,
   asserting parity with documented APIM semantics; this is the regression net for fidelity.
6. **UI end-to-end suite (Phase 7)** - **Microsoft.Playwright + xUnit**, under
   `tests/Heimdall.Tests/E2E/`. Once the console SPA exists, these drive a real browser against the
   running gateway + console to validate every implemented feature through the UI (config explorer,
   live trace canvas, playground replay, policy authoring + hot-reload). Browsers are installed via
   the Playwright CLI (`pwsh bin/Debug/net10.0/playwright.ps1 install`, i.e. `playwright install`).
   The suite is gated behind the Phase 7 headless/CI disable-switch so the default `dotnet test` run
   stays browserless. Scaffolded in Phase 7 (no UI to drive before then), not earlier.

`IClock` injection makes rate-limit/quota windows deterministic in tests.

---

## 13. Milestones (deliverable per milestone)

| # | Milestone | Deliverable / exit criteria |
|---|-----------|------------------------------|
| **M0** | Docs | `PRD.md` + this doc, reviewed. *(current step)* |
| **M1** | Walking skeleton | ASP.NET Core + YARP forwards a request to a backend; XmlOpenApi loader; router matches one API/op; `<base/>` flattening; no real policies. YARP imperative-forward spike confirmed. |
| **M2** | Expression engine | Roslyn compile/cache + `context` shim; `set-header`/`set-variable` work via expressions; expression xUnit tests green. |
| **M3** | Breadth | Tier-1 policy matrix (§5) end-to-end; per-policy + e2e tests; **edit→test loop < 2s** verified. |
| **M4** | Resource model | Subscriptions/products/keys/named values + key enforcement (`401`). |
| **M5** | Depth + conformance | `context.*` completeness, rate-limit/JWT fidelity, subscription-scope bypass, conformance suite. |
| **M6** | APIOps loader + polish | APIOps loader, samples, docs; (Terraform/ARM/Bicep/live import remain backlog adapters). |

---

## 14. Open technical questions

- **TQ1 (→ PRD Q3):** `validate-jwt` key supply - static PEM/JWK file vs. local JWKS endpoint.
- **TQ2:** YARP `IHttpForwarder` imperative invocation mid-pipeline - confirm in M1 spike (§7).
- **TQ3:** Roslyn cold-start compile cost vs. the <2s target - measure in M2; warm/precompile if needed.
- **TQ4:** Exact tier-1 list vs. the policies the real target APIs use (→ PRD Q4).

---

*See `PRD.md` for product scope, personas, and success metrics.*
