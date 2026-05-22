using DuckDB.NET.Data;
using Microsoft.Extensions.Logging;
using Tracer.Core.Time;

namespace Tracer.Storage.Parquet;

/// <summary>
/// Reads fast-state Parquet files on demand using DuckDB's <c>read_parquet()</c> function.
/// Every public method opens its own in-memory DuckDB connection; no shared state.
/// </summary>
public sealed class ParquetReader
{
    private readonly ILogger<ParquetReader> _logger;

    public ParquetReader(ILogger<ParquetReader> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <summary>
    /// Inspects the columns and types of a Parquet file without reading data rows.
    /// </summary>
    public async Task<ParquetSchema> InspectSchemaAsync(string parquetPath, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(parquetPath);

        await using var conn = new DuckDBConnection("Data Source=:memory:");
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DESCRIBE SELECT * FROM read_parquet('{EscapeSql(parquetPath)}')";

        var columns = new List<ParquetColumn>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var name = reader.GetString(0);
            var type = reader.GetString(1);
            columns.Add(new ParquetColumn(name, type, IsNumeric(type)));
        }

        return new ParquetSchema(parquetPath, columns);
    }

    /// <summary>
    /// Reads a time-series from a single Parquet file for the given entity, projecting specific columns.
    /// Applies stride-based downsampling when <paramref name="maxSamples"/> is exceeded.
    /// </summary>
    public Task<ParquetTimeSeriesResult> ReadTimeSeriesAsync(
        string parquetPath,
        string entityId,
        IReadOnlyList<string> columns,
        WallclockTime from,
        WallclockTime to,
        int maxSamples,
        CancellationToken ct)
        => ReadTimeSeriesAsync([parquetPath], entityId, columns, from, to, maxSamples, ct);

    /// <summary>
    /// Reads a time-series across multiple Parquet files for the given entity.
    /// Uses DuckDB's <c>read_parquet([...])</c> list syntax to query all files in one pass.
    /// </summary>
    public async Task<ParquetTimeSeriesResult> ReadTimeSeriesAsync(
        IReadOnlyList<string> parquetPaths,
        string entityId,
        IReadOnlyList<string> columns,
        WallclockTime from,
        WallclockTime to,
        int maxSamples,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(parquetPaths);
        ArgumentNullException.ThrowIfNull(entityId);
        ArgumentNullException.ThrowIfNull(columns);

        if (parquetPaths.Count == 0)
            return new ParquetTimeSeriesResult
            {
                Columns = columns,
                Samples = Array.Empty<ParquetSample>(),
                TotalSamples = 0,
                Downsampled = false
            };

        await using var conn = new DuckDBConnection("Data Source=:memory:");
        await conn.OpenAsync(ct);

        var safeColumns = columns.Select(SafeColumnIdentifier).ToList();
        var columnList = string.Join(", ", safeColumns);

        // Build the read_parquet() expression
        var parquetExpr = BuildParquetExpr(parquetPaths);

        var fromDt = from.ToDateTimeOffset().UtcDateTime;
        var toDt = to.ToDateTimeOffset().UtcDateTime;

        // Count first — decides whether downsampling is needed
        long totalSamples;
        await using (var countCmd = conn.CreateCommand())
        {
            countCmd.CommandText = $"""
                SELECT COUNT(*)
                FROM {parquetExpr}
                WHERE instance_key = $entityId
                  AND publish_wallclock >= $from
                  AND publish_wallclock <  $to
                """;
            countCmd.Parameters.Add(new DuckDBParameter("entityId", entityId));
            countCmd.Parameters.Add(new DuckDBParameter("from", fromDt));
            countCmd.Parameters.Add(new DuckDBParameter("to", toDt));
            totalSamples = (long)(await countCmd.ExecuteScalarAsync(ct))!;
        }

        if (totalSamples == 0)
            return new ParquetTimeSeriesResult
            {
                Columns = columns,
                Samples = Array.Empty<ParquetSample>(),
                TotalSamples = 0,
                Downsampled = false
            };

        var downsampled = totalSamples > maxSamples;
        var stride = downsampled ? (totalSamples / maxSamples) : 1L;

        string dataSql;
        if (downsampled)
        {
            dataSql = $"""
                WITH numbered AS (
                    SELECT
                        ROW_NUMBER() OVER (ORDER BY publish_wallclock) AS rn,
                        publish_wallclock,
                        {columnList}
                    FROM {parquetExpr}
                    WHERE instance_key = $entityId
                      AND publish_wallclock >= $from
                      AND publish_wallclock <  $to
                )
                SELECT publish_wallclock, {columnList}
                FROM numbered
                WHERE (rn - 1) % $stride = 0
                ORDER BY publish_wallclock
                """;
        }
        else
        {
            dataSql = $"""
                SELECT publish_wallclock, {columnList}
                FROM {parquetExpr}
                WHERE instance_key = $entityId
                  AND publish_wallclock >= $from
                  AND publish_wallclock <  $to
                ORDER BY publish_wallclock
                """;
        }

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = dataSql;
        cmd.Parameters.Add(new DuckDBParameter("entityId", entityId));
        cmd.Parameters.Add(new DuckDBParameter("from", fromDt));
        cmd.Parameters.Add(new DuckDBParameter("to", toDt));
        if (downsampled)
            cmd.Parameters.Add(new DuckDBParameter("stride", stride));

        var samples = new List<ParquetSample>();
        await using var dataReader = await cmd.ExecuteReaderAsync(ct);
        while (await dataReader.ReadAsync(ct))
        {
            var rawDt = dataReader.GetDateTime(0);
            var time = WallclockTime.FromDateTimeOffset(
                new DateTimeOffset(DateTime.SpecifyKind(rawDt, DateTimeKind.Utc), TimeSpan.Zero));

            var values = new Dictionary<string, double?>();
            for (int i = 0; i < columns.Count; i++)
            {
                if (dataReader.IsDBNull(i + 1))
                {
                    values[columns[i]] = null;
                }
                else
                {
                    try { values[columns[i]] = Convert.ToDouble(dataReader.GetValue(i + 1)); }
                    catch { values[columns[i]] = null; }
                }
            }
            samples.Add(new ParquetSample(time, values));
        }

        return new ParquetTimeSeriesResult
        {
            Columns = columns,
            Samples = samples,
            TotalSamples = totalSamples,
            Downsampled = downsampled
        };
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string BuildParquetExpr(IReadOnlyList<string> paths)
    {
        if (paths.Count == 1)
            return $"read_parquet('{EscapeSql(paths[0])}')";

        var list = string.Join(", ", paths.Select(p => $"'{EscapeSql(p)}'"));
        return $"read_parquet([{list}])";
    }

    /// <summary>Returns <c>true</c> for DuckDB numeric types that can be coerced to <c>double</c>.</summary>
    internal static bool IsNumeric(string duckType) =>
        duckType switch
        {
            "TINYINT" or "SMALLINT" or "INTEGER" or "BIGINT" or "HUGEINT" or
            "UTINYINT" or "USMALLINT" or "UINTEGER" or "UBIGINT" or
            "FLOAT" or "DOUBLE" or "DECIMAL" => true,
            _ => false
        };

    /// <summary>Doubles single quotes in <paramref name="s"/> for safe embedding in SQL string literals.</summary>
    internal static string EscapeSql(string s) => s.Replace("'", "''");

    /// <summary>
    /// Wraps <paramref name="name"/> in double-quotes, escaping any internal double-quote as <c>""</c>.
    /// Prevents SQL injection through user-supplied column names.
    /// </summary>
    internal static string SafeColumnIdentifier(string name)
        => $"\"{name.Replace("\"", "\"\"")}\"";
}

public sealed record ParquetColumn(string Name, string DuckType, bool IsNumeric);
public sealed record ParquetSchema(string Path, IReadOnlyList<ParquetColumn> Columns);
public sealed record ParquetSample(WallclockTime PublishWallclock, IReadOnlyDictionary<string, double?> Values);

public sealed record ParquetTimeSeriesResult
{
    public required IReadOnlyList<string> Columns { get; init; }
    public required IReadOnlyList<ParquetSample> Samples { get; init; }
    public required long TotalSamples { get; init; }
    public required bool Downsampled { get; init; }
}
