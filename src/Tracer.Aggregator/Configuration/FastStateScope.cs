namespace Tracer.Aggregator.Configuration;

/// <summary>
/// Controls which fast-state (Parquet) data is included in a bundle.
/// </summary>
public enum FastStateScope
{
    /// <summary>No fast-state data is included. Default.</summary>
    None,

    /// <summary>Only the entities listed in <see cref="AggregationRequest.FastStateEntities"/> are included.</summary>
    SelectedEntities,

    /// <summary>All entities found in the source intervals are included.</summary>
    All,
}
