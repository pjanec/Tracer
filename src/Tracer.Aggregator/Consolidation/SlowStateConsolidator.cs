using Tracer.Aggregator.Discovery;
using Tracer.Aggregator.Progress;
using Tracer.Core.Time;

namespace Tracer.Aggregator.Consolidation;

/// <summary>
/// Stub implementation: creates an empty slow_state.duckdb at the output path.
/// Will be replaced with a real implementation in TRC-P4-006.
/// </summary>
internal static class SlowStateConsolidator
{
    public static async Task<SlowStateConsolidationStats> ConsolidateAsync(
        IReadOnlyList<ExtractedInterval> sources,
        string outputDbPath,
        TimeRange timeRange,
        IAggregationProgressReporter? progress,
        CancellationToken ct = default)
    {
        await File.WriteAllBytesAsync(outputDbPath, Array.Empty<byte>(), ct);
        return new SlowStateConsolidationStats(TotalSamples: 0);
    }
}
