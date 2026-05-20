using DuckDB.NET.Data;
using Tracer.Aggregator.Discovery;
using Tracer.Aggregator.Progress;
using Tracer.Core.Time;
using Tracer.Storage.DuckDB.Schema;

namespace Tracer.Aggregator.Consolidation;

/// <summary>
/// Merges slow_state rows from per-node events.duckdb files into a single slow_state.duckdb.
/// The source events.duckdb contains BOTH the events and slow_state tables (created by DuckDbStorageWriter).
/// Only rows whose publish_wallclock falls within [timeRange.StartUtc, timeRange.EndUtc) are copied.
/// </summary>
internal static class SlowStateConsolidator
{
    public static async Task<SlowStateConsolidationStats> ConsolidateAsync(
        IReadOnlyList<ExtractedInterval> sources,
        string outputDbPath,
        TimeRange timeRange,
        IAggregationProgressReporter? progress,
        CancellationToken ct = default)
    {
        await using var output = new DuckDBConnection($"Data Source={outputDbPath}");
        await output.OpenAsync(ct);

        await ExecAsync(output, SchemaV1.CreateSlowStateTable, ct);

        long totalSamples = 0;
        for (int idx = 0; idx < sources.Count; idx++)
        {
            ct.ThrowIfCancellationRequested();
            var source = sources[idx];
            // Both events and slow_state live in the same events.duckdb from DuckDbStorageWriter
            var srcPath = Path.Combine(source.Directory, "events.duckdb");
            if (!File.Exists(srcPath)) continue;

            var alias = $"ss_{idx}";
            await ExecAsync(output,
                $"ATTACH '{EscapeSql(srcPath)}' AS {alias} (READ_ONLY);", ct);

            await using (var cmd = output.CreateCommand())
            {
                cmd.CommandText = $"""
                    INSERT INTO slow_state
                    SELECT * FROM {alias}.slow_state
                    WHERE publish_wallclock >= $from
                      AND publish_wallclock <  $to;
                    """;
                cmd.Parameters.Add(new DuckDBParameter("from", timeRange.StartUtc.ToDateTimeOffset().UtcDateTime));
                cmd.Parameters.Add(new DuckDBParameter("to",   timeRange.EndUtc.ToDateTimeOffset().UtcDateTime));
                var inserted = await cmd.ExecuteNonQueryAsync(ct);
                if (inserted >= 0)
                    totalSamples += inserted;
            }

            await ExecAsync(output, $"DETACH {alias};", ct);
        }

        // Recount if any INSERT returned -1
        if (totalSamples == 0 && sources.Any(s => File.Exists(Path.Combine(s.Directory, "events.duckdb"))))
        {
            await using var countCmd = output.CreateCommand();
            countCmd.CommandText = "SELECT COUNT(*) FROM slow_state;";
            var scalar = await countCmd.ExecuteScalarAsync(ct);
            if (scalar is not null)
                totalSamples = Convert.ToInt64(scalar);
        }

        await ExecAsync(output, "CREATE INDEX IF NOT EXISTS idx_ss_instance_time ON slow_state(instance_key, publish_wallclock);", ct);
        await ExecAsync(output, "CREATE INDEX IF NOT EXISTS idx_ss_topic ON slow_state(topic);", ct);
        await ExecAsync(output, "CHECKPOINT;", ct);

        return new SlowStateConsolidationStats(TotalSamples: totalSamples);
    }

    private static async Task ExecAsync(DuckDBConnection conn, string sql, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static string EscapeSql(string s) => s.Replace("'", "''");
}
