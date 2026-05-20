using Tracer.Aggregator.Configuration;
using Tracer.Aggregator.Discovery;
using Tracer.Aggregator.Progress;
using Tracer.Core.Time;

namespace Tracer.Aggregator.Consolidation;

/// <summary>
/// Stub implementation: does nothing (FastStateScope.None is the default).
/// Will be replaced with a real implementation in TRC-P4-006.
/// </summary>
internal static class FastStateCopier
{
    public static Task<FastStateConsolidationStats> CopyAsync(
        IReadOnlyList<ExtractedInterval> sources,
        string bundleStagingPath,
        FastStateScope scope,
        IReadOnlyList<string>? entityFilter,
        TimeRange timeRange,
        IAggregationProgressReporter? progress,
        CancellationToken ct = default)
    {
        return Task.FromResult(new FastStateConsolidationStats(TotalRowCount: 0, EntityCount: 0));
    }
}
