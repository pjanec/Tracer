using DuckDB.NET.Data;
using Tracer.Aggregator.Configuration;
using Tracer.Aggregator.Discovery;
using Tracer.Aggregator.Progress;
using Tracer.Bundle.Format;
using Tracer.Core.Time;

namespace Tracer.Aggregator.Consolidation;

/// <summary>
/// Copies per-entity fast-state Parquet samples into the bundle staging directory,
/// filtered by <see cref="FastStateScope"/> and optionally by entity list.
/// </summary>
internal static class FastStateCopier
{
    public static async Task<FastStateConsolidationStats> CopyAsync(
        IReadOnlyList<ExtractedInterval> sources,
        string bundleStagingPath,
        FastStateScope scope,
        IReadOnlyList<string>? entityFilter,
        TimeRange timeRange,
        IAggregationProgressReporter? progress,
        CancellationToken ct = default)
    {
        if (scope == FastStateScope.None)
            return new FastStateConsolidationStats(TotalRowCount: 0, EntityCount: 0);

        var bundleFastStateDir = Path.Combine(bundleStagingPath, BundleLayout.FastStateDirectory);
        Directory.CreateDirectory(bundleFastStateDir);

        long totalRows = 0;
        var entitiesSeen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var source in sources)
        {
            ct.ThrowIfCancellationRequested();
            var srcFastDir = Path.Combine(source.Directory, "fast_state");
            if (!Directory.Exists(srcFastDir)) continue;

            foreach (var parquetFile in Directory.EnumerateFiles(srcFastDir, "*.parquet"))
            {
                var topic = Path.GetFileNameWithoutExtension(parquetFile);
                var rowsCopied = await SplitAndCopyByEntityAsync(
                    parquetFile, topic, bundleFastStateDir,
                    scope, entityFilter, timeRange, entitiesSeen, ct);
                totalRows += rowsCopied;
            }

            progress?.Report(AggregationStage.FastStateCopied,
                $"  Processed {source.NodeId} {source.Descriptor.Timestamp.Value}");
        }

        return new FastStateConsolidationStats(
            TotalRowCount: totalRows,
            EntityCount: entitiesSeen.Count);
    }

    private static async Task<long> SplitAndCopyByEntityAsync(
        string srcParquet, string topic, string bundleFastStateDir,
        FastStateScope scope, IReadOnlyList<string>? entityFilter,
        TimeRange timeRange, HashSet<string> entitiesSeen,
        CancellationToken ct)
    {
        await using var conn = new DuckDBConnection("Data Source=:memory:");
        await conn.OpenAsync(ct);

        // Discover entities present in this file within the time range
        var entitiesInSource = new List<string>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"""
                SELECT DISTINCT instance_key
                FROM read_parquet('{EscapeSql(srcParquet)}')
                WHERE publish_wallclock >= $from AND publish_wallclock < $to
                """;
            cmd.Parameters.Add(new DuckDBParameter("from", timeRange.StartUtc.ToDateTimeOffset().UtcDateTime));
            cmd.Parameters.Add(new DuckDBParameter("to",   timeRange.EndUtc.ToDateTimeOffset().UtcDateTime));
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                entitiesInSource.Add(reader.GetString(0));
        }

        long totalRowsWritten = 0;
        foreach (var entity in entitiesInSource)
        {
            if (scope == FastStateScope.SelectedEntities &&
                (entityFilter is null || !entityFilter.Contains(entity)))
                continue;

            entitiesSeen.Add(entity);

            var safeTopic  = BundleNaming.SafeFileName(topic);
            var safeEntity = BundleNaming.SafeFileName(entity);
            var outDir  = Path.Combine(bundleFastStateDir, safeTopic, safeEntity);
            Directory.CreateDirectory(outDir);
            var outPath = Path.Combine(outDir, "samples.parquet");

            long rowsThisCopy = await WriteOrAppendParquetAsync(
                conn, srcParquet, entity, outPath, timeRange, ct);
            totalRowsWritten += rowsThisCopy;
        }

        return totalRowsWritten;
    }

    private static async Task<long> WriteOrAppendParquetAsync(
        DuckDBConnection conn, string srcParquet, string entity, string outPath,
        TimeRange timeRange, CancellationToken ct)
    {
        bool exists = File.Exists(outPath);

        var sql = exists
            ? $"""
                COPY (
                    SELECT * FROM read_parquet('{EscapeSql(outPath)}')
                    UNION ALL
                    SELECT * FROM read_parquet('{EscapeSql(srcParquet)}')
                    WHERE instance_key = $entity
                      AND publish_wallclock >= $from AND publish_wallclock < $to
                ) TO '{EscapeSql(outPath + ".tmp")}' (FORMAT PARQUET);
                """
            : $"""
                COPY (
                    SELECT * FROM read_parquet('{EscapeSql(srcParquet)}')
                    WHERE instance_key = $entity
                      AND publish_wallclock >= $from AND publish_wallclock < $to
                ) TO '{EscapeSql(outPath)}' (FORMAT PARQUET);
                """;

        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = sql;
            cmd.Parameters.Add(new DuckDBParameter("entity", entity));
            cmd.Parameters.Add(new DuckDBParameter("from",   timeRange.StartUtc.ToDateTimeOffset().UtcDateTime));
            cmd.Parameters.Add(new DuckDBParameter("to",     timeRange.EndUtc.ToDateTimeOffset().UtcDateTime));
            await cmd.ExecuteNonQueryAsync(ct);
        }

        if (exists)
        {
            File.Delete(outPath);
            File.Move(outPath + ".tmp", outPath);
        }

        await using var countCmd = conn.CreateCommand();
        countCmd.CommandText = $"SELECT COUNT(*) FROM read_parquet('{EscapeSql(outPath)}')";
        var scalar = await countCmd.ExecuteScalarAsync(ct);
        return scalar is null ? 0L : Convert.ToInt64(scalar);
    }

    private static string EscapeSql(string s) => s.Replace("'", "''");
}
