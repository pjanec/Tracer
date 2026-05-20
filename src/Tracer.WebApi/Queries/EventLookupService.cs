using System.Globalization;
using Tracer.Core.Identity;
using Tracer.Core.Time;
using Tracer.WebApi.Contracts.Dto;
using Tracer.WebApi.Lifecycle;

namespace Tracer.WebApi.Queries;

public sealed class EventLookupService(ReadOnlyConnectionPool pool)
{
    private readonly ReadOnlyConnectionPool _pool = pool;

    public async Task<EventDto?> GetByIdAsync(string eventIdHex, CancellationToken ct)
    {
        if (!ulong.TryParse(eventIdHex, NumberStyles.HexNumber, null, out var rawId))
            return null;

        await using var pooled = await _pool.AcquireAsync(ct);
        var conn = pooled.Connection;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT event_id, trace_id, parent_event_id, sequence_number,
                   publish_wallclock, receive_wallclock, publisher_node, subscriber_node,
                   topic, entity_id, owning_player_id, scenario_phase, severity, notable_label, payload
            FROM events
            WHERE event_id = $id
            LIMIT 1
            """;
        var p = cmd.CreateParameter();
        p.ParameterName = "id";
        p.Value = rawId;
        cmd.Parameters.Add(p);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;

        var eventIdVal = Convert.ToUInt64(reader.GetValue(0));
        var traceIdVal = Convert.ToUInt64(reader.GetValue(1));
        var parentRaw = reader.IsDBNull(2) ? (ulong?)null : Convert.ToUInt64(reader.GetValue(2));
        var seqNum = Convert.ToUInt64(reader.GetValue(3));
        var dt = (DateTime)reader.GetValue(4);

        return new EventDto
        {
            EventId = new EventId(eventIdVal).ToString(),
            TraceId = new TraceId(traceIdVal).ToString(),
            ParentEventId = parentRaw.HasValue ? new EventId(parentRaw.Value).ToString() : null,
            SequenceNumber = (long)seqNum,
            OccurredAtUtc = new DateTimeOffset(dt, TimeSpan.Zero),
            PublisherNode = reader.GetString(6),
            SubscriberNode = reader.GetString(7),
            Topic = reader.GetString(8),
            EntityId = reader.IsDBNull(9) ? null : reader.GetString(9),
            OwningPlayerId = reader.IsDBNull(10) ? null : reader.GetString(10),
            ScenarioPhase = reader.IsDBNull(11) ? null : reader.GetString(11),
            Severity = reader.IsDBNull(12) ? null : reader.GetString(12),
            NotableLabel = reader.IsDBNull(13) ? null : reader.GetString(13),
            PayloadJson = reader.IsDBNull(14) ? null : reader.GetString(14),
        };
    }
}

