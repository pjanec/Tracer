namespace Tracer.Aggregator.Progress;

/// <summary>
/// Receives progress notifications during an aggregation run.
/// Implementations must be thread-safe.
/// </summary>
public interface IAggregationProgressReporter
{
    /// <summary>Reports the current stage and an optional human-readable message.</summary>
    void Report(AggregationStage stage, string? message = null);
}
