# Security Policy

Heimdall is a **local-development emulator**, not a hosted service or a production gateway. It is meant
to run on a developer's machine or in CI, offline. Keep that threat model in mind.

## Reporting a vulnerability

Please report security issues privately, not in a public issue. Use GitHub's
**[private vulnerability reporting](https://github.com/twentytwokhz/heimdall/security/advisories/new)**
(Security tab -> Report a vulnerability). I will acknowledge and respond as time allows; there is no SLA.

## Known local-dev caveats (by design, not vulnerabilities)

- **The admin API is unauthenticated.** Leave it off (`Heimdall:EnableAdminApi=false`, the default) for
  anything network-accessible. It is a local-dev aid.
- **The console and its APIs are unauthenticated** (config explorer, playground, authoring, SignalR
  hub). Disable the whole surface with `Heimdall:EnableConsole=false` for data-plane-only deployments.
- **`heimdall.overrides.json` holds real subscription keys and secret named values.** Keep it out of
  version control. Heimdall masks secrets in the console, but the file on disk is plaintext.

Do not expose a Heimdall instance to an untrusted network with the console or admin API enabled.
