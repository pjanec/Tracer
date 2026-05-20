namespace Tracer.Aggregator.Consolidation;

/// <summary>Stats returned by EventsConsolidator.</summary>
public sealed record EventsConsolidationStats(long TotalEvents);

/// <summary>Stats returned by SlowStateConsolidator.</summary>
public sealed record SlowStateConsolidationStats(long TotalSamples);

/// <summary>Stats returned by FastStateCopier.</summary>
public sealed record FastStateConsolidationStats(long TotalRowCount, int EntityCount);
