# Heimdall - Phased Implementation Plan

> Execution plan for building the emulator. Pairs with `docs/IMPLEMENTATION.md` (the design:
> architecture, canonical model, signatures) and `docs/PRD.md` (scope, acceptance criteria).
> This document is the **build sequence** - what to do, in what order, with what gates.

## How to use this plan

- **TDD per task**: write the failing test first, then the implementation, then green. Test
  libs: xUnit + Moq + Shouldly. Direct constructor injection for unit tests; `WebApplicationFactory<Program>`
  (`EmulatorFactory`/`TestAppFactory`) for end-to-end.
- **Commit often**: commit at each meaningful green step (a batch, a policy, a passing test
  group) - small, focused commits, not one big drop per phase. Commit messages describe the
  change only - **no Claude attribution** (enforced by `.claude/settings.json`).
- **Per-phase loop (required)**: each phase runs **plan → execute → review → commit**:
  (1) **plan mode** - write the phase plan and get user approval before any code;
  (2) **execute** - implement against the approved plan, committing often within the phase;
  (3) **review** - **code-review** the phase diff, address findings;
  (4) **commit** - finalize once exit criteria pass **and** review is clean. A phase is done only when all four are complete.
- **Fail loud**: unknown/unsupported policy elements throw `UnsupportedPolicyException` at flatten
  time - never silently skipped.
- **The Newtonsoft seam**: `Newtonsoft.Json` is referenced only in the Roslyn `ScriptOptions` and
  the `*EmuBody`/`ScriptOptionsFactory` types. A NetArchTest enforces zero leakage elsewhere; the
  rest of the codebase is System.Text.Json.

## Conventions

`.slnx` solution · `Directory.Build.props` (`net10.0`, Nullable, ImplicitUsings) ·
`Directory.Packages.props` (Central Package Management - all versions pinned, csproj uses
version-free `<PackageReference>`) · DI via `AddXxx()` extension methods (one per project) ·
Scrutor for assembly-scan registration · Serilog (bootstrap logger → `UseSerilog` →
`UseSerilogRequestLogging`) · provider-plugin projects (`Infrastructure.*`) selected by config key ·
Dockerfile multi-stage `mcr…/sdk:10.0` → `aspnet:10.0`, non-root `1000:1000`, `EXPOSE 8080`,
`ASPNETCORE_URLS=http://+:8080`, `/p:UseAppHost=false`.

### Pinned packages (`Directory.Packages.props`)

| Package | Version | Used by |
|---|---|---|
| xunit / xunit.runner.visualstudio | 2.9.3 / 3.1.x | tests |
| Microsoft.NET.Test.Sdk | 17.14.x | tests |
| Moq | 4.20.72 | tests |
| Shouldly | 4.3.0 | tests |
| NetArchTest.Rules | 1.3.2 | tests (layering + Newtonsoft seam) |
| Microsoft.AspNetCore.Mvc.Testing / TestHost | 10.0.x | e2e tests |
| Scrutor | 7.0.0 | DI scan |
| Serilog.AspNetCore / Sinks.Seq | 10.0.0 / 9.0.0 | logging |
| Yarp.ReverseProxy | 2.3.x (verify net10 artifact) | Api forwarding |
| Microsoft.OpenApi.Readers | 1.6.x | XmlOpenApi loader |
| Microsoft.CodeAnalysis.CSharp.Scripting | 4.13-4.14 | expression engine |
| Newtonsoft.Json | 13.0.3 | expression sandbox ONLY |
| Microsoft.IdentityModel.JsonWebTokens / System.IdentityModel.Tokens.Jwt | 8.10.x | validate-jwt |
| YamlDotNet | 16.3.x | APIOps loader metadata |

---

## Phase 0 - Solution scaffold

**Goal:** everything builds + tests run; trivial `/health` returns 200; layering test green;
Docker image builds and runs.

**Key files:** `Heimdall.slnx`, `Directory.Build.props`, `Directory.Packages.props`,
the 6 projects (`Domain`, `Application`, `Infrastructure`, `Infrastructure.XmlOpenApiLoader`,
`Api`, `tests/Heimdall.Tests`), `Dockerfile`, `docker-compose.yml`,
`tests/.../Architecture/LayeringTests.cs`, `tests/.../Fixtures/TestAppFactory.cs`,
`samples/{policies,openapi,backend,config.json}`. Canonical model records in `Domain`; core
interfaces in `Application` (see `IMPLEMENTATION.md` §4).

**Tasks:** scaffold props → Domain records → Application interfaces → Infrastructure DI stub +
`SystemClock` → XmlOpenApiLoader stub → Api `Program.cs` (Serilog + health) → Tests csproj →
**[test-first]** layering test → `.slnx` → Dockerfile + compose → sample stub backend.

**Verification:** `dotnet build Heimdall.slnx` = 0; `dotnet test --filter ~Architecture` green;
`docker build` + `docker run -p 18080:8080` → `curl /health` returns `{"status":"healthy"}`.

**Commit points:** props+slnx; each project as it builds; layering test; Docker.
**Review gate:** code-review the scaffold (focus: project boundaries, DI shape, Dockerfile).

## Phase 1 (M1) - Walking skeleton

**Goal:** a routed request flows empty inbound → **YARP `IHttpForwarder`** backend forward →
empty outbound to the sample backend; `<base/>` flattening computes an (empty) effective policy.

**Do first - YARP Direct Forwarding spike:** confirm YARP's documented Direct Forwarding path works
mid-pipeline - `builder.Services.AddHttpForwarder()`, a shared proxy-tuned `HttpMessageInvoker`
(`SocketsHttpHandler` with `AllowAutoRedirect`/`UseCookies`/`AutomaticDecompression` off, see
`IMPLEMENTATION.md` §7), `HttpTransformer.Default`, and `IHttpForwarder.SendAsync` to a destination
chosen at call time from `ctx.BackendServiceUrl`. The baseline invoker config is captured in
`IMPLEMENTATION.md` §7. **Fallback** (only if a sharp edge appears): an `HttpClient`-based
`HttpClientForwarder` behind the same `ForwardAsync(HttpContext, Uri)` seam - nothing else changes.

**Key files:** `Application/Pipeline/EffectivePolicy.cs` + `EffectivePolicyBuilder.cs` (`<base/>`
splice), `UnsupportedPolicyException`; `XmlOpenApiLoader/XmlOpenApiConfigLoader.cs` +
`PolicyXmlParser.cs`; `Api/Routing/{ApiRouter,UriTemplateMatcher,RouteMatch}.cs`;
`Api/Forwarding/YarpForwarder.cs`; `Api/Middleware/GatewayMiddleware.cs`;
`Api/Http/{HttpPolicyContext,EmuRequest,EmuResponse}.cs`; `Api/Configuration/GatewayConfigHolder.cs`.

**Tasks (test-first):** flatten empty → `<base/>` splice positions → loader produces `GatewayConfig`
from sample → router match/no-match → gateway middleware e2e (matched→200 forwarded, unmatched→404).

**Verification:** `dotnet test`; `docker compose up`; `curl /pets` → sample backend body;
`curl /nonexistent` → 404.

**Commit points:** spike note; flattening; loader; router; middleware+forwarder; compose.
**Review gate:** code-review the phase diff (focus: forwarding correctness, `<base/>` edge cases incl. global-has-no-parent, URI-template matching).

## Phase 2 (M2) - Expression engine

**Goal:** compiled + cached Roslyn evaluator; `@(...)` and `@{...}` evaluate against a `context`
mirroring APIM's surface; `Interpolate` for mixed templates; Newtonsoft seam intact.

**Key files:** `Application/Expressions/IExpressionEvaluator.cs`; `Application/Context/*` (EmuRequest,
EmuResponse, EmuBody, SubscriptionInfo, ProductInfo, ApiInfo, OperationInfo, UserInfo, DeploymentInfo,
LastErrorInfo); `Infrastructure/Expressions/{RoslynExpressionEvaluator,ExpressionGlobals,
ScriptOptionsFactory,ExpressionCompileCache}.cs`; `Infrastructure/Context/{PolicyContext,HttpEmuBody}.cs`;
`Infrastructure/DependencyInjection.cs` (`AddExpressionEngine()`). Signatures: `IMPLEMENTATION.md` §8.

**Tasks (test-first):** interfaces + context types → expression tests (header access, `JObject`
indexing, statement block, cache-hit, `Interpolate`) → ScriptOptionsFactory → compile-cache →
`HttpEmuBody.As<JObject>()` (Newtonsoft) → evaluator → DI → **Newtonsoft-seam NetArchTest** →
measure cold-compile.

**Roslyn cold-start mitigation:** the <2s target is the *edit→test* (warm) loop, not first cold
compile (~0.8-4s). Add a warm-up `IHostedService` that compiles `@(true)` at startup; optionally
pre-compile all discovered expression texts in parallel at config load. Record a baseline in
`docs/perf-baseline.md`.

**Verification:** all expression xUnit tests green; seam test proves zero Newtonsoft leakage
outside the allowed types; cold/warm timings recorded.

**Commit points:** interfaces; each evaluator capability; seam test.
**Review gate:** code-review the phase diff (focus: cache correctness/thread-safety, sigil stripping, seam, `context` surface fidelity).

## Phase 3 (M3) - Tier-1 policy breadth

**Goal:** ~24 tier-1 policies execute end-to-end, Scrutor-registered (one class per element);
per-policy + e2e tests green; warm edit→test loop <2s.

**Batches (in order):** **0** plumbing (`IPolicy`, `PolicyNode` parser, `PolicyRegistry`+Scrutor scan,
`ICounterStore`/`ICacheStore`/`IClock` + in-memory impls, `PolicyPipelineExecutor` with section/short-
circuit/on-error semantics) → **1** transforms (`set-header`, `set-body`, `set-method`, `rewrite-uri`,
`set-query-parameter`, `find-and-replace`, `set-status`) → **2** routing/response (`forward-request`,
`return-response`, `mock-response`, `set-backend-service`) → **3** control flow (`set-variable`,
`choose/when/otherwise`, `include-fragment` resolved at flatten time) → **4** rate/quota
(`rate-limit`, `rate-limit-by-key`, `quota`, `quota-by-key` via `ICounterStore`; epoch-aligned window) →
**5** caching (`cache-lookup`/`store` + `-value`) → **6** auth (`check-header`, `ip-filter`, `cors`,
`validate-jwt` with local JWK Set file) → **7** `{{named-value}}` substitution.

**Key files:** `Infrastructure/Policies/{Transforms,ControlFlow,RoutingResponse,RateQuota,Caching,Auth}/*.cs`;
`Application/Policies/{IPolicy,PolicySection,PolicyNode,PolicyRegistry,UnsupportedPolicyException,PolicyException}.cs`;
`Application/Stores/*`; `Infrastructure/Stores/{InMemoryCounterStore,InMemoryCacheStore,SystemClock}.cs`.

**Verification:** per-policy unit tests; e2e (no key→401, rate-limit→429+`Retry-After`,
`set-header`/`rewrite-uri` visible at backend, `mock-response` with no backend, `validate-jwt`
rejects bad token, cors preflight, ip-filter block, cache hit). Confirm warm loop <2s via
`dotnet test --filter` on one class.

**Commit points:** Batch 0; **each policy** (or small group) as it greens.
**Review gate:** code-review the phase diff (focus: APIM error-shape parity, rate-limit window math, cors/jwt correctness, Scrutor registration completeness).

## Phase 4 (M4) - Resource & auth model

**Goal:** subscription-key validation before the pipeline (`Ocp-Apim-Subscription-Key` header, then
`subscription-key` query); missing/invalid → `401` in APIM's exact body shape; key stripped before
forwarding; subscription-scope **bypass rule** wired into flattening; `{{named-value}}` resolution.

**Key files:** `Application/Resources/{ISubscriptionStore,IProductStore,ISubscriptionKeyValidator,
SubscriptionKeyValidationResult,SubscriptionKeyValidator}.cs`; `Application/Pipeline/{PolicyFlattener,
EffectivePolicyCache,PolicyAttributeResolver}.cs`; `Infrastructure/Resources/{InMemorySubscriptionStore,
InMemoryProductStore,InMemoryNamedValues}.cs`; `Api/Middleware/{SubscriptionKeyMiddleware,ApimErrorShaper}.cs`;
`Api/Context/HttpPolicyContext.cs`. Signatures: blueprint §Phase 4 / `IMPLEMENTATION.md`.

**Tasks (test-first):** validator (valid primary/secondary, missing, unknown, suspended, scope match) →
flattener bypass (Api/AllApis/AllAccess skip product policies; Product includes them) → named-value
resolve + unknown→throw → in-memory stores → middleware (401 shape, **strip key** from forwarded request,
header-beats-query) → `HttpPolicyContext` populates `context.Subscription`/`Product` → e2e enforcement.

**Verification:** validator unit tests; bypass tests (4 scopes); e2e 401/200, query-param, header
precedence; 401 body matches APIM string verbatim (`{"statusCode":401,"message":"Access denied due to
missing subscription key…"}`).

**Commit points:** validator; flattener bypass; named values; middleware; e2e.
**Review gate:** code-review the phase diff (focus: 401 parity, key stripping, scope-bypass correctness, cache invalidation).

## Phase 5 (M5) - Depth + conformance

**Goal:** complete `context.*` surface; rate-limit/JWT fidelity; `on-error` depth; subscription-scope
bypass conformance; a table-driven conformance suite vs documented APIM semantics.

**Key files:** expand `Api/Pipeline/EmuPolicyContext.cs` + `Application/Context/*`; request-body
buffering middleware (`EnableBuffering`) so policies can re-read body; `tests/.../Conformance/{ConformanceTestCase,
ConformanceSuite}.cs` + `Cases/*.xml`; `tests/.../Architecture/ArchitectureTests.cs`.

> **File uploads / large bodies:** forwarding streams the body via `IHttpForwarder` (no buffering),
> so file uploads pass through efficiently by default. Buffering must stay **opt-in** - only enable
> `EnableBuffering` when an effective policy actually re-reads the body, or large uploads get spooled
> needlessly. Also raise/relax Kestrel `MaxRequestBodySize` (default 30 MB) for the gateway.

**Tasks:** complete context props (throw-with-message for not-yet-implemented, never null-deref) →
`validate-jwt` depth (RS256/HS256, key rotation, claims, schemes, query-param token, clock-skew) →
rate-limit/quota window accuracy with `FakeClock` → cors/ip-filter depth → `on-error` (`context.LastError`)
→ build conformance harness + ~20 rows (C01-C20) → NetArchTest full layering + Newtonsoft seam.

**Verification:** `dotnet test --filter ~Conformance` green; perf baseline updated.

**Commit points:** each depth area; conformance harness; each batch of conformance rows.
**Review gate:** code-review the phase diff (focus: fidelity gaps, conformance coverage, body-buffering correctness).

## Phase 6 (M6) - APIOps loader + polish

**Goal:** `ConfigLoader: "ApiOps"` produces the same `GatewayConfig` as XmlOpenApi from a real
APIOps extractor folder (target v6 layout); samples, docs, admin niceties.

**Key files:** new `src/Heimdall.Infrastructure.ApiOpsLoader/` (`ApiOpsConfigLoader.cs`,
`ApiOpsDirectoryLayout.cs`, `Readers/*`, `DependencyInjection.cs`); `Program.cs` loader selection;
`samples/apiops-layout/*`; `README.md` quick-start.

**Tasks (test-first):** loader + readers → loader unit tests → **parity test** (same logical config via
both loaders → equal `GatewayConfig`) → Key Vault named-value override escape hatch → loader selection +
startup banner (loaded counts, skipped-unsupported with file+line) → optional `POST /admin/reload` +
`GET /admin/status` → samples + compose variant → README.

**Verification:** loader + parity tests green; `docker run` with mounted APIOps folder serves requests.

**Commit points:** loader project; readers; parity test; samples; README.
**Review gate:** code-review the phase diff (focus: parity with XmlOpenApi, APIOps version handling, Key Vault fallback).

---

## Cross-cutting risks (carry through all phases)

1. **YARP imperative forward** - de-risked by the Phase 1 spike; `HttpClient` fallback ready.
2. **Roslyn cold-start** - warm-up service + optional pre-compile; baseline measured in M2.
3. **Newtonsoft seam** - NetArchTest from M2 onward guarantees no leakage.
4. **APIM error-shape parity** - centralize in an `ApimErrorShapes`/`ApimErrorShaper`; assert verbatim.
5. **Rate-limit window anchor** - epoch-aligned bucket via `IClock`; documented fidelity boundary vs
   APIM's subscription-anchored window.
6. **Named-value timing** - resolved at load (reload to change); documented deviation acceptable for offline dev.
7. **Subscription key stripping** - backend must never see the key; verified in e2e.
8. **APIOps layout version** - target v6 explicitly; fail loud on v4/v5 with a clear message.

## Phase 7 - Heimdall console UI (embedded SPA)

**Goal:** an embedded React/Vite SPA (served from the Api's `wwwroot`) + an admin/observability API
under `/_apim/*` + a SignalR trace feed - delivering: live request tracing on the
Frontend→Inbound→Backend→Outbound canvas, read-only config explorer, request playground, and
in-browser policy authoring with hot-reload. Modeled on Azure's portal structure, far better designed.

**Prereq:** the per-request trace + bounded ring-buffer `ITraceSink` (land it in M2/M3 as the engine
gains stages).

**Key files:** `src/Heimdall.Ui/` (Vite + React + TS, Tailwind + shadcn/ui); `Heimdall.Api` admin
endpoints (`GET /_apim/config`, `GET /_apim/policies/{api}/{op}`, `POST /_apim/policies` (author+reload),
`POST /_apim/playground`) + SignalR `TraceHub`; `Heimdall.Application` `ITraceSink` +
`Heimdall.Infrastructure` ring-buffer impl; Dockerfile node build stage → `wwwroot`.

**Tasks (test-first where applicable):** trace sink + hub → admin API (config / effective-policy /
playground) → SPA shell + design system (`frontend-design`) → config explorer → live trace canvas →
playground → **collection import (replay-only, see below)** → authoring + hot-reload → Docker node
stage + a disable-switch for headless/CI → **full-feature UI E2E suite** (see below).

**UI E2E suite (Microsoft.Playwright + xUnit, `tests/Heimdall.Tests/E2E/`):** once the console is up,
a browser-driven suite that walks the UI to validate *every* feature shipped through M1-M6 - routing,
the tier-1 policy set, the expression engine, subscription-key auth (401/200), rate-limit/quota,
caching, `validate-jwt`, named values - by exercising them via the config explorer, live trace canvas,
and playground replay. Browsers install through the Playwright CLI (`playwright install`); the suite is
gated behind the headless/CI disable-switch so the default `dotnet test` stays browserless. This is the
single end-to-end regression net that confirms the data plane and console agree on behavior. Scaffolded
here (not earlier) because it needs a real UI to drive.

**Shipped** (`tests/Heimdall.Tests/E2e/Browser/`): 12 specs across config explorer, live trace canvas,
playground (import / replay / variables) and policy authoring. The harness boots the Api
out-of-process (a real Kestrel serving the built SPA) with a WireMock stub backend wired via
`Heimdall:BackendOverrides`, then drives Chromium. Opt-in gate is a `[E2eFact]` attribute keyed on
`HEIMDALL_E2E=1` (no extra dependency); the default `dotnet test` reports the specs skipped (308 pass,
12 skipped, browserless). Subscription-key auth is covered through the UI (401 with the key removed);
rate-limit *enforcement* stays at the data-plane tests (the acme 5-calls/60s window is shared across
the single host), while its *presence* is verified through the effective-policy explorer - so the
suite stays deterministic without a fragile browser-driven 429 trip.

**Verification:** console loads at the gateway root; a playground request streams a live trace onto
the canvas; a Postman v2.1 export and a `.http` file import into the playground and replay against the
gateway; editing a policy + save hot-reloads and re-traces; design pass with
`frontend-design`/`ui-ux-pro-max`; **the Playwright UI E2E suite passes for each implemented feature.**

**Commit points:** trace sink; each admin endpoint; SPA shell; each UI surface; E2E suite harness + each feature spec.
**Review gate:** code-review the phase diff (focus: admin/proxy isolation, authoring write-safety, SignalR
backpressure) + a frontend design review.

### Collection import into the playground (replay-only) - required

**Goal:** the playground loads requests from a **Postman Collection v2.1** JSON export or one or
more **`.http`** files (the VS Code REST Client / JetBrains format), so a user can replay an existing
request set against the local gateway instead of hand-typing each one. This is a **required** Phase 7
deliverable: it builds on the base playground and ships within the same phase (not a data-plane
engine change).

**Scope (replay only):**
- Parse a collection into a flat list of playground requests: method, URL (path + query), headers,
  body. Folders flatten into a labelled list; nested folders keep a breadcrumb name.
- Resolve `{{variable}}` placeholders best-effort from the collection's own variables and an
  optionally supplied Postman environment export, at **import time**. Unresolved placeholders are
  left verbatim and flagged in the UI, never silently blanked.
- Rebase the request URL onto the local gateway origin (keep path/query/body; swap host) so imported
  requests hit Heimdall, with the original URL shown for reference.

**Boundary (fail loud, never silently "support"):**
- **No script execution.** Postman `prerequest`/`test` scripts and `.http` request-handler scripts
  are parsed but **not run**; their presence is surfaced as an "ignored: scripts not executed" note.
- No write-back, no environment sync, no Newman-style assertions, no auth-helper emulation (OAuth
  token fetch, AWS SigV4, etc.) - an imported auth block is passed through as plain headers only.
- Unsupported collection schema (anything other than Postman **v2.1**) fails the import with a clear
  message naming the detected version, consistent with the project's fail-loud rule.

**Key files:** `Heimdall.Application` `ICollectionImporter` + `PlaygroundRequest` DTO;
`Heimdall.Infrastructure` `PostmanV21Importer` and `HttpFileImporter` (System.Text.Json + a small
`.http` tokenizer, both behind the one interface); `Heimdall.Api` `POST /_apim/playground/import`
(multipart upload → list of `PlaygroundRequest`); a drop-zone + imported-request list in the
`src/Heimdall.Ui/` playground.

**Tasks (test-first):** importer interface + DTO → Postman v2.1 importer (folders, variables,
body modes: raw/urlencoded/formdata) → `.http` importer (request separators, headers, body,
file-level variables) → version/format guards (fail loud) → import endpoint → playground UI drop-zone
and replay wiring.

**Verification:** a sample Postman v2.1 export and a sample `.http` file (both Acme-only, committed
under `samples/collections/`) import into the playground and replay against the gateway; a
script-bearing collection imports with the scripts flagged as ignored; a v2.0 / non-Postman file is
rejected with a clear version message.

**Commit points:** importer interface + Postman importer; `.http` importer; import endpoint; UI wiring.
**Review gate:** code-review the diff (focus: schema-version guards, variable-resolution fidelity,
no script execution, URL rebasing correctness).

---

## Phase 8 - Documentation, marketing & showcase (pre-public)

**Goal:** turn the finished gateway + console into a public-ready repo: an accurate, well-marketed
README that sells the feature set, CI-published test + coverage badges, and a crafted Playwright
screenshot showcasing the console (matching the visual language in `design/`).

**Tasks:**
- **Code-gather:** walk the codebase and enumerate the real, shipped feature set (policies supported,
  `context.*` surface, loaders, conformance coverage, console capabilities) so the README markets what
  actually exists, no overclaiming. Cross-check against the conformance suite.
- **README rewrite:** feature-forward structure (what it is, why, supported policies matrix, quick
  start, architecture, fidelity boundaries). Keep the writing-style rules (no em-dashes, no AI fluff).
- **Badges:** a CI workflow (GitHub Actions) that runs `dotnet test` with coverage (Coverlet), publishes
  a test-status badge and a coverage badge, and wires both into the README.
- **Playwright showcase:** a script that boots the gateway + console, drives it through representative
  features, and captures a polished screenshot (and optional short clip) for the README hero, styled
  after `design/`.

**Verification:** README renders correctly; badges resolve from CI; screenshot regenerates from the
script deterministically.
**Review gate:** code-review the diff (focus: no overclaimed features, badge wiring, screenshot script
reproducibility). This is the last gate before the squash-to-one-commit + public-launch checklist.
