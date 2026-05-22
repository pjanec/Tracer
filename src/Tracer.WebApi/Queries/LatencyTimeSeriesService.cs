using DuckDB.NET.Data;
using Tracer.Core.Time;
using Tracer.Storage.DuckDB.MultiInterval;

namespace Tracer.WebApi.Queries;

public sealed record LatencyTimeSeriesQuery
{
    public required WallclockTime From { get; init; }
    public required WallclockTime To { get; init; }
    public string? Topic { get; init; }
    public string? PublisherNode { get; init; }
    public string? SubscriberNode { get; init; }
    public bool ExcludeSelfSubscribe { get; init; } = true;
}

public sealed record LatencyTimePoint(DateTimeOffset BucketStartUtc, double P50Ms, double P99Ms, long SampleCount);
public sealed record LatencyTimeSeries(string BucketSize, IReadOnlyList<LatencyTimePoint> Points);

public sealed class LatencyTimeSeriesService(LiveMultiIntervalReader reader)
{
    private readonly LiveMultiIntervalReader _reader = reader;

    public async Task<LatencyTimeSeries> GetAsync(LatencyTimeSeriesQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        await using var pooled = await _reader.AcquireAsync(ct);

        var from = query.From.ToDateTimeOffset();
        var to = query.To.ToDateTimeOffset();
        var span = to - from;

        var (bucketLabel, bucketSql) = ChooseBucket(span);

        var whereParts = new List<string>
        {
            "publish_wallclock >= $from",
            "publish_wallclock < $to",
        };
        if (query.ExcludeSelfSubscribe) whereParts.Add("publisher_node != subscriber_node");
        if (query.Topic is not null) whereParts.Add("topic = $topic");
        if (query.PublisherNode is not null) whereParts.Add("publisher_node = $pub");
        if (query.SubscriberNode is not null) whereParts.Add("subscriber_node = $sub");
        var where = "WHERE " + string.Join(" AND ", whereParts);

        var sql = pooled.WithEventsCte($"""
            SELECT
                {bucketSql} AS bucket_start,
                APPROX_QUANTILE((EXTRACT(EPOCH FROM (receive_wallclock - publish_wallclock)) * 1000.0), 0.50::FLOAT) AS p50,
                APPROX_QUANTILE((EXTRACT(EPOCH FROM (receive_wallclock - publish_wallclock)) * 1000.0), 0.99::FLOAT) AS p99,
                COUNT(*) AS sample_count
            FROM events
            {where}
            GROUP BY bucket_start
            ORDER BY bucket_start
            """);

        using var cmd = pooled.Connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new DuckDBParameter("from", from.UtcDateTime));
        cmd.Parameters.Add(new DuckDBParameter("to", to.UtcDateTime));
        if (query.Topic is not null) cmd.Parameters.Add(new DuckDBParameter("topic", query.Topic));
        if (query.PublisherNode is not null) cmd.Parameters.Add(new DuckDBParameter("pub", query.PublisherNode));
        if (query.SubscriberNode is not null) cmd.Parameters.Add(new DuckDBParameter("sub", query.SubscriberNode));

        var points = new List<LatencyTimePoint>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var bucketDt = (DateTime)r.GetValue(0);
            var p50 = ReadDouble(r, 1);
            var p99 = ReadDouble(r, 2);
            var count = Convert.ToInt64(r.GetValue(3));
            points.Add(new LatencyTimePoint(
                new DateTimeOffset(bucketDt, TimeSpan.Zero),
                p50,
                p99,
                count));
        }

        return new LatencyTimeSeries(bucketLabel, points);
    }

    internal static (string label, string sql) ChooseBucket(TimeSpan span)
    {
        if (span >= TimeSpan.FromHours(4))
            return ("5 minutes", "TIME_BUCKET(INTERVAL '5 minutes', publish_wallclock)");
        if (span >= TimeSpan.FromHours(1))
            return ("1 minute", "TIME_BUCKET(INTERVAL '1 minute', publish_wallclock)");
        if (span >= TimeSpan.FromMinutes(30))
            return ("30 seconds", "TIME_BUCKET(INTERVAL '30 seconds', publish_wallclock)");
        if (span >= TimeSpan.FromMinutes(5))
            return ("10 seconds", "TIME_BUCKET(INTERVAL '10 seconds', publish_wallclock)");
        if (span >= TimeSpan.FromMinutes(1))
            return ("1 second", "TIME_BUCKET(INTERVAL '1 second', publish_wallclock)");
        return ("100 milliseconds", "TIME_BUCKET(INTERVAL '100 milliseconds', publish_wallclock)");
    }

    private static double ReadDouble(System.Data.IDataReader r, int idx)
    {
        if (r.IsDBNull(idx)) return 0.0;
        var val = r.GetValue(idx);
        return val is double d ? d : Convert.ToDouble(val);
    }
}
