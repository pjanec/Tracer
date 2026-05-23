namespace Tracer.Aggregator.Progress;

/// <summary>
/// Describes the current stage of an aggregation run.
/// </summary>
public enum AggregationStage
{
    /// <summary>Aggregation has started; initial validation is complete.</summary>
    Started,

    /// <summary>Session ID has been resolved to a time range.</summary>
    TimeRangeResolved,

    /// <summary>Overlapping source intervals have been enumerated.</summary>
    IntervalsDiscovered,

    /// <summary>Source interval archives have been extracted to the staging directory.</summary>
    IntervalsExtracted,

    /// <summary>Events are being copied from a source interval (intermediate progress).</summary>
    EventsConsolidating,

    /// <summary>All events have been consolidated into the output database.</summary>
    EventsConsolidated,

    /// <summary>All slow-state samples have been consolidated into the output database.</summary>
    SlowStateConsolidated,

    /// <summary>Fast-state Parquet files have been copied according to the scope policy.</summary>
    FastStateCopied,

    /// <summary>Scenario, topology, and source-intervals metadata files have been written.</summary>
    MetadataWritten,

    /// <summary>Annotations (user notes) have been exported into the bundle's annotations/ directory.</summary>
    AnnotationsExported,

    /// <summary>Saved views have been exported into the bundle's saved_views/ directory.</summary>
    SavedViewsExported,

    /// <summary>Checksums and manifest have been computed and written.</summary>
    ManifestWritten,

    /// <summary>The bundle has been finalized at its output path.</summary>
    Completed,

    /// <summary>The aggregation run failed with an unrecoverable error.</summary>
    Failed,
}
