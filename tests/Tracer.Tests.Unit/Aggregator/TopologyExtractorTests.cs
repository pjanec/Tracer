using FluentAssertions;
using Tracer.Aggregator.Consolidation;
using Tracer.Aggregator.Discovery;
using Tracer.Core.Domain;
using Tracer.Core.Time;
using Xunit;

namespace Tracer.Tests.Unit.Aggregator;

public sealed class TopologyExtractorTests
{
    private static readonly DateTimeOffset _base = new(2026, 5, 19, 14, 0, 0, TimeSpan.Zero);

    private static IntervalDescriptor MakeDescriptor(DateTimeOffset start, DateTimeOffset end)
        => new(IntervalTimestamp.FromUtc(start), start, end);

    private static Tracer.Core.Time.TimeRange MakeRange(DateTimeOffset start, DateTimeOffset end)
        => new(WallclockTime.FromDateTimeOffset(start), WallclockTime.FromDateTimeOffset(end));

    [Fact]
    public void Extract_EmptyInput_ReturnsEmptyTopology()
    {
        var topology = TopologyExtractor.Extract(
            Array.Empty<ExtractedInterval>(),
            MakeRange(_base, _base.AddHours(1)));

        topology.Nodes.Should().BeEmpty();
    }

    [Fact]
    public void Extract_UniqueNodes_OneNodePerNodeId()
    {
        var range = MakeRange(_base, _base.AddHours(2));
        var sources = new[]
        {
            new ExtractedInterval("node-a", MakeDescriptor(_base, _base.AddHours(1)), "/tmp/a1"),
            new ExtractedInterval("node-b", MakeDescriptor(_base, _base.AddHours(1)), "/tmp/b1"),
            new ExtractedInterval("node-a", MakeDescriptor(_base.AddHours(1), _base.AddHours(2)), "/tmp/a2"),
        };

        var topology = TopologyExtractor.Extract(sources, range);

        topology.Nodes.Should().HaveCount(2, "two distinct node IDs");
        topology.Nodes.Select(n => n.NodeId).Should().BeEquivalentTo(new[] { "node-a", "node-b" });
    }

    [Fact]
    public void Extract_FirstLastSeen_ReflectEarliestLatestDescriptor()
    {
        var early = _base;
        var mid   = _base.AddHours(1);
        var late  = _base.AddHours(2);

        var sources = new[]
        {
            new ExtractedInterval("node-a", MakeDescriptor(mid, late),   "/tmp/a1"),
            new ExtractedInterval("node-a", MakeDescriptor(early, mid),  "/tmp/a2"),
        };

        var topology = TopologyExtractor.Extract(sources, MakeRange(early, late));

        var nodeA = topology.Nodes.Single(n => n.NodeId == "node-a");
        nodeA.FirstSeenUtc.Should().Be(early);
        nodeA.LastSeenUtc.Should().Be(late);
    }
}
