using Tracer.Aggregator.Discovery;
using Tracer.Core.Time;

namespace Tracer.Aggregator.Consolidation;

/// <summary>A node entry in the topology output.</summary>
internal sealed record TopologyNode(
    string NodeId,
    DateTimeOffset FirstSeenUtc,
    DateTimeOffset LastSeenUtc,
    long EventsPublished);

/// <summary>The topology of a bundle.</summary>
internal sealed record BundleTopology(IReadOnlyList<TopologyNode> Nodes);

/// <summary>
/// Stub implementation: derives topology from extracted interval descriptors.
/// Will be replaced with a real implementation in TRC-P4-006.
/// </summary>
internal static class TopologyExtractor
{
    public static BundleTopology Extract(IReadOnlyList<ExtractedInterval> sources, TimeRange timeRange)
    {
        var nodes = sources
            .GroupBy(s => s.NodeId)
            .Select(g => new TopologyNode(
                NodeId: g.Key,
                FirstSeenUtc: g.Min(x => x.Descriptor.StartUtc),
                LastSeenUtc: g.Max(x => x.Descriptor.EndUtc),
                EventsPublished: 0))
            .ToArray();

        return new BundleTopology(nodes);
    }
}
