using Tracer.Bundle.Format;
using Tracer.Core.Time;

namespace Tracer.Aggregator.Configuration;

/// <summary>
/// The result of a completed aggregation run.
/// </summary>
public sealed record AggregationResult
{
    public required string BundleId { get; init; }
    public required string OutputPath { get; init; }
    public required TimeRange TimeRange { get; init; }
    public required BundleStatistics Statistics { get; init; }
    public required TimeSpan Duration { get; init; }
    public required int SourceIntervalsUsed { get; init; }
}
