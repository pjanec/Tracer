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

    [Fact]
    public async Task GetTraceTree_SessionIdResolved_MatchesSessionContainingFirstEvent()
    {
        // Arrange: push a session_start event, then trace events after it
        var sessionId = $"session-{_nextId++}";
        var traceId   = _nextId++;
        var rootId    = _nextId++;

        // Session start event at BaseTime - 10 seconds
        var sessionStart = new EventRecord
        {
            SequenceNumber   = _nextId++,
            PublishWallclock = At(BaseTime.AddSeconds(-10)),
            ReceiveWallclock = At(BaseTime.AddSeconds(-10)),
            PublisherNode    = new AgentId("system"),
            SubscriberNode   = new AgentId("system"),
            Topic            = new TopicName("system.session_start"),
            EventId          = new EventId(_nextId++),
            TraceId          = new TraceId(0),
            PayloadJson      = $"{{\"sessionId\":\"{sessionId}\",\"scenarioId\":\"Test\"}}",
        };

        var traceEvent = MakeEvent(rootId, traceId, 0, at: BaseTime);

        await _fixture.PushAsync([sessionStart]);
        await _fixture.PushAsync([traceEvent]);

        // Act
        var tree = await _svc.GetTraceTreeAsync(traceId, maxEvents: 100, CancellationToken.None);

        // Assert
        tree.Should().NotBeNull();
        tree!.SessionId.Should().Be(sessionId);
        tree.Summary.FirstEventUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task GetTraceTree_ConvergentDag_BothParentEdgesPresent()
    {
        // DAG: A → C and B → C (two parents for C)
        var traceId = _nextId++;
        var idA = _nextId++;
        var idB = _nextId++;
        var idC = _nextId++;

        await _fixture.PushAsync(
        [
            MakeEvent(idA, traceId, parentEventId: 0,    at: BaseTime),
            MakeEvent(idB, traceId, parentEventId: 0,    at: BaseTime.AddMilliseconds(1)),
            MakeEvent(idC, traceId, parentEventId: idA,  at: BaseTime.AddMilliseconds(50)),
        ]);

        // NOTE: True convergent DAG (event with two parents) is not possible with
        // single parent_event_id per event. "Convergent" here means two separate
        // root chains in the same trace. A and B are both roots; only A→C edge exists.

        var tree = await _svc.GetTraceTreeAsync(traceId, maxEvents: 100, CancellationToken.None);

        tree.Should().NotBeNull();
        tree!.Nodes.Should().HaveCount(3, "3 events in the trace");
        tree.Edges.Should().HaveCount(1, "only A→C edge (B is a separate root)");
        tree.Summary.RootCount.Should().Be(2, "A and B are both roots");
        tree.Summary.LeafCount.Should().Be(2, "B and C are both leaves");

        // Verify the edge is A → C
        tree.Edges.Should().ContainSingle(e =>
            e.ParentEventId.Value == idA && e.ChildEventId.Value == idC,
            "edge from A to C must exist");
    }

    [Fact]
    public async Task GetTraceTree_CrossIntervalTrace_AllNodesReturnedWithCrossRotationEdges()
    {
        // Arrange: push 5 events on a trace, rotate, push 5 more on the SAME trace
        var traceId = _nextId++;
        var rootId  = _nextId++;
        var midId   = _nextId++;

        // Events in interval 1: root → e1 → e2 → e3 → mid
        var ids1 = Enumerable.Range(0, 4).Select(_ => _nextId++).ToArray();
        var events1 = new List<EventRecord>
        {
            MakeEvent(rootId,  traceId, 0,       at: BaseTime),
            MakeEvent(ids1[0], traceId, rootId,  at: BaseTime.AddSeconds(1)),
            MakeEvent(ids1[1], traceId, ids1[0], at: BaseTime.AddSeconds(2)),
            MakeEvent(ids1[2], traceId, ids1[1], at: BaseTime.AddSeconds(3)),
            MakeEvent(midId,   traceId, ids1[2], at: BaseTime.AddSeconds(4)),
        };
        await _fixture.PushAsync(events1);

        // Force rotation so interval 1 is closed and interval 2 opens
        await _fixture.ForceRotationAsync();

        // Events in interval 2: continue from mid
        var ids2 = Enumerable.Range(0, 5).Select(_ => _nextId++).ToArray();
        var events2 = new List<EventRecord>
        {
            MakeEvent(ids2[0], traceId, midId,   at: BaseTime.AddSeconds(5)),
            MakeEvent(ids2[1], traceId, ids2[0], at: BaseTime.AddSeconds(6)),
            MakeEvent(ids2[2], traceId, ids2[1], at: BaseTime.AddSeconds(7)),
            MakeEvent(ids2[3], traceId, ids2[2], at: BaseTime.AddSeconds(8)),
            MakeEvent(ids2[4], traceId, ids2[3], at: BaseTime.AddSeconds(9)),
        };
        await _fixture.PushAsync(events2);

        // Act: query the full trace tree
        var tree = await _svc.GetTraceTreeAsync(traceId, maxEvents: 100, CancellationToken.None);

        // Assert: all 10 events returned with 9 edges intact across the interval boundary
        tree.Should().NotBeNull();
        tree!.Nodes.Should().HaveCount(10, "all 10 events across both intervals");
        tree.Edges.Should().HaveCount(9, "9 edges: full chain root→leaf across rotation");
        tree.Summary.RootCount.Should().Be(1, "single root");
        tree.Summary.LeafCount.Should().Be(1, "single leaf");

        // Verify the cross-interval edge: mid → ids2[0]
        tree.Edges.Should().Contain(e =>
            e.ParentEventId.Value == midId && e.ChildEventId.Value == ids2[0],
            "cross-interval edge from interval 1 to interval 2 must be present");
    }
}
