using Heimdall.Application;

namespace Heimdall.Tests.Fixtures;

/// <summary>A controllable <see cref="IClock"/> so rate-limit/quota windows are deterministic in tests.</summary>
public sealed class FakeClock(DateTimeOffset start) : IClock
{
    public DateTimeOffset UtcNow { get; set; } = start;

    public void Advance(TimeSpan by) => UtcNow += by;
}
