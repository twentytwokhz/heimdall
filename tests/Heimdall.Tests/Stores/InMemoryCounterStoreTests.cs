using Heimdall.Infrastructure.Stores;
using Heimdall.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace Heimdall.Tests.Stores;

public class InMemoryCounterStoreTests
{
    private static readonly TimeSpan Minute = TimeSpan.FromSeconds(60);

    [Fact]
    public void Increments_within_the_same_window()
    {
        var store = new InMemoryCounterStore(new FakeClock(DateTimeOffset.UnixEpoch));

        store.Increment("k", Minute).Count.ShouldBe(1);
        store.Increment("k", Minute).Count.ShouldBe(2);
    }

    [Fact]
    public void Resets_when_the_window_rolls_over()
    {
        var clock = new FakeClock(DateTimeOffset.UnixEpoch);
        var store = new InMemoryCounterStore(clock);

        store.Increment("k", Minute);
        clock.Advance(Minute);

        store.Increment("k", Minute).Count.ShouldBe(1);
    }

    [Fact]
    public void Keys_are_counted_independently()
    {
        var store = new InMemoryCounterStore(new FakeClock(DateTimeOffset.UnixEpoch));

        store.Increment("a", Minute);
        store.Increment("a", Minute);

        store.Increment("b", Minute).Count.ShouldBe(1);
    }

    [Fact]
    public void Reset_time_is_the_end_of_the_aligned_window()
    {
        var store = new InMemoryCounterStore(new FakeClock(DateTimeOffset.UnixEpoch));

        var state = store.Increment("k", Minute);

        state.ResetsAt.ShouldBe(DateTimeOffset.UnixEpoch + Minute);
    }
}
