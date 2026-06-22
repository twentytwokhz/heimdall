# 🛡️ Heimdall

**A local, offline emulator of the Azure API Management (APIM) data plane - with a live policy-trace console.**

![license](https://img.shields.io/badge/license-MIT-blue) ![dotnet](https://img.shields.io/badge/.NET-10-512BD4) ![status](https://img.shields.io/badge/status-pre--release-orange)

---

Azure API Management has **no local emulator**. Every policy change means a live cloud instance
(30-40 minutes to provision, $50-$2,800/month per tier), a deploy, and a guess. The self-hosted
gateway isn't a local option either - it's data-plane only and needs the cloud instance to run.

**Heimdall runs APIM locally.** `docker run`, point it at your policy XML and OpenAPI spec, and the
edit-to-test loop drops from minutes to **under 2 seconds** - fully offline, no Azure account.

It is **not a mock.** Real C# policy expressions evaluated with **Roslyn**. Real `validate-jwt`,
`rate-limit`, `set-backend-service`, `choose/when`, caching, named values, subscription keys. Real
backends. For the supported policy set, the gateway behaves like production APIM - same `401`/`429`
shapes, same header/body transforms.

The part most tools miss: a **console that traces every request** through inbound → backend →
outbound, live - the thing Azure's own portal makes painful and tier-gates.

---

## Quickstart

```bash
# Run the gateway against a config directory (policy XML + OpenAPI + config.json)
docker run -p 8080:8080 -v "$(pwd)/samples:/config" heimdall --config /config

# Send a request through it - watch it forward to your backend, with policies applied
curl -H "Ocp-Apim-Subscription-Key: <key>" http://localhost:8080/catalog/items

# Open the console (live trace, config explorer, request playground) at:
#   http://localhost:8080
```

Or bring up the gateway + a sample backend together:

```bash
docker compose up
```

### Load from an APIOps extractor folder

Heimdall reads an [APIOps](https://github.com/Azure/apiops) **v6** extractor folder directly, so you can
run the same artifacts your APIM CI/CD already produces:

```bash
docker compose -f docker-compose.apiops.yml up
```

Set `Heimdall:ConfigLoader=ApiOps` and point `Heimdall:ConfigPath` at the extractor output. The extractor
never exports secrets, so supply the two things it omits, subscription keys and secret named-value
values, in a `heimdall.overrides.json` at the folder root:

```json
{
  "subscriptions": [
    { "id": "acme-standard-sub", "primaryKey": "...", "secondaryKey": "...",
      "scope": "Product", "productId": "acme-standard", "state": "Active" }
  ],
  "namedValues": { "secret-named-value": "local-dev-value" }
}
```

A worked example lives in [`samples/apiops-layout/`](samples/apiops-layout). With the admin API enabled
(`Heimdall:EnableAdminApi=true`), `GET /admin/status` reports the loaded counts and `POST /admin/reload`
re-reads the folder.

> A real `heimdall.overrides.json` holds actual subscription keys and secret values, so keep it out of
> version control (`.gitignore` it). The admin API is unauthenticated; leave it off
> (`Heimdall:EnableAdminApi=false`, the default) for anything network-accessible - it is a local-dev aid.

## The console

A web console served by the gateway itself (same container) at **`/_apim/`** (e.g.
`http://localhost:8080/_apim/`). It mirrors APIM's mental model - the
**Frontend → Inbound → Backend → Outbound** policy canvas - but adds what Azure can't do locally:

- **Live request tracing** - click a request, watch which policies fired, branch decisions, header
  and body transforms (before → after), evaluated `@()` expression results, rate-limit/cache hits,
  the resolved backend, and the final status.
- **Config explorer** - browse APIs, operations, products, subscriptions, named values, and the
  flattened effective policy per operation.
- **Request playground** - compose or **import a Postman / `.http` collection**, fire it through the
  gateway, and watch the trace light up.
- **Policy authoring** - edit policy XML in-browser and hot-reload.

Set `Heimdall:EnableConsole=false` to run the gateway **headless** - the whole `/_apim` console
surface (SPA, admin/authoring/playground APIs, and SignalR hub) goes offline - for CI and
data-plane-only deployments. The gateway, policies, and request tracing keep working; only the
console is off.

## Supported policies (tier 1)

Control flow `choose/when/otherwise`, `set-variable`, `include-fragment` · Transform `set-header`,
`set-body`, `set-method`, `rewrite-uri`, `set-query-parameter`, `set-backend-service`,
`find-and-replace` · Auth/security `validate-jwt`, `check-header`, `ip-filter`, `cors` · Rate/quota
`rate-limit`, `rate-limit-by-key`, `quota`, `quota-by-key` · Routing/response `forward-request`,
`return-response`, `mock-response`, `set-status` · Caching `cache-lookup`/`cache-store` (+ value
variants) · Subscription keys · Named values `{{name}}`.

Unsupported policies **fail loudly** (a clear error) - never silently skipped.

## Scope & non-goals

**In scope:** the APIM **data plane** for the most-used policies, fully offline, behaviorally
faithful, with the trace console.

**Not in scope:** the Azure **management plane** / portal, the developer portal, analytics/billing,
multi-region topology, and exotic policies (GraphQL resolvers, LLM/semantic-caching, WebSockets).
Heimdall emulates how APIM *behaves*, not how you *manage* Azure.

## How it works

ASP.NET Core host · **YARP** for backend forwarding · **Roslyn** to compile and cache policy
expressions · a custom policy engine that flattens global → product → API → operation policies
(resolving `<base/>`) and runs the four-stage pipeline. Config loads through a pluggable
`IConfigLoader` (policy XML + OpenAPI, or an APIOps v6 extractor folder). See [`docs/`](docs/) for the
full design.

## Status

Pre-release, built milestone by milestone.

**Done:** solution scaffold (M0) · the walking skeleton - host/path/method routing with URI-template
matching, YARP backend forwarding, and `<base/>` policy flattening (M1) · the Roslyn expression
engine - compiled + cached `@(...)`/`@{...}` evaluation against the APIM `context.*` surface, with the
Newtonsoft seam locked by an architecture test (M2) · tier-1 policy breadth (M3) - the four-stage
pipeline executes ~25 policies (transforms, routing, control flow, rate-limit/quota, caching, auth incl.
`validate-jwt`) over a buffered, policy-aware forward, with `{{named-value}}` substitution and
`include-fragment` inlined at flatten time · the resource / subscription-key model (M4) - products,
subscriptions, and named values load from config; the gateway validates the
`Ocp-Apim-Subscription-Key` header (then `subscription-key` query) before the pipeline, rejecting
missing/invalid keys with APIM's verbatim `401` body, stripping the key before forwarding, and
pulling product-scope policies into the effective policy only for product-scoped access · depth &
conformance (M5) - `context.RequestId`/`Timestamp`, deeper `validate-jwt` (query-param token,
clock-skew, require-scheme, required-claims, key rotation), `on-error` fault handling, opt-in
request-body buffering (bodies stream to the backend unless a policy re-reads them), and a
table-driven conformance suite (C01-C22) asserting parity with documented APIM semantics · the APIOps
loader (M6) - a second `IConfigLoader` that reads an APIOps **v6** extractor folder and produces the
same canonical config as the XmlOpenApi loader (proven by a parity test), with a `heimdall.overrides.json`
escape hatch for the secrets the extractor cannot export (subscription keys, secret named values),
loader-selection by config, and an opt-in `/admin/status` + `/admin/reload` API.

**Done:** the console (M7), completing the build. The observability/admin API is up - a bounded request-trace ring
buffer, a `/_apim` surface (masked config, effective policy, recent traces) and a SignalR live trace
feed - plus the playground backend: import a Postman v2.1 export or `.http` file into replayable
requests (replay-only, scripts flagged not run, fail-loud on non-v2.1) and replay one through the live
gateway to produce a correlated trace. The embedded SPA shell + design system is now in: a Vite +
React + TypeScript + Tailwind + shadcn/ui console served from `/_apim` (themed to the approved
design, with a Cmd/Ctrl+K command palette and the navigation chrome), built into the image by a node
stage in the Dockerfile and served as static files behind the gateway. The first data surface is now
live: a config explorer that reads `/_apim/config` to browse loaded APIs (drilling into operations and
their flattened effective policy), products, subscriptions, named values (secrets masked), backends,
and policy fragments, with an overview of config counts and gateway health. Sidebar and topbar counts
are driven by the live config rather than placeholders. The live trace canvas is in too: an app-wide
SignalR client streams each request from `/_apim/hub/traces` (with a REST backfill so the feed is
never empty) onto the Frontend/Inbound/Backend/Outbound pipeline, which lights up per the selected
trace; a live feed auto-follows the newest request until you pin one, a per-trace timeline shows each
stage's policies and timings, and a metrics strip (requests/60s, p50/p95 latency, status mix,
rate-limited %, cache-hit ratio) is computed client-side from the traces themselves, never faked.
The request playground surface is in too: import a Postman v2.1 export or a `.http` file, pick a
request, edit its method, URL, headers, and body, then replay it through the live gateway and read
the response, with a link to the correlated trace on the live canvas (import and per-request caveats
are shown loudly). Tokens a Postman script would set at runtime stay unresolved on import; a
collection-wide variables panel lets you supply them once and substitutes them into the request on
replay. The Overview now opens on a live recent-activity panel (a metric summary plus the
newest requests) driven by the same trace feed. In-browser policy authoring is in too: pick a scope
(global, API, operation, or product), edit its policy XML, and save to validate and hot-reload it into
the live gateway in memory (no file is written; a restart resets to the on-disk config). An
unsupported policy or malformed XML fails the save loudly without swapping; a built-in replay then
fires a request and links the resulting trace to the live canvas, so you see the change immediately.
A headless/CI switch (`Heimdall:EnableConsole=false`) takes the whole console surface offline for
data-plane-only deployments. A browser-driven Playwright UI E2E suite (xUnit + Microsoft.Playwright)
walks the console across every surface as the single end-to-end regression net; it is opt-in
(`HEIMDALL_E2E=1`) so the default `dotnet test` stays browserless.

**Next:** the pre-release launch gates - the data plane and console are feature-complete.

## Contributing

This is a focused, opinionated project built in spare time - no SLA. Issues and discussion are
welcome; PRs are accepted with tests and conformance coverage. Please keep the scope lean and the
fidelity honest.

**Running the tests.** `dotnet test` runs the full unit + conformance + HTTP-level suite browserless
in seconds; the browser UI E2E specs are skipped by default. To run them, build the console and the
Playwright browsers first, then opt in:

```bash
cd src/Heimdall.Ui && npm run build && cd -          # emit wwwroot/console the E2E host serves
pwsh tests/Heimdall.Tests/bin/Debug/net10.0/playwright.ps1 install chromium
HEIMDALL_E2E=1 dotnet test                            # boots a real host on a free port, drives Chromium
```

The E2E suite boots the Api out-of-process (a real Kestrel serving the built SPA) with a WireMock
stub backend, so it needs no external services. In CI it belongs in its own pre-merge job that caches
the Playwright browsers; the default unit job stays browserless via the same `HEIMDALL_E2E` gate.

## Disclaimer

Heimdall is an **independent, unofficial emulator for** Azure API Management. It is **not affiliated
with, endorsed by, or sponsored by Microsoft.** "Azure" and "Azure API Management" are trademarks of
Microsoft Corporation, used here only to describe compatibility.

## License

[MIT](LICENSE) © 2026 Florin Bobis
