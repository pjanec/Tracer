using DuckDB.NET.Data;
using Microsoft.Extensions.Logging;
using Tracer.Core.Time;
using Tracer.Storage.DuckDB.MultiInterval;
using Tracer.WebApi.Util;

namespace Tracer.WebApi.Queries;

public sealed record LatencyQuery
{
    public required WallclockTime From { get; init; }
    public required WallclockTime To { get; init; }
    public string? Topic { get; init; }
    public string? PublisherNode { get; init; }
    public string? SubscriberNode { get; init; }
    public bool ExcludeSelfSubscribe { get; init; } = true;
}

public sealed record LatencyDistribution
{
    public required long SampleCount { get; init; }
    public required double P50Ms { get; init; }
    public required double P90Ms { get; init; }
    public required double P99Ms { get; init; }
    public required double P999Ms { get; init; }
    public required double MaxMs { get; init; }
    public required double MinMs { get; init; }
    public required double MeanMs { get; init; }
    public required double StddevMs { get; init; }
    public required IReadOnlyList<HistogramBucket> Buckets { get; init; }
}

public sealed record LatencyPairSummary
{
    public required string Topic { get; init; }
    public required string PublisherNode { get; init; }
    public required string SubscriberNode { get; init; }
    public required long SampleCount { get; init; }
    public required double P50Ms { get; init; }
    public required double P99Ms { get; init; }
    public required double MaxMs { get; init; }
}

public sealed class LatencyDistributionService(LiveMultiIntervalReader reader)
{
    private readonly LiveMultiIntervalReader _reader = reader;

    public async Task<LatencyDistribution> GetAsync(LatencyQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        await using var pooled = await _reader.AcquireAsync(ct);

        var (whereSql, hasWhere) = BuildWhere(query);

        // Aggregate query
        var aggregateSql = pooled.WithEventsCte($"""
            SELECT
                COUNT(*) AS sample_count,
                APPROX_QUANTILE((EXTRACT(EPOCH FROM (receive_wallclock - publish_wallclock)) * 1000.0), 0.50) AS p50,
                APPROX_QUANTILE((EXTRACT(EPOCH FROM (receive_wallclock - publish_wallclock)) * 1000.0), 0.90) AS p90,
                APPROX_QUANTILE((EXTRACT(EPOCH FROM (receive_wallclock - publish_wallclock)) * 1000.0), 0.99) AS p99,
                APPROX_QUANTILE((EXTRACT(EPOCH FROM (receive_wallclock - publish_wallclock)) * 1000.0), 0.999) AS p999,
                MAX((EXTRACT(EPOCH FROM (receive_wallclock - publish_wallclock)) * 1000.0)) AS max_ms,
                MIN((EXTRACT(EPOCH FROM (receive_wallclock - publish_wallclock)) * 1000.0)) AS min_ms,
                AVG((EXTRACT(EPOCH FROM (receive_wallclock - publish_wallclock)) * 1000.0)) AS mean_ms,
                STDDEV_POP((EXTRACT(EPOCH FROM (receive_wallclock - publish_wallclock)) * 1000.0)) AS stddev_ms
            FROM events
            {whereSql}
            """);

        long sampleCount = 0;
        double p50 = 0, p90 = 0, p99 = 0, p999 = 0, maxMs = 0, minMs = 0, meanMs = 0, stddevMs = 0;

        using (var cmd = pooled.Connection.CreateCommand())
        {
            cmd.CommandText = aggregateSql;
            BindParams(cmd, query);
            using var r = cmd.ExecuteReader();
            if (r.Read() && !r.IsDBNull(0))
            {
                sampleCount = Convert.ToInt64(r.GetValue(0));
                p50 = ReadDouble(r, 1);
                p90 = ReadDouble(r, 2);
                p99 = ReadDouble(r, 3);
                p999 = ReadDouble(r, 4);
                maxMs = ReadDouble(r, 5);
                minMs = ReadDouble(r, 6);
                meanMs = ReadDouble(r, 7);
                stddevMs = ReadDouble(r, 8);
            }
        }

        if (sampleCount == 0)
        {
            return new LatencyDistribution
            {
                SampleCount = 0,
                P50Ms = 0, P90Ms = 0, P99Ms = 0, P999Ms = 0,
                MaxMs = 0, MinMs = 0, MeanMs = 0, StddevMs = 0,
                Buckets = []
            };
        }

        // Histogram query
        var histSql = pooled.WithEventsCte($"""
            SELECT
                CAST(FLOOR(LOG2(GREATEST((EXTRACT(EPOCH FROM (receive_wallclock - publish_wallclock)) * 1000.0), 0.001)) * 4) AS BIGINT) AS bucket_index,
                COUNT(*) AS cnt
            FROM events
            {whereSql}
            GROUP BY bucket_index
            ORDER BY bucket_index
            """);

        var buckets = new List<HistogramBucket>();
        using (var cmd = pooled.Connection.CreateCommand())
        {
            cmd.CommandText = histSql;
            BindParams(cmd, query);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var idx = Convert.ToInt64(r.GetValue(0));
                var cnt = Convert.ToInt64(r.GetValue(1));
                var (lowMs, highMs) = HistogramSink.BucketBounds(idx);
                buckets.Add(new HistogramBucket(idx, lowMs, highMs, cnt));
            }
        }

        return new LatencyDistribution
        {
            SampleCount = sampleCount,
            P50Ms = p50,
            P90Ms = p90,
            P99Ms = p99,
            P999Ms = p999,
            MaxMs = maxMs,
            MinMs = minMs,
            MeanMs = meanMs,
            StddevMs = stddevMs,
            Buckets = buckets,
        };
    }

    public async Task<IReadOnlyList<LatencyPairSummary>> ListByPairAsync(
        WallclockTime from,
        WallclockTime to,
        int minSamples,
        int limit,
        CancellationToken ct)
    {
        await using var pooled = await _reader.AcquireAsync(ct);

        var sql = pooled.WithEventsCte("""
            SELECT
                topic, publisher_node, subscriber_node,
                COUNT(*) AS sample_count,
                APPROX_QUANTILE(latency_ms, 0.50) AS p50,
                APPROX_QUANTILE(latency_ms, 0.99) AS p99,
                MAX(latency_ms) AS max_ms
            FROM (
                SELECT topic, publisher_node, subscriber_node,
                    (EXTRACT(EPOCH FROM (receive_wallclock - publish_wallclock)) * 1000.0) AS latency_ms
                FROM events
                WHERE publish_wallclock >= $from
                  AND publish_wallclock < $to
                  AND publisher_node != subscriber_node
            ) sub
            GROUP BY topic, publisher_node, subscriber_node
            HAVING COUNT(*) >= $minSamples
            ORDER BY p99 DESC
            LIMIT $limit
            """);

        using var cmd = pooled.Connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new DuckDBParameter("from", from.ToDateTimeOffset().UtcDateTime));
        cmd.Parameters.Add(new DuckDBParameter("to", to.ToDateTimeOffset().UtcDateTime));
        cmd.Parameters.Add(new DuckDBParameter("minSamples", (long)minSamples));
        cmd.Parameters.Add(new DuckDBParameter("limit", limit));

        var results = new List<LatencyPairSummary>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            results.Add(new LatencyPairSummary
            {
                Topic = r.GetString(0),
                PublisherNode = r.GetString(1),
                SubscriberNode = r.GetString(2),
                SampleCount = Convert.ToInt64(r.GetValue(3)),
                P50Ms = ReadDouble(r, 4),
                P99Ms = ReadDouble(r, 5),
                MaxMs = ReadDouble(r, 6),
            });
        }

        return results;
    }

    private static (string whereSql, bool hasWhere) BuildWhere(LatencyQuery query)
    {
        var parts = new List<string>
        {
            "publish_wallclock >= $from",
            "publish_wallclock < $to"
        };

        if (query.ExcludeSelfSubscribe)
            parts.Add("publisher_node != subscriber_node");

        if (query.Topic is not null)
            parts.Add("topic = $topic");

        if (query.PublisherNode is not null)
            parts.Add("publisher_node = $pub");

        if (query.SubscriberNode is not null)
            parts.Add("subscriber_node = $sub");

        var sql = "WHERE " + string.Join(" AND ", parts);
        return (sql, true);
    }

    private static void BindParams(DuckDBCommand cmd, LatencyQuery query)
    {
        cmd.Parameters.Add(new DuckDBParameter("from", query.From.ToDateTimeOffset().UtcDateTime));
        cmd.Parameters.Add(new DuckDBParameter("to", query.To.ToDateTimeOffset().UtcDateTime));
        if (query.Topic is not null)
            cmd.Parameters.Add(new DuckDBParameter("topic", query.Topic));
        if (query.PublisherNode is not null)
            cmd.Parameters.Add(new DuckDBParameter("pub", query.PublisherNode));
        if (query.SubscriberNode is not null)
            cmd.Parameters.Add(new DuckDBParameter("sub", query.SubscriberNode));
    }

    private static double ReadDouble(System.Data.IDataReader r, int idx)
    {
        if (r.IsDBNull(idx)) return 0.0;
        var val = r.GetValue(idx);
        return val is double d ? d : Convert.ToDouble(val);
    }
}
