using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Tracer.Core.Domain;
using Tracer.Core.Identity;
using Tracer.Core.Records;
using Tracer.Core.Time;
using Tracer.TestHarness.Observer;
using Tracer.WebApi.Queries;
using Xunit;

namespace Tracer.Tests.Unit.WebApi;

/// <summary>
/// Tests for <see cref="TraceQueryService"/> using real DuckDB storage.
/// </summary>
public sealed class TraceQueryServiceTests : IAsyncDisposable
{
    private readonly ObserverFixture _fixture;
    private readonly TraceQueryService _svc;

    private static readonly DateTimeOffset BaseTime =
        new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private static ulong _nextId = 800_000;

    private static WallclockTime At(DateTimeOffset dto) =>
        WallclockTime.FromUnixNanoseconds(dto.ToUnixTimeMilliseconds() * 1_000_000L);

    private EventRecord MakeEvent(ulong eventId, ulong traceId, ulong parentEventId = 0,
        DateTimeOffset? at = null, string node = "node-a")
    {
        return new EventRecord
        {
            SequenceNumber = eventId,
            PublishWallclock = At(at ?? BaseTime),
            ReceiveWallclock = At(at ?? BaseTime),
            PublisherNode = new AgentId(node),
            SubscriberNode = new AgentId(node),
            Topic = new TopicName("trace.query.test"),
            EventId = new EventId(eventId),
            TraceId = new TraceId(traceId),
            ParentEventId = parentEventId != 0 ? new EventId(parentEventId) : null,
            PayloadJson = "{}",
        };
    }

    public TraceQueryServiceTests()
    {
        _fixture = ObserverFixture.CreateAsync().GetAwaiter().GetResult();
        // TraceQueryService requires registration. Create it manually with the fixture's reader.
        var reader = _fixture.App.Services.GetRequiredService<Tracer.Storage.DuckDB.MultiInterval.LiveMultiIntervalReader>();
        _svc = new TraceQueryService(reader, Microsoft.Extensions.Logging.Abstractions.NullLogger<TraceQueryService>.Instance);
    }

    public async ValueTask DisposeAsync() => await _fixture.DisposeAsync();

    [Fact]
    public async Task GetTraceTree_NormalTrace_ReturnsNodesEdgesAndSummary()
    {
        // 10 events on one trace: 1 root + 9 children
        var traceId = _nextId++;
        var rootId = _nextId++;
        var events = new List<EventRecord> { MakeEvent(rootId, traceId, 0) };
        for (int i = 0; i < 9; i++)
        {
            var childId = _nextId++;
            events.Add(MakeEvent(childId, traceId, rootId, at: BaseTime.AddSeconds(i + 1)));
        }
        await _fixture.PushAsync(events);

        var tree = await _svc.GetTraceTreeAsync(traceId, maxEvents: 1000, CancellationToken.None);

        tree.Should().NotBeNull();
        tree!.Nodes.Should().HaveCount(10);
        tree.Edges.Should().HaveCount(9);
        tree.Summary.TotalEvents.Should().Be(10);
        tree.Summary.Truncated.Should().BeFalse();
        tree.Summary.RootCount.Should().Be(1);
        tree.Summary.LeafCount.Should().Be(9);
    }

    [Fact]
    public async Task GetTraceTree_ExceedsMaxEvents_ReturnsTruncatedResultWithFlagSet()
    {
        // 20 events on one trace; query with maxEvents=10
        var traceId = _nextId++;
        var rootId = _nextId++;
        var events = new List<EventRecord> { MakeEvent(rootId, traceId, 0) };
        for (int i = 0; i < 19; i++)
            events.Add(MakeEvent(_nextId++, traceId, rootId, at: BaseTime.AddSeconds(i + 1)));
        await _fixture.PushAsync(events);

        var tree = await _svc.GetTraceTreeAsync(traceId, maxEvents: 10, CancellationToken.None);

        tree.Should().NotBeNull();
        tree!.Summary.Truncated.Should().BeTrue("20 events exceeds maxEvents=10");
        tree.Nodes.Should().HaveCount(10);
    }

    [Fact]
    public async Task GetTraceTreeForEvent_EventWithTraceId_ReturnsSameResultAsDirectTraceCall()
    {
        var traceId = _nextId++;
        var rootId = _nextId++;
        var leafId = _nextId++;
        await _fixture.PushAsync(
        [
            MakeEvent(rootId, traceId, 0),
            MakeEvent(leafId, traceId, rootId, at: BaseTime.AddSeconds(1)),
        ]);

        var viaEvent = await _svc.GetTraceTreeForEventAsync(
            new EventId(leafId), maxEvents: 1000, CancellationToken.None);
        var directTrace = await _svc.GetTraceTreeAsync(
            traceId, maxEvents: 1000, CancellationToken.None);

        viaEvent.Should().NotBeNull();
        directTrace.Should().NotBeNull();
        viaEvent!.Nodes.Count.Should().Be(directTrace!.Nodes.Count);
        viaEvent.Edges.Count.Should().Be(directTrace.Edges.Count);
    }

    [Fact]
    public async Task GetTraceTreeForEvent_EventWithZeroTraceId_ReturnsSingletonTree()
    {
        // Event with trace_id = 0 (no trace context)
        var eventId = _nextId++;
        await _fixture.PushAsync(
        [
            new EventRecord
            {
                SequenceNumber = eventId,
                PublishWallclock = At(BaseTime),
                ReceiveWallclock = At(BaseTime),
                PublisherNode = new AgentId("node-a"),
                SubscriberNode = new AgentId("node-a"),
                Topic = new TopicName("trace.singleton"),
                EventId = new EventId(eventId),
                TraceId = new TraceId(0),       // zero trace ID
                ParentEventId = null,
                PayloadJson = "{}",
            }
        ]);

        var tree = await _svc.GetTraceTreeForEventAsync(
            new EventId(eventId), maxEvents: 1000, CancellationToken.None);

        tree.Should().NotBeNull();
        tree!.Nodes.Should().HaveCount(1, "singleton tree has exactly one node");
        tree.Edges.Should().BeEmpty("singleton has no edges");
        tree.Summary.Truncated.Should().BeFalse();
    }
}
