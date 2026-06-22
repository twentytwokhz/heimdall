using Xunit;

namespace Heimdall.Tests.E2e.Browser;

/// <summary>
/// A <see cref="FactAttribute"/> that skips unless <c>HEIMDALL_E2E=1</c> is set in the environment.
/// The browser E2E suite drives a real Chromium against a real host, which needs a built console SPA
/// (<c>npm run build</c>) and installed Playwright browsers - too heavy for the default fast test run.
/// So the default <c>dotnet test</c> reports these as skipped; CI's dedicated e2e job flips the env on.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class E2eFactAttribute : FactAttribute
{
    public E2eFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("HEIMDALL_E2E") != "1")
        {
            Skip = "Browser E2E suite is opt-in. Set HEIMDALL_E2E=1 (and build the console + Playwright browsers) to run it.";
        }
    }
}
