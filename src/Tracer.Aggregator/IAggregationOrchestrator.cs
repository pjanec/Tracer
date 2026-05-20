using Tracer.Aggregator.Configuration;
using Tracer.Aggregator.Progress;

namespace Tracer.Aggregator;

/// <summary>Abstraction over <see cref="AggregationOrchestrator"/> for testability.</summary>
public interface IAggregationOrchestrator
{
    Task<AggregationResult> RunAsync(
        AggregationRequest request,
        IAggregationProgressReporter? progress = null,
        CancellationToken ct = default);
}
