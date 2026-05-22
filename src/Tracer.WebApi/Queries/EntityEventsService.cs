using DuckDB.NET.Data;
using Microsoft.Extensions.Logging;
using Tracer.Core.Records;
using Tracer.Core.Time;
using Tracer.Storage.DuckDB.MultiInterval;

namespace Tracer.WebApi.Queries;

public sealed record EntityEventsResult
{
    public required string EntityId { get; init; }
    public required IReadOnlyList<EventRecord> Events { get; init; }
    public required bool Truncated { get; init; }
}

public sealed class EntityEventsService(LiveMultiIntervalReader reader, ILogger<EntityEventsService> logger)
{
    public async Task<EntityEventsResult> GetEventsAsync(
        string entityId,
        WallclockTime from,
        WallclockTime to,
        int limit,
        CancellationToken ct)
    {
        await using var pooled = await reader.AcquireAsync(ct);

        var sql = pooled.WithEventsCte($"""
            SELECT event_id, trace_id, parent_event_id, sequence_number,
                   publish_wallclock, receive_wallclock, publisher_node, subscriber_node,
                   topic, entity_id, owning_player_id, scenario_phase, severity, notable_label, payload
            FROM events
            WHERE entity_id = $entityId
              AND publish_wallclock >= $from
              AND publish_wallclock < $to
            ORDER BY publish_wallclock
            LIMIT $limitPlus1
            """);

        using var cmd = pooled.Connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new DuckDBParameter("entityId", entityId));
        cmd.Parameters.Add(new DuckDBParameter("from", from.ToDateTimeOffset().UtcDateTime));
        cmd.Parameters.Add(new DuckDBParameter("to", to.ToDateTimeOffset().UtcDateTime));
        cmd.Parameters.Add(new DuckDBParameter("limitPlus1", limit + 1));

        var events = new List<EventRecord>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            events.Add(EventRecordMapper.FromReader(r));

        var truncated = events.Count > limit;
        if (truncated) events.RemoveAt(events.Count - 1);

        logger.LogDebug("GetEventsAsync returned {Count} events for entity {EntityId}", events.Count, entityId);
        return new EntityEventsResult { EntityId = entityId, Events = events, Truncated = truncated };
    }
}
