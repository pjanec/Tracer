namespace Tracer.Core.Records;

/// <summary>
/// Indicates the sampling rate of a state snapshot.
/// </summary>
public enum StateSampleRate
{
    /// <summary>Low-frequency state — stored in DuckDB.</summary>
    Slow,

    /// <summary>High-frequency state — stored in Parquet (Phase 7).</summary>
    Fast
}

/// <summary>
/// A periodic snapshot of a node's state for a given instance key.
/// </summary>
public sealed record StateSampleRecord : DiagnosticRecord
{
    public required string InstanceKey { get; init; }
    public Tracer.Core.Identity.TraceId? TraceId { get; init; }
    public required string PayloadJson { get; init; }
    public required StateSampleRate Rate { get; init; }
    public IReadOnlyDictionary<string, double?>? TypedValues { get; init; }
}
