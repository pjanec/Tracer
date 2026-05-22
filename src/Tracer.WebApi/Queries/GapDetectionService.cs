using DuckDB.NET.Data;
using Tracer.Core.Time;
using Tracer.Storage.DuckDB.MultiInterval;

namespace Tracer.WebApi.Queries;

public sealed record GapDetectionQuery
{
    public required WallclockTime From { get; init; }
    public required WallclockTime To { get; init; }
    public string? Topic { get; init; }
    public string? PublisherNode { get; init; }
    public string? SubscriberNode { get; init; }
    public int Limit { get; init; } = 500;
}

public sealed record Gap
{
    public required string Topic { get; init; }
    public required string PublisherNode { get; init; }
    public required string SubscriberNode { get; init; }
    public required ulong ResumedAtSequence { get; init; }
    public required ulong PreviousSequence { get; init; }
    public required ulong MissingCount { get; init; }
    public required DateTimeOffset ResumedAtWallclockUtc { get; init; }
}

public sealed record GapDetectionResult
{
    public required IReadOnlyList<Gap> Gaps { get; init; }
    public required long TotalGaps { get; init; }
}

public sealed class GapDetectionService(LiveMultiIntervalReader reader)
{
    private readonly LiveMultiIntervalReader _reader = reader;

    public async Task<GapDetectionResult> GetGapsAsync(GapDetectionQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        await using var pooled = await _reader.AcquireAsync(ct);

        var extraFilter = new System.Text.StringBuilder();
        if (query.Topic is not null) extraFilter.Append(" AND topic = $topic");
        if (query.PublisherNode is not null) extraFilter.Append(" AND publisher_node = $pub");
        if (query.SubscriberNode is not null) extraFilter.Append(" AND subscriber_node = $sub");

        // Use BuildEventsUnionSql to chain multiple CTEs (events + ordered)
        var fullSql = $"""
            WITH events AS (
            {pooled.BuildEventsUnionSql()}
            ),
            ordered AS (
                SELECT
                    topic, publisher_node, subscriber_node, sequence_number, publish_wallclock,
                    LAG(sequence_number) OVER (
                        PARTITION BY topic, publisher_node, subscriber_node
                        ORDER BY sequence_number
                    ) AS prev_seq
                FROM events
                WHERE publish_wallclock >= $from
                  AND publish_wallclock < $to
                  AND publisher_node != subscriber_node
                  {extraFilter}
            )
            SELECT
                topic, publisher_node, subscriber_node,
                CAST(sequence_number AS UBIGINT) AS resumed_at_seq,
                CAST(COALESCE(prev_seq, 0) AS UBIGINT) AS prev_seq_out,
                CAST(sequence_number - COALESCE(prev_seq, sequence_number) AS UBIGINT) - 1 AS missing_count_raw,
                publish_wallclock
            FROM ordered
            WHERE sequence_number - COALESCE(prev_seq, 0) > 1
              AND (prev_seq IS NOT NULL OR sequence_number > 1)
            ORDER BY missing_count_raw DESC, publish_wallclock
            LIMIT $limit
            """;

        using var cmd = pooled.Connection.CreateCommand();
        cmd.CommandText = fullSql;
        cmd.Parameters.Add(new DuckDBParameter("from", query.From.ToDateTimeOffset().UtcDateTime));
        cmd.Parameters.Add(new DuckDBParameter("to", query.To.ToDateTimeOffset().UtcDateTime));
        cmd.Parameters.Add(new DuckDBParameter("limit", query.Limit));
        if (query.Topic is not null) cmd.Parameters.Add(new DuckDBParameter("topic", query.Topic));
        if (query.PublisherNode is not null) cmd.Parameters.Add(new DuckDBParameter("pub", query.PublisherNode));
        if (query.SubscriberNode is not null) cmd.Parameters.Add(new DuckDBParameter("sub", query.SubscriberNode));

        var gaps = new List<Gap>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var topic = r.GetString(0);
            var pub = r.GetString(1);
            var sub = r.GetString(2);
            var resumedAt = Convert.ToUInt64(r.GetValue(3));
            var prevSeq = Convert.ToUInt64(r.GetValue(4));
            var missing = Convert.ToUInt64(r.GetValue(5));
            var wallclock = (DateTime)r.GetValue(6);

            gaps.Add(new Gap
            {
                Topic = topic,
                PublisherNode = pub,
                SubscriberNode = sub,
                ResumedAtSequence = resumedAt,
                PreviousSequence = prevSeq,
                MissingCount = missing,
                ResumedAtWallclockUtc = new DateTimeOffset(wallclock, TimeSpan.Zero),
            });
        }

        return new GapDetectionResult
        {
            Gaps = gaps,
            TotalGaps = gaps.Count,
        };
    }
}
