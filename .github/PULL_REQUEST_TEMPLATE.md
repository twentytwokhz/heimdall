## What and why

<!-- What does this change do, and why? Link any related issue. -->

## Checklist

- [ ] One focused change, within Heimdall's scope (the APIM data plane / console).
- [ ] Tests added or updated; for policy behavior, conformance coverage added or extended.
- [ ] `dotnet test` passes. If the console changed, the E2E suite (`HEIMDALL_E2E=1`) was run locally.
- [ ] Unsupported policies fail loudly (no silent skips).
- [ ] New config formats are `IConfigLoader` adapters with a parity test, not engine changes.
- [ ] Writing style: no em-dashes, no filler. Commit messages describe the change only.
