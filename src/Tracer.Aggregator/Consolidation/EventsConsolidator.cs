using DuckDB.NET.Data;
using Tracer.Aggregator.Discovery;
using Tracer.Aggregator.Progress;
using Tracer.Core.Time;
using Tracer.Storage.DuckDB.Schema;

namespace Tracer.Aggregator.Consolidation;

/// <summary>
/// Merges per-node events.duckdb files into a single consolidated events.duckdb.
/// Only rows whose publish_wallclock falls within [timeRange.StartUtc, timeRange.EndUtc) are copied.
/// </summary>
internal static class EventsConsolidator
{
    public static async Task<EventsConsolidationStats> ConsolidateAsync(
        IReadOnlyList<ExtractedInterval> sources,
        string outputDbPath,
        TimeRange timeRange,
        IAggregationProgressReporter? progress,
        CancellationToken ct = default)
    {
        // 1. Create output DB with schema
        await using var output = new DuckDBConnection($"Data Source={EscapeConnStr(outputDbPath)}");
        await output.OpenAsync(ct);

        await ExecAsync(output, SchemaV1.CreateEventsTable, ct);

        // 2. For each source ATTACH, INSERT within range, DETACH
        long totalEvents = 0;
        for (int idx = 0; idx < sources.Count; idx++)
        {
            ct.ThrowIfCancellationRequested();
            var source = sources[idx];
            var srcPath = Path.Combine(source.Directory, "events.duckdb");
            if (!File.Exists(srcPath)) continue;

            var alias = $"src_{idx}";
            await ExecAsync(output,
                $"ATTACH '{EscapeSql(srcPath)}' AS {alias} (READ_ONLY);", ct);

            await using (var cmd = output.CreateCommand())
            {
                cmd.CommandText = $"""
                    INSERT INTO events
                    SELECT * FROM {alias}.events
                    WHERE publish_wallclock >= $from
                      AND publish_wallclock <  $to;
                    """;
                cmd.Parameters.Add(new DuckDBParameter("from", timeRange.StartUtc.ToDateTimeOffset().UtcDateTime));
                cmd.Parameters.Add(new DuckDBParameter("to",   timeRange.EndUtc.ToDateTimeOffset().UtcDateTime));
                var inserted = await cmd.ExecuteNonQueryAsync(ct);
                // ExecuteNonQueryAsync may return -1 on some DuckDB.NET versions; fall back to COUNT delta
                if (inserted >= 0)
                    totalEvents += inserted;

                progress?.Report(AggregationStage.EventsConsolidating,
                    $"  {source.NodeId} {source.Descriptor.Timestamp.Value}: {(inserted >= 0 ? $"+{inserted:N0}" : "done")} ({idx + 1}/{sources.Count})");
            }

            await ExecAsync(output, $"DETACH {alias};", ct);
        }

        // If any source returned -1, recount the totals
        if (totalEvents == 0 && sources.Any(s => File.Exists(Path.Combine(s.Directory, "events.duckdb"))))
        {
            await using var countCmd = output.CreateCommand();
            countCmd.CommandText = "SELECT COUNT(*) FROM events;";
            var scalar = await countCmd.ExecuteScalarAsync(ct);
            if (scalar is not null)
                totalEvents = Convert.ToInt64(scalar);
        }

        // 3. Build events-only indexes
        await ExecAsync(output, "CREATE INDEX IF NOT EXISTS idx_events_trace ON events(trace_id);", ct);
        await ExecAsync(output, "CREATE INDEX IF NOT EXISTS idx_events_parent ON events(parent_event_id);", ct);
        await ExecAsync(output, "CREATE INDEX IF NOT EXISTS idx_events_entity ON events(entity_id);", ct);
        await ExecAsync(output, "CREATE INDEX IF NOT EXISTS idx_events_player ON events(owning_player_id);", ct);
        await ExecAsync(output, "CREATE INDEX IF NOT EXISTS idx_events_topic_time ON events(topic, publish_wallclock);", ct);

        // 4. CHECKPOINT to flush WAL
        await ExecAsync(output, "CHECKPOINT;", ct);

        return new EventsConsolidationStats(TotalEvents: totalEvents);
    }

    private static async Task ExecAsync(DuckDBConnection conn, string sql, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static string EscapeSql(string s) => s.Replace("'", "''");
    private static string EscapeConnStr(string s) => s; // DuckDB connection string uses raw path
}
