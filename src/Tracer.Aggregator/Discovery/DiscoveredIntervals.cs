using Tracer.Core.Domain;

namespace Tracer.Aggregator.Discovery;

/// <summary>
/// A single discovered interval for a specific node.
/// </summary>
public sealed record DiscoveredInterval(string NodeId, IntervalDescriptor Descriptor);

/// <summary>
/// The set of discovered intervals across all nodes.
/// </summary>
public sealed record DiscoveredIntervals(IReadOnlyList<DiscoveredInterval> Intervals)
{
    public int Count => Intervals.Count;
    public int NodeCount => Intervals.Select(i => i.NodeId).Distinct().Count();
}
