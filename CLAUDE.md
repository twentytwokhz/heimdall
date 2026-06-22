# Heimdall - Project Guide

**Heimdall** (codename + product brand; the all-seeing watchman of the gateway bridge) - a local,
offline, full-fidelity emulator of the Azure API Management (APIM) **data plane**, with an embedded
observability/debug **console**. .NET projects are namespaced `Heimdall.*`; repo dir stays `apim-emulator`.
.NET / C# · ASP.NET Core · YARP · Roslyn · Docker. See `docs/PRD.md` and
`docs/IMPLEMENTATION.md` for product scope and technical design.

## Commit & PR rules

- **Never** add Claude attribution to commits or PRs - no `Co-Authored-By: Claude …` trailer,
  no "Generated with Claude Code" line. Enforced via `.claude/settings.json`
  (`attribution.commit: ""`, `attribution.pr: ""`, `includeCoAuthoredBy: false`).
- Commit messages describe the change only.
- **Commit often** - small, focused commits at each meaningful green step (a passing batch,
  a policy, a test group), not one big drop per phase.
- **Never commit during the Plan step.** Commits start in Execute (step 2). Do not commit spec,
  plan, or design artifacts - the plan lives in the harness plan file, not the repo. Final commits
  land in step 4 after Review passes.
- **Per-phase loop (required)** - every implementation phase (`docs/IMPLEMENTATION_PLAN.md`,
  Phase 0→7) follows this exact loop:
  1. **Plan** - enter plan mode, write the phase plan, get user approval before any code. No commits.
  2. **Execute** - implement against the approved plan (TDD, commit-often within the phase).
  3. **Review** - code-review the phase diff; address findings.
  4. **Commit** - finalize commits once exit criteria pass AND review is clean.
  A phase is done only when all four steps are complete.

## Tooling

- **code-index MCP** (`.mcp.json`) - index and navigate the codebase for symbol-level search
  and exploration instead of brute-force file reads. Use it when exploring or reviewing.
- **rtk** - Bash commands are routed through `rtk` automatically (global hook); no action needed.

## Conventions

- Canonical model + pluggable `IConfigLoader`s - new config formats (Terraform/ARM/Bicep) are
  adapters, never engine changes.
- Unsupported policies must **fail loudly** (clear error), never be silently skipped.

## Docs

- **Keep `docs/` clean.** Update only the original files (`PRD.md`, `IMPLEMENTATION.md`,
  `IMPLEMENTATION_PLAN.md`, `perf-baseline.md`). Do not add new files or subfolders under `docs/`.
  Plans live in the harness plan file; transient handoff notes live in `.handoff/` (gitignored).

## Writing style

- **No em-dashes.** Use a comma, colon, parentheses, or a spaced hyphen ( - ) instead. Applies to
  code comments, docs, READMEs, commit messages, and UI copy.
- **No AI fluff.** Cut filler and hype words (seamless, robust, leverage, "in today's..."). State
  things plainly; every sentence should carry information.
- **Emojis only when sensible.** A brand mark (the Heimdall shield) is fine; decorative emojis
  sprinkled through prose or headings are not.

## Confidentiality & licensing

- **No client or employer data - ever.** This is a generic product (Heimdall). Never put real
  prior client/employer names, endpoints, or domain terms in code,
  samples, demos, docs, or commits. Use the fictional **Acme Platform API** or a petstore stub.
- **License: MIT** - open-source. Heimdall is a credibility/portfolio asset, not a paid product.
- **Do NOT make the repo public, add a git remote, or push** until: (a) the employer IP /
  non-compete question is resolved, and (b) git history is scrubbed of any prior client data.
