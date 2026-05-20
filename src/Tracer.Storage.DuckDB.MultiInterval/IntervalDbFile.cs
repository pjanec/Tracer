namespace Tracer.Storage.DuckDB.MultiInterval;

/// <summary>A DuckDB file to attach to a <see cref="MultiIntervalReader"/>.</summary>
/// <param name="FilePath">Absolute path to the .duckdb file.</param>
/// <param name="AliasHint">Human-readable hint used as a prefix when generating the SQL alias.</param>
public record IntervalDbFile(string FilePath, string AliasHint);
