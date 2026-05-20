using Tracer.Aggregator.Discovery;

namespace Tracer.Aggregator.Consolidation;

/// <summary>Entry in source_intervals.json describing one contributing interval.</summary>
internal sealed record SourceIntervalEntry(
    string NodeId,
    string IntervalTimestamp,
    string IntervalSourcePath,
    long ContributedEventCount);

/// <summary>
/// Builds the source_intervals.json data from extracted intervals.
/// </summary>
internal static class SourceIntervalsBuilder
{
    public static IReadOnlyList<SourceIntervalEntry> Build(IReadOnlyList<ExtractedInterval> sources)
    {
        return sources
            .Select(s => new SourceIntervalEntry(
                NodeId: s.NodeId,
                IntervalTimestamp: s.Descriptor.Timestamp.Value,
                IntervalSourcePath: s.Directory,
                ContributedEventCount: 0))
            .ToArray();
    }
}
