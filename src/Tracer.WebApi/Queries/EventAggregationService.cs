using DuckDB.NET.Data;
using Microsoft.Extensions.Logging;
using Tracer.Core.Time;
using Tracer.Storage.DuckDB.MultiInterval;
using Tracer.WebApi.Contracts.Dto;

namespace Tracer.WebApi.Queries;

public enum AggregateGroupBy { Node, Topic, Severity, None }

public sealed record AggregateQuery : IEventFilter
{
    public required string SessionId { get; init; }
    public required WallclockTime From { get; init; }
    public required WallclockTime To { get; init; }
    public required string BucketDuration { get; init; }
    public AggregateGroupBy GroupBy { get; init; } = AggregateGroupBy.Node;
    public IReadOnlyList<string>? Topics { get; init; }
    public IReadOnlyList<string>? Nodes { get; init; }
    public string? TraceId { get; init; }
    public IReadOnlyList<string>? EntityIds { get; init; }
    public IReadOnlyList<string>? PlayerIds { get; init; }
    public IReadOnlyList<string>? Severities { get; init; }
    public bool NotablesOnly { get; init; }
}

public sealed record AggregateBucket(DateTimeOffset BucketStartUtc, IReadOnlyList<AggregateGroup> Groups, long Total);
public sealed record AggregateGroup(string? GroupKey, long Count);

public sealed record AggregateResult
{
    public required string BucketDuration { get; init; }
    public required IReadOnlyList<AggregateBucket> Buckets { get; init; }
}

public sealed class EventAggregationService(LiveMultiIntervalReader reader, ILogger<EventAggregationService> logger)
{
    private readonly LiveMultiIntervalReader _reader = reader;
    private readonly ILogger<EventAggregationService> _logger = logger;

    private static readonly IReadOnlyDictionary<string, string> DuckDbIntervals =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["100ms"] = "100 milliseconds",
            ["1s"]    = "1 second",
            ["5s"]    = "5 seconds",
            ["30s"]   = "30 seconds",
            ["1m"]    = "1 minute",
            ["5m"]    = "5 minutes",
            ["30m"]   = "30 minutes",
            ["1h"]    = "1 hour",
        };

    public async Task<AggregateResult> AggregateAsync(AggregateQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!DuckDbIntervals.TryGetValue(query.BucketDuration, out var interval))
            throw new ArgumentException(
                $"Invalid bucketDuration '{query.BucketDuration}'. " +
                $"Allowed values: {string.Join(", ", DuckDbIntervals.Keys)}",
                nameof(query));

        var groupByExpr = query.GroupBy switch
        {
            AggregateGroupBy.Node     => "publisher_node",
            AggregateGroupBy.Topic    => "topic",
            AggregateGroupBy.Severity => "severity",
            AggregateGroupBy.None     => "NULL",
            _                         => "publisher_node",
        };

        await using var pooled = await _reader.AcquireAsync(ct);

        // Build WHERE clause including time-range and all filters
        var (whereSql, _) = QueryPredicateBuilder.Build(query, includeTimeRange: true);

        var aggSql = pooled.WithEventsCte($"""
            SELECT
                time_bucket(INTERVAL '{interval}', publish_wallclock) AS bucket_start,
                {groupByExpr} AS group_key,
                COUNT(*) AS cnt
            FROM events {whereSql}
            GROUP BY bucket_start, group_key
            ORDER BY bucket_start, group_key
            """);

        var bucketMap = new SortedDictionary<DateTime, List<AggregateGroup>>();

        using (var cmd = pooled.Connection.CreateCommand())
        {
            cmd.CommandText = aggSql;
            cmd.Parameters.Add(new DuckDBParameter("from", query.From.ToDateTimeOffset().UtcDateTime));
            cmd.Parameters.Add(new DuckDBParameter("to", query.To.ToDateTimeOffset().UtcDateTime));
            QueryPredicateBuilder.BindParameters(cmd, query);

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var bucketDt = (DateTime)r.GetValue(0);
                var groupKey = r.IsDBNull(1) ? null : r.GetString(1);
                var count = Convert.ToInt64(r.GetValue(2));

                if (!bucketMap.TryGetValue(bucketDt, out var groups))
                {
                    groups = new List<AggregateGroup>();
                    bucketMap[bucketDt] = groups;
                }
                groups.Add(new AggregateGroup(groupKey, count));
            }
        }

        var buckets = bucketMap
            .Select(kv =>
            {
                var total = kv.Value.Sum(g => g.Count);
                return new AggregateBucket(
                    new DateTimeOffset(kv.Key, TimeSpan.Zero),
                    kv.Value.AsReadOnly(),
                    total);
            })
            .ToList();

        return new AggregateResult
        {
            BucketDuration = query.BucketDuration,
            Buckets = buckets,
        };
    }
}
