namespace Tracer.WebApi.Lifecycle;

/// <summary>
/// Provides live status metrics for the ingestion pipeline. Implemented by
/// <c>ObserverStateReporter</c> (in <c>Tracer.Observer</c>) and registered in DI.
/// </summary>
public interface ILiveStatusProvider
{
    long IngestedTotal { get; }
    long DroppedTotal { get; }
    DateTimeOffset? LastEventUtc { get; }
}
