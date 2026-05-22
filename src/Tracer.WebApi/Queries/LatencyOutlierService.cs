using DuckDB.NET.Data;
using Tracer.Core.Domain;
using Tracer.Core.Identity;
using Tracer.Core.Time;
using Tracer.Storage.DuckDB.MultiInterval;

namespace Tracer.WebApi.Queries;

public sealed record LatencyOutlierQuery
{
    public required WallclockTime From { get; init; }
    public required WallclockTime To { get; init; }
    public string? Topic { get; init; }
    public double? ThresholdMs { get; init; }
    public int Limit { get; init; } = 100;
}

public sealed record LatencyOutlier
{
    public required string EventId { get; init; }
    public required string Topic { get; init; }
    public required string PublisherNode { get; init; }
    public required string SubscriberNode { get; init; }
    public required DateTimeOffset PublishWallclockUtc { get; init; }
    public required DateTimeOffset ReceiveWallclockUtc { get; init; }
    public required double LatencyMs { get; init; }
    public required double ThresholdMs { get; init; }
    public required string BudgetSource { get; init; } // "budget" | "top-0.1%"
}

public sealed record LatencyOutlierResult
{
    public required IReadOnlyList<LatencyOutlier> Outliers { get; init; }
    public required IReadOnlyList<LatencyBudget> BudgetsUsed { get; init; }
}

public sealed class LatencyOutlierService(LiveMultiIntervalReader reader, BudgetService budgetService)
{
    private readonly LiveMultiIntervalReader _reader = reader;
    private readonly BudgetService _budgetService = budgetService;

    public async Task<LatencyOutlierResult> GetOutliersAsync(LatencyOutlierQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        // If explicit threshold: simple case
        if (query.ThresholdMs.HasValue)
            return await GetWithExplicitThresholdAsync(query, query.ThresholdMs.Value, ct);

        // No explicit threshold — use budgets or p99.9 fallback
        var budgets = await _budgetService.GetBudgetsAsync("", ct);
        var budgetMap = budgets
            .Where(b => b.AbsoluteMaxMs.HasValue)
            .ToDictionary(b => b.Topic, b => b);

        return await GetWithPerTopicThresholdAsync(query, budgetMap, budgets, ct);
    }

    private async Task<LatencyOutlierResult> GetWithExplicitThresholdAsync(
        LatencyOutlierQuery query,
        double thresholdMs,
        CancellationToken ct)
    {
        await using var pooled = await _reader.AcquireAsync(ct);

        var topicFilter = query.Topic is not null ? "AND topic = $topic" : "";

        var sql = pooled.WithEventsCte($"""
            SELECT
                event_id, topic, publisher_node, subscriber_node,
                publish_wallclock, receive_wallclock,
                (EXTRACT(EPOCH FROM (receive_wallclock - publish_wallclock)) * 1000.0) AS latency_ms
            FROM events
            WHERE publish_wallclock >= $from
              AND publish_wallclock < $to
              AND publisher_node != subscriber_node
              {topicFilter}
              AND (EXTRACT(EPOCH FROM (receive_wallclock - publish_wallclock)) * 1000.0) > $threshold
            ORDER BY latency_ms DESC
            LIMIT $limit
            """);

        using var cmd = pooled.Connection.CreateCommand();
        cmd.CommandText = sql;
        BindBaseParams(cmd, query);
        cmd.Parameters.Add(new DuckDBParameter("threshold", thresholdMs));
        cmd.Parameters.Add(new DuckDBParameter("limit", query.Limit));

        var outliers = ReadOutliers(cmd, thresholdMs, "budget");
        return new LatencyOutlierResult { Outliers = outliers, BudgetsUsed = [] };
    }

    private async Task<LatencyOutlierResult> GetWithPerTopicThresholdAsync(
        LatencyOutlierQuery query,
        Dictionary<string, LatencyBudget> budgetMap,
        IReadOnlyList<LatencyBudget> allBudgets,
        CancellationToken ct)
    {
        // First get all unique topics in range
        var topics = await GetTopicsAsync(query, ct);

        var allOutliers = new List<LatencyOutlier>();

        foreach (var topic in topics)
        {
            double threshold;
            string source;

            if (budgetMap.TryGetValue(topic, out var budget) && budget.AbsoluteMaxMs.HasValue)
            {
                threshold = budget.AbsoluteMaxMs.Value;
                source = "budget";
            }
            else
            {
                // Compute top-0.1% threshold for this topic
                threshold = await GetPercentileThresholdAsync(query, topic, 0.999, ct);
                source = "top-0.1%";
            }

            var topicOutliers = await GetOutliersForTopicAsync(query, topic, threshold, source, ct);
            allOutliers.AddRange(topicOutliers);
        }

        // Sort by latency desc and limit
        allOutliers = allOutliers
            .OrderByDescending(o => o.LatencyMs)
            .Take(query.Limit)
            .ToList();

        return new LatencyOutlierResult
        {
            Outliers = allOutliers,
            BudgetsUsed = allBudgets,
        };
    }

    private async Task<IReadOnlyList<string>> GetTopicsAsync(LatencyOutlierQuery query, CancellationToken ct)
    {
        await using var pooled = await _reader.AcquireAsync(ct);
        var topicFilter = query.Topic is not null ? "AND topic = $topic" : "";

        var sql = pooled.WithEventsCte($"""
            SELECT DISTINCT topic
            FROM events
            WHERE publish_wallclock >= $from
              AND publish_wallclock < $to
              AND publisher_node != subscriber_node
              {topicFilter}
            """);

        using var cmd = pooled.Connection.CreateCommand();
        cmd.CommandText = sql;
        BindBaseParams(cmd, query);

        var topics = new List<string>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) topics.Add(r.GetString(0));
        return topics;
    }

    private async Task<double> GetPercentileThresholdAsync(
        LatencyOutlierQuery query, string topic, double percentile, CancellationToken ct)
    {
        await using var pooled = await _reader.AcquireAsync(ct);

        var sql = pooled.WithEventsCte("""
            SELECT APPROX_QUANTILE(
                (EXTRACT(EPOCH FROM (receive_wallclock - publish_wallclock)) * 1000.0),
                $pct::FLOAT)
            FROM events
            WHERE publish_wallclock >= $from
              AND publish_wallclock < $to
              AND publisher_node != subscriber_node
              AND topic = $topic
            """);

        using var cmd = pooled.Connection.CreateCommand();
        cmd.CommandText = sql;
        BindBaseParams(cmd, query);
        cmd.Parameters.Add(new DuckDBParameter("pct", percentile));
        cmd.Parameters.Add(new DuckDBParameter("topic", topic));

        using var r = cmd.ExecuteReader();
        if (r.Read() && !r.IsDBNull(0))
            return Convert.ToDouble(r.GetValue(0));
        return double.MaxValue;
    }

    private async Task<IReadOnlyList<LatencyOutlier>> GetOutliersForTopicAsync(
        LatencyOutlierQuery query, string topic, double threshold, string source, CancellationToken ct)
    {
        await using var pooled = await _reader.AcquireAsync(ct);

        var sql = pooled.WithEventsCte("""
            SELECT
                event_id, topic, publisher_node, subscriber_node,
                publish_wallclock, receive_wallclock,
                (EXTRACT(EPOCH FROM (receive_wallclock - publish_wallclock)) * 1000.0) AS latency_ms
            FROM events
            WHERE publish_wallclock >= $from
              AND publish_wallclock < $to
              AND publisher_node != subscriber_node
              AND topic = $topic
              AND (EXTRACT(EPOCH FROM (receive_wallclock - publish_wallclock)) * 1000.0) > $threshold
            ORDER BY latency_ms DESC
            LIMIT $limit
            """);

        using var cmd = pooled.Connection.CreateCommand();
        cmd.CommandText = sql;
        BindBaseParams(cmd, query);
        cmd.Parameters.Add(new DuckDBParameter("topic", topic));
        cmd.Parameters.Add(new DuckDBParameter("threshold", threshold));
        cmd.Parameters.Add(new DuckDBParameter("limit", query.Limit));

        return ReadOutliers(cmd, threshold, source);
    }

    private static IReadOnlyList<LatencyOutlier> ReadOutliers(
        DuckDBCommand cmd, double threshold, string source)
    {
        var results = new List<LatencyOutlier>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var eventIdRaw = Convert.ToUInt64(r.GetValue(0));
            var topic = r.GetString(1);
            var pub = r.GetString(2);
            var sub = r.GetString(3);
            var publishDt = (DateTime)r.GetValue(4);
            var receiveDt = (DateTime)r.GetValue(5);
            var latencyMs = Convert.ToDouble(r.GetValue(6));

            results.Add(new LatencyOutlier
            {
                EventId = new Core.Identity.EventId(eventIdRaw).ToString(),
                Topic = topic,
                PublisherNode = pub,
                SubscriberNode = sub,
                PublishWallclockUtc = new DateTimeOffset(publishDt, TimeSpan.Zero),
                ReceiveWallclockUtc = new DateTimeOffset(receiveDt, TimeSpan.Zero),
                LatencyMs = latencyMs,
                ThresholdMs = threshold,
                BudgetSource = source,
            });
        }
        return results;
    }

    private static void BindBaseParams(DuckDBCommand cmd, LatencyOutlierQuery query)
    {
        cmd.Parameters.Add(new DuckDBParameter("from", query.From.ToDateTimeOffset().UtcDateTime));
        cmd.Parameters.Add(new DuckDBParameter("to", query.To.ToDateTimeOffset().UtcDateTime));
        if (query.Topic is not null)
            cmd.Parameters.Add(new DuckDBParameter("topic", query.Topic));
    }
}
