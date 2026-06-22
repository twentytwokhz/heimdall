# Product Requirements Document: Heimdall - Local APIM Emulator

---

## Document Control

| Field | Value |
|-------|-------|
| **Version** | 1.0 |
| **Date** | 2026-06-20 |
| **Author** | Florin Bobis |
| **Reviewers** | TBD |
| **Status** | Draft |
| **Project Code** | APIM-EMU |

**Revision History:**

| Version | Date | Author | Changes |
|---------|------|--------|---------|
| 1.0 | 2026-06-20 | Florin Bobis | Initial draft from brainstorming + research |

---

## Executive Summary

**What are we building?** A local, fully-offline emulator of the Azure API Management (APIM)
data plane - packaged as a Docker container - that developers point their applications at
(`http://localhost:<port>`) instead of a real cloud APIM instance. It loads your real APIM
artifacts (policy XML + OpenAPI), executes the policy pipeline with **real C# expression
evaluation**, enforces subscription keys, and forwards to your real backend - so your app
cannot tell it apart from production Azure for the supported feature set.

**Why does it matter?** APIM has no first-party offline emulator. Today every policy change
requires a live instance (30-40 minutes to provision, $50-$2,800/month per tier), a deploy,
and a manual request to verify. Microsoft concedes this slow loop in its own Policy Toolkit
README. The self-hosted gateway is *not* a local solution - it is data-plane only and
**requires the cloud instance** for configuration sync, so it cannot run offline. The result
is a painful inner loop, expensive non-prod environments, and brittle local integration for
any app that sits behind APIM.

**Who benefits?** Backend developers authoring and debugging policies; application/frontend
developers who need APIM behavior available offline to run their app end-to-end; and teams
who want to assert policy behavior automatically in CI without a cloud instance.

**Key outcomes?** An app configured against real APIM runs unchanged against the emulator for
the supported policy set; the policy edit→test loop drops from minutes (deploy + request) to
under ~2 seconds; and a single `docker run` produces a working gateway with no Azure account,
no provisioning wait, and no per-environment cost.

---

## Business Context

### Problem Statement

**Current Situation:**
There is no mature, fully-offline, full-fidelity way to run or test APIM locally. Developers
must use a live Azure instance for any meaningful validation of policies, subscription-key
behavior, rate limiting, or request/response transformation. Concretely:

- **No offline emulator.** Azure ships local emulators for Cosmos DB, Service Bus, Storage,
  and App Configuration - but not for API Management.
- **Slow provisioning.** Dedicated-tier instances take 30-40 minutes to create (by design),
  and are routinely reported stuck "Activating" for hours; VNet updates can hang 2-4 hours.
- **Slow policy loop.** Per Microsoft's Policy Toolkit README, the feedback loop on even the
  smallest policy change requires "a live Azure API Management instance, a policy document
  deployment, and manual testing through the API request."
- **Gated, flaky debugging.** Live policy debugging is restricted to Developer/Premium tiers,
  and the VS Code "live debug" experience has unresolved reliability issues.
- **The self-hosted gateway is not local.** It is data-plane only, needs outbound connectivity
  to the cloud instance for config sync (every ~10s), cannot bootstrap offline, and silently
  skips unsupported policies.

**Cost of Inaction:**
- Each developer needs access to a shared or personal cloud APIM instance just to test policy
  changes - recurring cost (Developer ~$50/mo non-prod only; Basic ~$150; Standard ~$700;
  Premium ~$2,800/mo per unit) and contention on shared environments.
- Minutes-long edit→deploy→test loops multiplied across every policy iteration.
- Local app development is blocked or faked whenever the app depends on APIM behavior
  (subscription keys, header injection, rewrites), because there is nothing faithful to run.

**Opportunity:**
- Eliminate the cloud round-trip for the inner loop: edit a policy, send a request, see the
  result in seconds, fully offline.
- Remove per-developer non-prod APIM cost for everyday work.
- Make APIM-dependent apps runnable offline and in CI, with behavior that matches production.

---

### Success Metrics

| Metric | Baseline | Target | Timeline |
|--------|----------|--------|----------|
| Policy edit→test loop time | Minutes (deploy + manual request) | < 2 seconds | M3 |
| Time from zero to running gateway | 30-40 min (provision a cloud instance) | < 1 min (`docker run`) | M1 |
| Supported tier-1 policies running end-to-end | 0 (no offline option) | ~20 policies | M3 |
| App behavioral parity (supported set) | N/A | App runs unchanged vs. real APIM | M5 |
| Offline operation | Not possible (self-hosted gw needs cloud) | 100% offline, no Azure account | M1 |
| Recurring non-prod cost for local dev | $50-$2,800/mo | $0 | M1 |

---

### Stakeholders

| Role | Involvement | Decision Authority |
|------|-------------|-------------------|
| Project owner (Florin) | Direction, scope, review | Final approval |
| Backend/policy developers | Primary users, feedback | Feature priorities |
| App/frontend developers | Primary users (integration) | Integration ergonomics |

---

## User Personas

### Persona 1: Backend / Policy Developer (Primary)

**Context:**
- Authors and maintains APIM policy XML (rate limits, JWT validation, header transforms).
- Today: edits policy, deploys to a cloud instance, sends a request, repeats. Slow and costly.

**Goals:**
1. Edit a policy and see its effect on a real request within seconds, offline.
2. Reproduce and debug policy behavior locally without a Developer/Premium instance.

**Pain Points:**
1. 30-40 min provisioning and minutes-per-iteration deploy loops.
2. Policy debugging gated to specific tiers and unreliable in VS Code.
3. Cryptic XML/expression errors only surfaced after deploy.

**Success Criteria:**
- Copy real policy XML from the portal/repo, run it locally, get identical behavior.
- Sub-2-second feedback on a change.

---

### Persona 2: Application / Frontend Developer (Primary)

**Context:**
- Builds an app that sits behind APIM and depends on its behavior (subscription keys injected,
  headers rewritten, backend URL set by `set-backend-service`).
- Today: cannot run the full stack offline; must point at a shared cloud APIM or stub it badly.

**Goals:**
1. Run the whole app offline with APIM behavior in front of the real backend.
2. Not change app code or config between "local" and "real APIM".

**Pain Points:**
1. Local dev blocked or inaccurate when APIM is in the path.
2. Shared cloud instances cause contention and unexpected cost.

**Success Criteria:**
- App sends `Ocp-Apim-Subscription-Key` and gets the same accept/reject behavior as prod.
- Same `401`/`429` shapes, same header/body transforms, then forwarded to the real backend.

---

### Persona 3: Team / CI (Secondary)

**Context:**
- Wants automated assertions that policies behave as intended, in pipelines, without a cloud
  instance.

**Goals:**
1. Spin up the emulator in CI and assert policy behavior (e.g., key required, rate limit hit).

**Success Criteria:**
- Deterministic, offline, container-based runs with no Azure dependency.

---

## Functional Requirements

> Structured as Epics → Features. Priorities use MoSCoW (P0 = Must, P1 = Should, P2 = Could).
> Phasing maps to the milestones in `docs/IMPLEMENTATION.md`.

---

### Epic 1: Configuration Loading (Priority: P0)

**Description:** Load APIM configuration from real artifacts into a canonical internal model.

**Business Value:** The closer the input is to "the exact artifacts Azure consumes," the more
useful the emulator. A canonical model + pluggable loaders means new formats add zero engine
changes.

#### Feature 1.1: Raw policy XML + OpenAPI loader (P0)
As a developer, I want to point the emulator at a directory of policy XML files and an OpenAPI
spec, so that my APIs/operations and their policies are loaded with no manual translation.
- Given a directory with an OpenAPI document and policy XML at global/API/operation scope, when the emulator starts, then all APIs, operations, and policies are loaded.
- Policy XML is preserved verbatim (parsed, not rewritten).
- Invalid/missing files produce a clear startup error pointing at the file.

#### Feature 1.2: APIOps folder-layout loader (P1)
As a team, I want the emulator to consume our existing APIOps extractor folder layout, so that
we reuse what is already in git with zero manual work.
- Given an APIOps extract (apis/, policies/, products/, named-values/, subscriptions/), when the emulator starts, then the same canonical model is produced as the XML+OpenAPI loader.

#### Feature 1.3: IaC loaders - Terraform / ARM / Bicep (P2)
As a team whose source of truth is IaC, I want the emulator to extract config from Terraform
(`azurerm` provider), ARM, or Bicep, so that I do not maintain a separate config.
- Terraform: parse `azurerm_api_management_*` resources / `terraform show -json`, extracting `xml_content` policies into the canonical model.
- ARM/Bicep: extract embedded policy XML and resources.

#### Feature 1.4: Live-Azure import (P2)
As a developer, I want to seed the local config from an existing APIM instance once, so that I
can mirror production locally.
- Given Azure credentials, when I run the import, then the canonical model is populated from the live instance.

---

### Epic 2: Policy Pipeline Engine (Priority: P0)

**Description:** Execute the APIM request pipeline (inbound → backend → outbound → on-error)
with real C# expression evaluation and correct scope inheritance.

**Business Value:** This is the core differentiator. Fidelity here is what makes the emulator
indistinguishable from real APIM.

#### Feature 2.1: Pipeline execution model (P0)
As a developer, I want requests to flow through the four policy stages exactly as APIM does, so
that ordering and short-circuit behavior match production.
- Inbound runs before forwarding; outbound runs on the response; exceptions jump to on-error.
- `return-response`/`mock-response` short-circuit forwarding entirely.
- A policy error skips the rest of its section and routes to on-error.

#### Feature 2.2: C# expression evaluation via Roslyn (P0)
As a developer, I want `@(...)` and `@{...}` policy expressions evaluated as real C#, so that
expressions behave exactly as in Azure.
- Expressions are compiled and cached; the `context` object exposes Request/Response/Variables/User/Subscription/Product/Api/Operation/Deployment/LastError.
- `Newtonsoft.Json` is available **inside policy expressions** (`JObject`/`JToken`/`JsonConvert`, `context.Request.Body.As<JObject>()`), matching Azure. (The emulator's own code uses System.Text.Json; Newtonsoft is scoped to the expression sandbox.)

#### Feature 2.3: Scope inheritance + `<base/>` flattening (P0)
As a developer, I want global/product/API/operation policies combined with `<base/>` exactly as
APIM does, so that effective policy matches production.
- `<base/>` inserts the parent scope's policies at the correct position.
- API-scoped / all-APIs / all-access subscriptions bypass product-scoped policies (documented quirk).

#### Feature 2.4: Tier-1 policy set (breadth) (P0)
As a developer, I want the ~20 most-used policies to work end-to-end, so that most real APIs run.
- Control flow: `choose/when/otherwise`, `set-variable`, `include-fragment`.
- Transform: `set-header`, `set-body`, `set-method`, `rewrite-uri`, `set-query-parameter`, `set-backend-service`, `find-and-replace`.
- Auth/security: `validate-jwt`, `check-header`, `ip-filter`, `cors`.
- Rate/quota: `rate-limit`, `rate-limit-by-key`, `quota`, `quota-by-key`.
- Routing/response: `forward-request`, `return-response`, `mock-response`, `set-status`.
- Caching: `cache-lookup`/`cache-store` (and `-value` variants).
- Named values: `{{name}}` substitution.

#### Feature 2.5: Depth pass + conformance (P1)
As a developer, I want edge cases and accuracy hardened, so that fidelity is trustworthy.
- Complete `context.*` surface; rate-limit accuracy with `429` + `Retry-After`.
- `validate-jwt` against local JWKS/keys; conformance suite vs. documented APIM semantics.

---

### Epic 3: Resource & Auth Model (Priority: P0)

**Description:** Model Subscriptions, Products, Subscription Keys, and Named Values, and enforce
subscription-key authentication.

#### Feature 3.1: Subscription-key enforcement (P0)
As an app developer, I want the emulator to validate `Ocp-Apim-Subscription-Key`, so that my app
authenticates exactly as it does against real APIM.
- Key accepted via `Ocp-Apim-Subscription-Key` header or `subscription-key` query param (header checked first).
- Missing/invalid key returns `401` with the same shape as APIM.
- Product/API/subscription scoping respected.

#### Feature 3.2: Named values (P0)
As a developer, I want `{{named-value}}` references resolved, so that policies relying on named
values run unchanged.
- Named values are loaded and substituted at the right points.

---

### Epic 4: Gateway Runtime & Delivery (Priority: P0)

**Description:** A YARP-based ASP.NET Core gateway, packaged as a Docker container, that forwards
to real backends.

#### Feature 4.1: Backend forwarding via YARP (P0)
As an app developer, I want passed requests forwarded to my real backend, so that the full path
works offline.
- Destination honors `set-backend-service`; default backend is configurable.
- Request/response streaming and headers handled correctly.

#### Feature 4.2: Docker delivery (P0)
As a developer, I want one command to run the emulator, so that setup is trivial and CI-friendly.
- `docker run` (config mounted as a volume) starts a working gateway.
- A `docker-compose.yml` runs emulator + sample backend.

---

### Epic 5: Local Console UI - "Heimdall" (Priority: P1)

**Description:** A web console served by the gateway itself (embedded SPA, same container) that
mirrors Azure APIM's mental model - left-nav resource model + the Frontend→Inbound→Backend→Outbound
policy canvas - but with a far better, modern design, and adds the live request tracing Azure can't
do locally. Built with the `frontend-design` skill.

#### Feature 5.1: Observability + debug console (P1)
As a developer, I want a live request feed and a per-request execution trace on the four-stage
canvas, so I can see exactly what every policy did.
- Live feed (SignalR); click a request → trace shows which policy chips fired, branch decisions (`choose/when`), header/body before→after, evaluated `@()` results, rate-limit/cache hits, resolved backend, final status.

#### Feature 5.2: Config explorer - read-only (P1)
- Browse APIs/operations/products/subscriptions/named values/backends + the flattened **effective policy** per operation, with scope provenance.

#### Feature 5.3: Request playground (P1)
- Compose + fire a request (method/path/headers/body, with/without subscription key); watch the trace light up live.

#### Feature 5.4: Policy authoring + hot-reload (P2)
As a developer, I want to edit policy XML in-browser and hot-reload, so the inner loop is instant.
- Edit the **loaded local** policy XML, save to the config dir, hot-reload, re-trace. **Boundary:** local config only - not the Azure management plane, developer portal, or IaC write-back.

---

## Non-Functional Requirements

### Performance
- Policy edit→test loop: < 2 seconds (config reload + single request).
- Cold start to ready: target < ~5 seconds for a typical config.
- Expression compilation cached so repeated requests do not recompile.

### Fidelity (the defining NFR)
- For the supported policy set, behavior must be **indistinguishable** from real APIM at the
  data plane: status codes, headers, body transforms, key handling, and error shapes.
- Unsupported policies must fail **loudly** (clear log/startup error), never silently skipped
  (explicitly the opposite of the self-hosted gateway's behavior).

### Offline & Portability
- 100% offline; no Azure account or outbound connectivity required to run.
- Single container image; Linux-based; runnable on developer machines and CI.

### Security
- Local dev tool: secrets (named values, keys) are loaded from local config only.
- JWT validation uses locally provided keys/JWKS; no calls to external identity providers
  required for the offline path.

### Observability
- Per-request trace of policy execution (which policies ran, decisions, transforms) to aid
  debugging - the local equivalent of APIM trace, available without tier gating.

### Compatibility
- Accepts APIM policy XML as-authored (verbatim), so config can be copy-pasted from Azure.

---

## Constraints & Assumptions

### Technical Constraints
**Must use:** .NET 10 (LTS) / C# (policy expressions are C#; Roslyn gives fidelity); YARP for
forwarding; Docker for delivery. System.Text.Json for the emulator's own code; Newtonsoft.Json
only inside the policy-expression sandbox (Azure parity).

### Assumptions

| ID | Assumption | Confidence | Impact if Invalid |
|----|-----------|-----------|-------------------|
| A-001 | Roslyn scripting can faithfully evaluate the documented expression surface | High | Lower fidelity on some expressions; interpret subset |
| A-002 | YARP integrates cleanly around the four policy stages | Medium | More custom forwarding code |
| A-003 | The documented `context.*` surface is stable enough to mirror | High | Ongoing maintenance as APIM evolves |
| A-004 | Tier-1 policy set covers the majority of real-world APIs | Medium | Expand breadth sooner |

---

## Out of Scope (v1.0)

- ❌ APIM **management plane** / Azure portal / management REST API / ARM-Terraform write-back *(the console's policy authoring edits the **loaded local config** + hot-reloads - it does not manage Azure resources or the developer portal)*
- ❌ Developer portal
- ❌ Analytics, billing, and reporting
- ❌ Multi-region / high-availability topology
- ❌ Tier-2 / exotic policies: `validate-content`/`validate-parameters`, `send-request`/`send-one-way-request`, `retry`, `wait`, managed-identity auth, GraphQL resolvers/validation, WebSockets, LLM/semantic-caching policies
- ❌ External/Redis-backed shared state (in-memory only in v1; interfaces leave room for it)

**Rationale:** v1 targets the **data plane** for the **most-used policies**, fully offline. The
management plane and exotic policies add large surface area for little inner-loop value.

---

## Open Questions

- **Q1:** Project/binary name - keep `apim-emulator`, or a friendlier brand (e.g. `localapim`)? [@Florin]
- **Q2:** Should the per-request policy trace be on by default or opt-in via header/env? [@Florin]
- **Q3:** For `validate-jwt`, what is the preferred way to supply keys locally (static PEM/JWK file vs. a local JWKS endpoint)? [@Florin]
- **Q4:** Confirm the exact tier-1 policy list against the policies your real APIs actually use. [@Florin]

---

## Appendices

### Appendix A: Glossary

| Term | Definition |
|------|------------|
| **APIM** | Azure API Management |
| **Data plane** | The gateway that processes API traffic (vs. the management plane that configures it) |
| **Policy** | An XML-defined rule executed in the request pipeline (e.g. `rate-limit`, `validate-jwt`) |
| **Policy expression** | A `@(...)`/`@{...}` C# snippet embedded in policy XML |
| **`context`** | The object exposed to policy expressions (Request, Response, Subscription, etc.) |
| **`<base/>`** | Element that injects the parent scope's policies into a child scope |
| **Named value** | A reusable, optionally-secret config value referenced as `{{name}}` |
| **Self-hosted gateway** | Azure's containerized data-plane gateway - requires the cloud instance; not offline |
| **APIOps** | A common folder structure / workflow for extracting and deploying APIM config from git |
| **YARP** | Yet Another Reverse Proxy - Microsoft's .NET reverse proxy library |
| **Roslyn** | The .NET compiler platform; used here to compile/evaluate policy expressions |

### Appendix B: Reference Documents

- Implementation design: `docs/IMPLEMENTATION.md`
- Research: APIM pain-point report and feature-surface reference

---

*Document Version: 1.0 - Next review after stakeholder feedback.*
