using Tracer.WebApi.Contracts.Dto;
using Tracer.WebApi.Lifecycle;

namespace Tracer.WebApi.Queries;

public sealed class ScenarioQueryService(ReadOnlyConnectionPool pool)
{
    private readonly ReadOnlyConnectionPool _pool = pool;

    public async Task<DateTimeOffset?> GetEventTimestampAsync(ulong eventId, CancellationToken ct)
    {
        await using var pooled = await _pool.AcquireAsync(ct);
        var conn = pooled.Connection;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT publish_wallclock FROM events WHERE event_id = $id LIMIT 1";
        var p = cmd.CreateParameter();
        p.ParameterName = "id";
        p.Value = eventId;
        cmd.Parameters.Add(p);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        var dt = (DateTime)reader.GetValue(0);
        return new DateTimeOffset(dt, TimeSpan.Zero);
    }

    public async Task<IReadOnlyList<NotableEventDto>> GetNotablesAsync(
        string sessionId, int limit, DateTimeOffset? before, CancellationToken ct)
    {
        await using var pooled = await _pool.AcquireAsync(ct);
        var conn = pooled.Connection;

        var results = new List<NotableEventDto>();
        using var cmd = conn.CreateCommand();

        if (before.HasValue)
        {
            cmd.CommandText = """
                SELECT event_id, trace_id, parent_event_id, sequence_number,
                       publish_wallclock, receive_wallclock, publisher_node, subscriber_node,
                       topic, entity_id, owning_player_id, scenario_phase, severity, notable_label, payload
                FROM events
                WHERE json_extract_string(payload, '$.sessionId') = $sessionId
                  AND notable_label IS NOT NULL
                  AND publish_wallclock < $before
                ORDER BY publish_wallclock DESC
                LIMIT $limit
                """;
            var p1 = cmd.CreateParameter();
            p1.ParameterName = "sessionId"; p1.Value = sessionId;
            cmd.Parameters.Add(p1);
            var p2 = cmd.CreateParameter();
            p2.ParameterName = "before";
            p2.Value = before.Value.UtcDateTime;
            cmd.Parameters.Add(p2);
            var p3 = cmd.CreateParameter();
            p3.ParameterName = "limit"; p3.Value = limit;
            cmd.Parameters.Add(p3);
        }
        else
        {
            cmd.CommandText = """
                SELECT event_id, trace_id, parent_event_id, sequence_number,
                       publish_wallclock, receive_wallclock, publisher_node, subscriber_node,
                       topic, entity_id, owning_player_id, scenario_phase, severity, notable_label, payload
                FROM events
                WHERE json_extract_string(payload, '$.sessionId') = $sessionId
                  AND notable_label IS NOT NULL
                ORDER BY publish_wallclock DESC
                LIMIT $limit
                """;
            var p1 = cmd.CreateParameter();
            p1.ParameterName = "sessionId"; p1.Value = sessionId;
            cmd.Parameters.Add(p1);
            var p3 = cmd.CreateParameter();
            p3.ParameterName = "limit"; p3.Value = limit;
            cmd.Parameters.Add(p3);
        }

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var eventIdVal = Convert.ToUInt64(reader.GetValue(0));
            var traceIdVal = Convert.ToUInt64(reader.GetValue(1));
            var dt = (DateTime)reader.GetValue(4);
            results.Add(new NotableEventDto
            {
                EventId = new Tracer.Core.Identity.EventId(eventIdVal).ToString(),
                TraceId = new Tracer.Core.Identity.TraceId(traceIdVal).ToString(),
                OccurredAtUtc = new DateTimeOffset(dt, TimeSpan.Zero),
                Topic = reader.GetString(8),
                NotableLabel = reader.GetString(13),
                Severity = reader.IsDBNull(12) ? null : reader.GetString(12),
                EntityId = reader.IsDBNull(9) ? null : reader.GetString(9),
                ScenarioPhase = reader.IsDBNull(11) ? null : reader.GetString(11),
                PayloadJson = reader.IsDBNull(14) ? null : reader.GetString(14),
            });
        }

        return results;
    }

    public async Task<IReadOnlyList<ScenarioPhaseDto>> GetPhasesAsync(
        string sessionId, CancellationToken ct)
    {
        await using var pooled = await _pool.AcquireAsync(ct);
        var conn = pooled.Connection;

        // Collect phase_started events
        var starts = new List<(string Phase, DateTimeOffset StartedAt)>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT json_extract_string(payload, '$.phaseName') as phase_name,
                       publish_wallclock
                FROM events
                WHERE json_extract_string(payload, '$.sessionId') = $sessionId
                  AND topic = 'scenario.phase_started'
                ORDER BY publish_wallclock ASC
                """;
            var p = cmd.CreateParameter();
            p.ParameterName = "sessionId"; p.Value = sessionId;
            cmd.Parameters.Add(p);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var phase = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                var dt = (DateTime)reader.GetValue(1);
                starts.Add((phase, new DateTimeOffset(dt, TimeSpan.Zero)));
            }
        }

        // Collect phase_ended events
        var ends = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT json_extract_string(payload, '$.phaseName') as phase_name,
                       publish_wallclock
                FROM events
                WHERE json_extract_string(payload, '$.sessionId') = $sessionId
                  AND topic = 'scenario.phase_ended'
                ORDER BY publish_wallclock ASC
                """;
            var p = cmd.CreateParameter();
            p.ParameterName = "sessionId"; p.Value = sessionId;
            cmd.Parameters.Add(p);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var phase = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                var dt = (DateTime)reader.GetValue(1);
                ends[phase] = new DateTimeOffset(dt, TimeSpan.Zero);
            }
        }

        return starts.Select(s => new ScenarioPhaseDto
        {
            PhaseName = s.Phase,
            StartedAtUtc = s.StartedAt,
            EndedAtUtc = ends.TryGetValue(s.Phase, out var e) ? e : null,
            Status = ends.ContainsKey(s.Phase) ? "Completed" : "Active",
        }).ToList();
    }

    public async Task<ScenarioStateDto?> GetCurrentStateAsync(string sessionId, CancellationToken ct)
    {
        await using var pooled = await _pool.AcquireAsync(ct);
        var conn = pooled.Connection;

        long totalEvents = 0;
        long totalNotables = 0;
        var nodes = new List<string>();
        string? currentPhase = null;

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT COUNT(*) as total_events,
                       COUNT(CASE WHEN notable_label IS NOT NULL THEN 1 END) as notable_count,
                       array_agg(DISTINCT publisher_node) as nodes
                FROM events
                WHERE json_extract_string(payload, '$.sessionId') = $sessionId
                """;
            var p = cmd.CreateParameter();
            p.ParameterName = "sessionId"; p.Value = sessionId;
            cmd.Parameters.Add(p);
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                totalEvents = Convert.ToInt64(reader.GetValue(0));
                totalNotables = Convert.ToInt64(reader.GetValue(1));
                var nodesArr = reader.GetValue(2);
                if (nodesArr is string[] arr) nodes.AddRange(arr);
                else if (nodesArr is IEnumerable<object> objArr)
                    nodes.AddRange(objArr.Select(o => o?.ToString() ?? string.Empty));
            }
        }

        // No events for this session → return null
        if (totalEvents == 0) return null;

        // Find most recent active phase
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = """
                SELECT json_extract_string(payload, '$.phaseName') as phase_name
                FROM events
                WHERE json_extract_string(payload, '$.sessionId') = $sessionId
                  AND topic = 'scenario.phase_started'
                  AND NOT EXISTS (
                      SELECT 1 FROM events e2
                      WHERE json_extract_string(e2.payload, '$.sessionId') = $sessionId
                        AND e2.topic = 'scenario.phase_ended'
                        AND json_extract_string(e2.payload, '$.phaseName')
                            = json_extract_string(events.payload, '$.phaseName')
                  )
                ORDER BY publish_wallclock DESC
                LIMIT 1
                """;
            var p = cmd.CreateParameter();
            p.ParameterName = "sessionId"; p.Value = sessionId;
            cmd.Parameters.Add(p);
            using var reader = cmd.ExecuteReader();
            if (reader.Read() && !reader.IsDBNull(0))
                currentPhase = reader.GetString(0);
        }

        return new ScenarioStateDto
        {
            CurrentPhase = currentPhase,
            TotalEvents = totalEvents,
            TotalNotables = totalNotables,
            ParticipatingNodes = nodes,
        };
    }
}

