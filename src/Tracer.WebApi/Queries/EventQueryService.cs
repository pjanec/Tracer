using DuckDB.NET.Data;
using Microsoft.Extensions.Logging;
using Tracer.Core.Identity;
using Tracer.Core.Time;
using Tracer.Storage.DuckDB.MultiInterval;
using Tracer.WebApi.Contracts.Dto;

namespace Tracer.WebApi.Queries;

public sealed record EventQuery : IEventFilter
{
    public required string SessionId { get; init; }
    public required WallclockTime From { get; init; }
    public required WallclockTime To { get; init; }
    public IReadOnlyList<string>? Topics { get; init; }
    public IReadOnlyList<string>? Nodes { get; init; }
    public string? TraceId { get; init; }
    public IReadOnlyList<string>? EntityIds { get; init; }
    public IReadOnlyList<string>? PlayerIds { get; init; }
    public IReadOnlyList<string>? Severities { get; init; }
    public bool NotablesOnly { get; init; }
    public int Limit { get; init; } = 5000;
    public bool OrderDescending { get; init; } = false;
}

public sealed record EventListResult
{
    public required IReadOnlyList<EventDto> Events { get; init; }
    public required long TotalMatching { get; init; }
    public required int Returned { get; init; }
    public required bool Truncated { get; init; }
}

public sealed class EventQueryService(LiveMultiIntervalReader reader, ILogger<EventQueryService> logger)
{
    private readonly LiveMultiIntervalReader _reader = reader;
    private readonly ILogger<EventQueryService> _logger = logger;

    public async Task<EventListResult> ListAsync(EventQuery query, CancellationToken ct)
    {
        await using var pooled = await _reader.AcquireAsync(ct);

        // Build WHERE clause including time-range and all filters
        var (whereSql, _) = QueryPredicateBuilder.Build(query, includeTimeRange: true);
        var orderDir = query.OrderDescending ? "DESC" : "ASC";

        // List query — uses WithEventsCte which wraps with "WITH events AS (UNION ALL)"
        var listSql = pooled.WithEventsCte($"""
            SELECT event_id, trace_id, parent_event_id, sequence_number,
                   publish_wallclock, receive_wallclock, publisher_node, subscriber_node,
                   topic, entity_id, owning_player_id, scenario_phase, severity, notable_label, payload
            FROM events {whereSql}
            ORDER BY publish_wallclock {orderDir}
            LIMIT $limit
            """);

        var events = new List<EventDto>();
        using (var cmd = pooled.Connection.CreateCommand())
        {
            cmd.CommandText = listSql;
            cmd.Parameters.Add(new DuckDBParameter("from", query.From.ToDateTimeOffset().UtcDateTime));
            cmd.Parameters.Add(new DuckDBParameter("to", query.To.ToDateTimeOffset().UtcDateTime));
            cmd.Parameters.Add(new DuckDBParameter("limit", query.Limit));
            QueryPredicateBuilder.BindParameters(cmd, query);

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var eventIdVal = Convert.ToUInt64(r.GetValue(0));
                var traceIdVal = Convert.ToUInt64(r.GetValue(1));
                var parentRaw = r.IsDBNull(2) ? (ulong?)null : Convert.ToUInt64(r.GetValue(2));
                var seqNum = Convert.ToUInt64(r.GetValue(3));
                var dt = (DateTime)r.GetValue(4);

                events.Add(new EventDto
                {
                    EventId = new Tracer.Core.Identity.EventId(eventIdVal).ToString(),
                    TraceId = new TraceId(traceIdVal).ToString(),
                    ParentEventId = parentRaw.HasValue ? new Tracer.Core.Identity.EventId(parentRaw.Value).ToString() : null,
                    SequenceNumber = (long)seqNum,
                    OccurredAtUtc = new DateTimeOffset(dt, TimeSpan.Zero),
                    PublisherNode = r.GetString(6),
                    SubscriberNode = r.GetString(7),
                    Topic = r.GetString(8),
                    EntityId = r.IsDBNull(9) ? null : r.GetString(9),
                    OwningPlayerId = r.IsDBNull(10) ? null : r.GetString(10),
                    ScenarioPhase = r.IsDBNull(11) ? null : r.GetString(11),
                    Severity = r.IsDBNull(12) ? null : r.GetString(12),
                    NotableLabel = r.IsDBNull(13) ? null : r.GetString(13),
                    PayloadJson = r.IsDBNull(14) ? null : r.GetString(14),
                });
            }
        }

        // Count query
        var countSql = pooled.WithEventsCte($"SELECT COUNT(*) FROM events {whereSql}");

        long totalMatching = 0;
        using (var cmd = pooled.Connection.CreateCommand())
        {
            cmd.CommandText = countSql;
            cmd.Parameters.Add(new DuckDBParameter("from", query.From.ToDateTimeOffset().UtcDateTime));
            cmd.Parameters.Add(new DuckDBParameter("to", query.To.ToDateTimeOffset().UtcDateTime));
            QueryPredicateBuilder.BindParameters(cmd, query);

            using var r = cmd.ExecuteReader();
            if (r.Read())
                totalMatching = Convert.ToInt64(r.GetValue(0));
        }

        return new EventListResult
        {
            Events = events,
            TotalMatching = totalMatching,
            Returned = events.Count,
            Truncated = events.Count < totalMatching,
        };
    }
}
