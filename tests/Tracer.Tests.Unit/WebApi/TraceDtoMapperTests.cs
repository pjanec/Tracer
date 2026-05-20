using FluentAssertions;
using Tracer.Core.Domain;
using Tracer.Core.Identity;
using Tracer.Core.Records;
using Tracer.Core.Time;
using Tracer.WebApi.Contracts.Mapping;
using Tracer.WebApi.Queries;
using Xunit;

namespace Tracer.Tests.Unit.WebApi;

public sealed class TraceDtoMapperTests
{
    private static readonly WallclockTime BaseTime =
        WallclockTime.FromUnixNanoseconds(
            new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero)
                .ToUnixTimeMilliseconds() * 1_000_000L);

    private static EventRecord MakeEvent(ulong eventId, ulong traceId, ulong parentId = 0,
        string node = "node-a") =>
        new EventRecord
        {
            SequenceNumber   = eventId,
            PublishWallclock = BaseTime,
            ReceiveWallclock = BaseTime,
            PublisherNode    = new AgentId(node),
            SubscriberNode   = new AgentId(node),
            Topic            = new TopicName("test.topic"),
            EventId          = new EventId(eventId),
            TraceId          = new TraceId(traceId),
            ParentEventId    = parentId != 0 ? new EventId(parentId) : null,
            PayloadJson      = "{}",
        };

    private static TraceTree MakeTree(ulong traceId, params EventRecord[] events)
    {
        var nodes = events.Select(e => new TraceNode(e)).ToList();
        var nodeById = nodes.ToDictionary(n => n.Event.EventId.Value);
        var edges = new List<TraceEdge>();
        foreach (var node in nodes)
        {
            var parentId = node.Event.ParentEventId?.Value ?? 0;
            if (parentId == 0 || !nodeById.TryGetValue(parentId, out var parent)) continue;
            edges.Add(new TraceEdge(parent.Event.EventId, node.Event.EventId, 5.0));
        }
        var childSet  = new HashSet<ulong>(edges.Select(e => e.ChildEventId.Value));
        var parentSet = new HashSet<ulong>(edges.Select(e => e.ParentEventId.Value));
        return new TraceTree
        {
            TraceId  = traceId,
            Nodes    = nodes,
            Edges    = edges,
            Roots    = nodes.Where(n => !childSet.Contains(n.Event.EventId.Value)).ToList(),
            Leaves   = nodes.Where(n => !parentSet.Contains(n.Event.EventId.Value)).ToList(),
            Summary  = new TraceSummary
            {
                TraceId            = traceId,
                TotalEvents        = events.Length,
                Truncated          = false,
                TotalSpanMs        = 0,
                ParticipatingNodes = new[] { "node-a" },
                RootCount          = 1,
                LeafCount          = 1,
            },
        };
    }

    [Fact]
    public void MapTraceTree_AllNodesProjected_EventIdIsUppercaseHex16()
    {
        ulong traceId  = 0xA1B2C3D4E5F60001UL;
        ulong event1Id = 0x00000000000000FFUL;
        ulong event2Id = 0x0000000000001000UL;

        var tree = MakeTree(traceId,
            MakeEvent(event1Id, traceId),
            MakeEvent(event2Id, traceId, event1Id));

        var dto = TraceDtoMapper.Map(tree);

        dto.Nodes.Should().HaveCount(2);
        foreach (var node in dto.Nodes)
        {
            node.EventId.Should().HaveLength(16, "EventId must be 16 chars");
            node.EventId.Should().MatchRegex("^[0-9A-F]{16}$", "EventId must be uppercase hex");
        }

        dto.Nodes.Should().Contain(n => n.EventId == "00000000000000FF");
        dto.Nodes.Should().Contain(n => n.EventId == "0000000000001000");
    }

    [Fact]
    public void MapTraceTree_RootNodes_HaveNullParentEventId()
    {
        ulong traceId = 0xBBBBBBBBBBBBBBBBUL;
        ulong rootId  = 0x0000000000000001UL;
        ulong childId = 0x0000000000000002UL;

        var tree = MakeTree(traceId,
            MakeEvent(rootId, traceId, parentId: 0),
            MakeEvent(childId, traceId, parentId: rootId));

        var dto = TraceDtoMapper.Map(tree);

        var rootDto  = dto.Nodes.Single(n => n.EventId == rootId.ToString("X16"));
        var childDto = dto.Nodes.Single(n => n.EventId == childId.ToString("X16"));

        rootDto.ParentEventId.Should().BeNull("root node has no parent");
        childDto.ParentEventId.Should().NotBeNull("child node has a parent");
        childDto.ParentEventId.Should().Be(rootId.ToString("X16"));
    }

    [Fact]
    public void MapTraceEdge_LatencyMs_RoundTripsAsDouble()
    {
        ulong traceId  = 0xCCCCCCCCCCCCCCCCUL;
        ulong parentId = 0x0000000000000001UL;
        ulong childId  = 0x0000000000000002UL;
        const double expectedLatency = 123.456789;

        var nodes = new[]
        {
            new TraceNode(MakeEvent(parentId, traceId)),
            new TraceNode(MakeEvent(childId, traceId, parentId)),
        };
        var edge = new TraceEdge(new EventId(parentId), new EventId(childId), expectedLatency);
        var tree = new TraceTree
        {
            TraceId  = traceId,
            Nodes    = nodes,
            Edges    = [edge],
            Roots    = [nodes[0]],
            Leaves   = [nodes[1]],
            Summary  = new TraceSummary
            {
                TraceId            = traceId,
                TotalEvents        = 2,
                Truncated          = false,
                TotalSpanMs        = 0,
                ParticipatingNodes = new[] { "node-a" },
                RootCount          = 1,
                LeafCount          = 1,
            },
        };

        var dto = TraceDtoMapper.Map(tree);

        dto.Edges.Should().HaveCount(1);
        dto.Edges[0].LatencyMs.Should().Be(expectedLatency, "latency must not be rounded");
    }

    [Fact]
    public void MapTraceSummary_WhenTruncated_TotalEventsAvailableIsNonNull()
    {
        var summary = new TraceSummary
        {
            TraceId              = 0xDDDDDDDDDDDDDDDDUL,
            TotalEvents          = 100,
            TotalEventsAvailable = 500,
            Truncated            = true,
            TotalSpanMs          = 1000,
            ParticipatingNodes   = new[] { "node-a" },
            RootCount            = 1,
            LeafCount            = 5,
        };

        var dto = TraceDtoMapper.Map(summary);

        dto.Truncated.Should().BeTrue();
        dto.TotalEventsAvailable.Should().NotBeNull("truncated traces must include TotalEventsAvailable");
        dto.TotalEventsAvailable.Should().Be(500);
    }

    [Fact]
    public void MapTraceSummary_WhenNotTruncated_TotalEventsAvailableIsNull()
    {
        var summary = new TraceSummary
        {
            TraceId              = 0xEEEEEEEEEEEEEEEEUL,
            TotalEvents          = 50,
            TotalEventsAvailable = null,
            Truncated            = false,
            TotalSpanMs          = 500,
            ParticipatingNodes   = new[] { "node-a" },
            RootCount            = 1,
            LeafCount            = 3,
        };

        var dto = TraceDtoMapper.Map(summary);

        dto.Truncated.Should().BeFalse();
        dto.TotalEventsAvailable.Should().BeNull("non-truncated traces must not include TotalEventsAvailable");
    }

    [Fact]
    public void MapTraceTree_SessionIdPresentInDto()
    {
        var ev = MakeEvent(1001, 2002);
        var tree = MakeTree(2002, ev);
        var treeWithSession = tree with { SessionId = "my-session-xyz" };

        var dto = TraceDtoMapper.Map(treeWithSession);

        dto.SessionId.Should().Be("my-session-xyz");
    }
}
