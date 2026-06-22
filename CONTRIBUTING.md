# Contributing to Heimdall

Thanks for your interest. Heimdall is a focused, opinionated project built in spare time, with no SLA.
Issues and discussion are welcome; PRs are accepted when they come with tests and (for policy behavior)
conformance coverage. Please keep the scope lean and the fidelity honest.

## Prerequisites

- .NET 10 SDK
- Node 22+ (only for the console SPA)
- Docker or Podman (optional, for the container path)

## Build and run

```bash
# Run the gateway against a sample config directory
Heimdall__ConfigPath=$PWD/samples dotnet run --project src/Heimdall.Api
# Console at http://localhost:8080/_apim/

# Console dev loop (hot reload): two terminals
Heimdall__ConfigPath=$PWD/samples dotnet watch run --project src/Heimdall.Api   # terminal A
cd src/Heimdall.Ui && npm install && npm run dev                                # terminal B -> :5173/_apim/
```

## Tests

```bash
dotnet test                 # full unit + conformance + HTTP-level suite, browserless, seconds
```

The browser UI E2E specs are gated off by default. To run them, build the console and the Playwright
browsers first, then opt in:

```bash
cd src/Heimdall.Ui && npm run build && cd -
pwsh tests/Heimdall.Tests/bin/Debug/net10.0/playwright.ps1 install chromium
HEIMDALL_E2E=1 dotnet test
```

CI mirrors this split: a browserless `unit` job on every push and PR, and a PR-only `e2e` job that
builds the SPA, installs Chromium, and runs the suite with `HEIMDALL_E2E=1`.

## Conventions

- **Test-driven.** Write the failing test first, then the code. Data-plane behavior is asserted against
  documented APIM semantics (see the conformance suite, C01-Cnn).
- **Unsupported policies fail loudly.** A policy Heimdall does not support must raise a clear error,
  never be silently skipped.
- **Canonical model, pluggable loaders.** New config formats (Terraform, ARM, Bicep, ...) are
  `IConfigLoader` adapters with their own parity test, never engine changes.
- **Writing style.** No em-dashes (use a comma, colon, parentheses, or a spaced hyphen). No filler or
  hype words. State things plainly. Applies to code comments, docs, and commit messages.
- **Commits** describe the change only.

## Pull requests

- One focused change per PR. Keep the diff surgical and within Heimdall's scope (the APIM data plane).
- Include tests; for policy behavior, add or extend conformance coverage.
- `dotnet test` must pass. If you touched the console, run the E2E suite locally.
- See [`docs/`](docs/) (`PRD.md`, `IMPLEMENTATION.md`) for product scope and technical design.

## License

By contributing you agree your contributions are licensed under the [MIT License](LICENSE).
