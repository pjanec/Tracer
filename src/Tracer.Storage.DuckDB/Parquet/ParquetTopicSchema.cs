namespace Tracer.Storage.DuckDB.Parquet;

/// <summary>
/// Schema definition for a single fast-state topic's Parquet file.
/// </summary>
public sealed record ParquetTopicSchema
{
    public required string TopicName { get; init; }
    public required IReadOnlyList<ParquetColumn> Columns { get; init; }
}

/// <summary>
/// A single column in a fast-state Parquet schema.
/// </summary>
public sealed record ParquetColumn
{
    public required string Name { get; init; }
    public required ParquetType Type { get; init; }
    public bool Nullable { get; init; } = false;

    /// <summary>JSONPath expression to extract the value from the record payload.</summary>
    public required string JsonPath { get; init; }
}

/// <summary>
/// Supported Parquet column types for fast-state topics.
/// </summary>
public enum ParquetType
{
    Int32,
    Int64,
    UInt64,
    Float,
    Double,
    Bool,
    String,
    TimestampNs
}
