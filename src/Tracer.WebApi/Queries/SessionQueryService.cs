using DuckDB.NET.Data;
using Tracer.Core.Time;
using Tracer.Storage.DuckDB.MultiInterval;
using Tracer.WebApi.Contracts.Dto;

namespace Tracer.WebApi.Queries;

public sealed class SessionQueryService(LiveMultiIntervalReader multiReader)
{
    private readonly LiveMultiIntervalReader _multiReader = multiReader;

    public async Task<IReadOnlyList<SessionDto>> ListAsync(
        (DateTimeOffset From, DateTimeOffset To)? range,
        CancellationToken ct)
    {
        await using var pooled = await _multiReader.AcquireAsync(ct);
        var conn = pooled.Connection;

        // 1. Find all session_start events with their payload data
        var starts = new List<(string SessionId, DateTimeOffset StartUtc, string? ScenarioId, string? Label)>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = pooled.WithEventsCte("""
                SELECT json_extract_string(payload, '$.sessionId') as session_id,
                       publish_wallclock,
                       json_extract_string(payload, '$.scenarioId') as scenario_id,
                       json_extract_string(payload, '$.label') as label
                FROM events
                WHERE topic = 'system.session_start'
                  AND json_extract_string(payload, '$.sessionId') IS NOT NULL
                ORDER BY publish_wallclock DESC
                """);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var sessionId = reader.GetString(0);
                var dt = (DateTime)reader.GetValue(1);
                var scenarioId = reader.IsDBNull(2) ? null : reader.GetString(2);
                var label = reader.IsDBNull(3) ? null : reader.GetString(3);
                var startUtc = new DateTimeOffset(dt, TimeSpan.Zero);
                starts.Add((sessionId, startUtc, scenarioId, label));
            }
        }

        if (starts.Count == 0) return Array.Empty<SessionDto>();

        // 2. Find all session_end events
        var ends = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = pooled.WithEventsCte("""
                SELECT json_extract_string(payload, '$.sessionId') as session_id,
                       publish_wallclock
                FROM events
                WHERE topic = 'system.session_end'
                  AND json_extract_string(payload, '$.sessionId') IS NOT NULL
                """);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var sessionId = reader.GetString(0);
                var dt = (DateTime)reader.GetValue(1);
                var endUtc = new DateTimeOffset(dt, TimeSpan.Zero);
                ends[sessionId] = endUtc;
            }
        }

        var results = new List<SessionDto>();

        foreach (var (sessionId, startUtc, scenarioId, label) in starts)
        {
            // Apply time range filter
            if (range.HasValue && startUtc < range.Value.From) continue;
            if (range.HasValue && startUtc > range.Value.To) continue;

            // 3. Aggregate per-session: event count and participating nodes
            int eventCount = 0;
            var nodes = new List<string>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = pooled.WithEventsCte("""
                    SELECT COUNT(*) as event_count,
                           array_agg(DISTINCT publisher_node) as nodes
                    FROM events
                    WHERE json_extract_string(payload, '$.sessionId') = $sessionId
                    """);
                var p = cmd.CreateParameter();
                p.ParameterName = "sessionId";
                p.Value = sessionId;
                cmd.Parameters.Add(p);
                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    eventCount = Convert.ToInt32(reader.GetValue(0));
                    var nodesArr = reader.GetValue(1);
                    if (nodesArr is string[] arr) nodes.AddRange(arr);
                    else if (nodesArr is IEnumerable<object> objArr)
                        nodes.AddRange(objArr.Select(o => o?.ToString() ?? string.Empty));
                }
            }

            ends.TryGetValue(sessionId, out var endUtc2);
            results.Add(new SessionDto
            {
                SessionId = sessionId,
                StartUtc = startUtc,
                EndUtc = ends.ContainsKey(sessionId) ? endUtc2 : null,
                Status = ends.ContainsKey(sessionId) ? "Completed" : "Active",
                EventCount = eventCount,
                ParticipatingNodes = nodes,
                ScenarioId = scenarioId,
                Label = label,
            });
        }

        return results;
    }

    public async Task<SessionDto?> GetAsync(string sessionId, CancellationToken ct)
    {
        var all = await ListAsync(null, ct);
        return all.FirstOrDefault(s => s.SessionId == sessionId);
    }

    /// <summary>
    /// Returns the (Start, End) time range for the given session, or null if not found.
    /// End is null for active sessions.
    /// </summary>
    public async Task<(WallclockTime Start, WallclockTime? End)?> GetSessionTimeRangeAsync(
        string sessionId, CancellationToken ct)
    {
        await using var pooled = await _multiReader.AcquireAsync(ct);
        var conn = pooled.Connection;

        WallclockTime? start = null;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = pooled.WithEventsCte("""
                SELECT publish_wallclock
                FROM events
                WHERE topic = 'system.session_start'
                  AND json_extract_string(payload, '$.sessionId') = $sessionId
                LIMIT 1
                """);
            var p = cmd.CreateParameter();
            p.ParameterName = "sessionId";
            p.Value = sessionId;
            cmd.Parameters.Add(p);
            using var r = cmd.ExecuteReader();
            if (r.Read())
            {
                var dt = (DateTime)r.GetValue(0);
                start = WallclockTime.FromDateTimeOffset(new DateTimeOffset(dt, TimeSpan.Zero));
            }
        }

        if (start is null) return null;

        WallclockTime? end = null;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = pooled.WithEventsCte("""
                SELECT publish_wallclock
                FROM events
                WHERE topic = 'system.session_end'
                  AND json_extract_string(payload, '$.sessionId') = $sessionId
                LIMIT 1
                """);
            var p = cmd.CreateParameter();
            p.ParameterName = "sessionId";
            p.Value = sessionId;
            cmd.Parameters.Add(p);
            using var r = cmd.ExecuteReader();
            if (r.Read())
            {
                var dt = (DateTime)r.GetValue(0);
                end = WallclockTime.FromDateTimeOffset(new DateTimeOffset(dt, TimeSpan.Zero));
            }
        }

        return (start.Value, end);
    }
}
