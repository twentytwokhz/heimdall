using Heimdall.Infrastructure.Stores;
using Heimdall.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace Heimdall.Tests.Stores;

public class InMemoryCacheStoreTests
{
    [Fact]
    public void Stores_and_retrieves_a_value()
    {
        var store = new InMemoryCacheStore(new FakeClock(DateTimeOffset.UnixEpoch));

        store.Set("k", "v", TimeSpan.FromSeconds(60));

        store.TryGet("k", out var value).ShouldBeTrue();
        value.ShouldBe("v");
    }

    [Fact]
    public void Missing_key_returns_false()
    {
        var store = new InMemoryCacheStore(new FakeClock(DateTimeOffset.UnixEpoch));

        store.TryGet("absent", out var value).ShouldBeFalse();
        value.ShouldBeNull();
    }

    [Fact]
    public void Entry_expires_after_its_duration()
    {
        var clock = new FakeClock(DateTimeOffset.UnixEpoch);
        var store = new InMemoryCacheStore(clock);
        store.Set("k", "v", TimeSpan.FromSeconds(60));

        clock.Advance(TimeSpan.FromSeconds(61));

        store.TryGet("k", out _).ShouldBeFalse();
    }
}
