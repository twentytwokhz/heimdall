using Heimdall.Application;
using Heimdall.Infrastructure.Tracing;
using Shouldly;
using Xunit;

namespace Heimdall.Tests.Tracing;

public class InMemoryTraceSinkTests
{
    private static RequestTrace Trace(Guid id) => new(
        id, DateTimeOffset.UnixEpoch, "GET", "/catalog/items",
        "acme", "Acme Platform API", "listCatalogItems", "GET",
        SubscriptionId: null, ProductId: null,
        StatusCode: 200, DurationMs: 1.0, TraceOutcome.Completed, Error: null,
        Stages: []);

    [Fact]
    public void Recent_returns_an_added_trace()
    {
        var sink = new InMemoryTraceSink();
        var trace = Trace(Guid.NewGuid());

        sink.Add(trace);

        sink.Recent(10).ShouldHaveSingleItem().ShouldBe(trace);
    }

    [Fact]
    public void Recent_orders_newest_first()
    {
        var sink = new InMemoryTraceSink();
        var first = Trace(Guid.NewGuid());
        var second = Trace(Guid.NewGuid());

        sink.Add(first);
        sink.Add(second);

        sink.Recent(10).Select(t => t.RequestId).ShouldBe([second.RequestId, first.RequestId]);
    }

    [Fact]
    public void Recent_caps_at_the_requested_limit()
    {
        var sink = new InMemoryTraceSink();
        for (var i = 0; i < 5; i++)
        {
            sink.Add(Trace(Guid.NewGuid()));
        }

        sink.Recent(2).Count.ShouldBe(2);
    }

    [Fact]
    public void Capacity_bounds_the_buffer_and_evicts_the_oldest()
    {
        var sink = new InMemoryTraceSink(capacity: 3);
        var traces = Enumerable.Range(0, 5).Select(_ => Trace(Guid.NewGuid())).ToList();

        foreach (var trace in traces)
        {
            sink.Add(trace);
        }

        var recent = sink.Recent(100);
        recent.Count.ShouldBe(3);
        // The two oldest were evicted; the three newest survive, newest-first.
        recent.Select(t => t.RequestId).ShouldBe([traces[4].RequestId, traces[3].RequestId, traces[2].RequestId]);
        sink.Get(traces[0].RequestId).ShouldBeNull();
    }

    [Fact]
    public void Get_finds_a_buffered_trace_by_id()
    {
        var sink = new InMemoryTraceSink();
        var trace = Trace(Guid.NewGuid());
        sink.Add(trace);

        sink.Get(trace.RequestId).ShouldBe(trace);
    }

    [Fact]
    public void Get_returns_null_for_an_unknown_id()
    {
        var sink = new InMemoryTraceSink();

        sink.Get(Guid.NewGuid()).ShouldBeNull();
    }

    [Fact]
    public void Add_raises_TraceAdded_with_the_trace_and_still_stores_it()
    {
        var sink = new InMemoryTraceSink();
        var trace = Trace(Guid.NewGuid());
        RequestTrace? observed = null;
        sink.TraceAdded += t => observed = t;

        sink.Add(trace);

        observed.ShouldBe(trace);                       // pushed to subscribers
        sink.Get(trace.RequestId).ShouldBe(trace);      // and still buffered
    }

    [Fact]
    public async Task Concurrent_adds_do_not_throw_and_stay_within_capacity()
    {
        var sink = new InMemoryTraceSink(capacity: 50);

        var tasks = Enumerable.Range(0, 16).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < 100; i++)
            {
                sink.Add(Trace(Guid.NewGuid()));
            }
        }));
        await Task.WhenAll(tasks);

        sink.Recent(1000).Count.ShouldBe(50);
    }
}
