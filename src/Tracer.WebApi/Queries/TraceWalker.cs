using DuckDB.NET.Data;
using Tracer.Core.Identity;
using Tracer.Core.Records;
using Tracer.Storage.DuckDB.MultiInterval;

namespace Tracer.WebApi.Queries;

/// <summary>
/// Pure static trace-walking algorithms operating on a pooled DuckDB connection.
/// </summary>
public static class TraceWalker
{
    /// <summary>
    /// Walks ancestor chain from <paramref name="startEventId"/> up to a root or depth limit.
    /// Returns events in leaf-first order (start event first, root event last).
    /// </summary>
    public static async Task<IReadOnlyList<EventRecord>> WalkAncestorsAsync(
        PooledMultiIntervalConnection conn,
        EventId startEventId,
        int maxDepth,
        CancellationToken ct)
    {
        var chain = new List<EventRecord>();
        var currentId = startEventId.Value;
        var visited = new HashSet<ulong>();

        for (int depth = 0; depth < maxDepth; depth++)
        {
            ct.ThrowIfCancellationRequested();
            if (currentId == 0) break;
            if (!visited.Add(currentId)) break;  // cycle guard

            var ev = await LookupEventAsync(conn, currentId, ct);
            if (ev is null) break;
            chain.Add(ev);

            currentId = ev.ParentEventId?.Value ?? 0;
        }

        return chain;
    }

    /// <summary>
    /// Walks descendants of <paramref name="startEventId"/> using BFS.
    /// Does NOT include <paramref name="startEventId"/> itself in the result.
    /// </summary>
    public static async Task<IReadOnlyList<EventRecord>> WalkDescendantsAsync(
        PooledMultiIntervalConnection conn,
        EventId startEventId,
        int maxDepth,
        int maxNodes,
        CancellationToken ct)
    {
        var allDescendants = new List<EventRecord>();
        var frontier = new List<ulong> { startEventId.Value };
        var visited = new HashSet<ulong> { startEventId.Value };

        for (int depth = 0; depth < maxDepth && frontier.Count > 0; depth++)
        {
            ct.ThrowIfCancellationRequested();
            var children = await FetchChildrenAsync(conn, frontier, ct);
            var nextFrontier = new List<ulong>();
            foreach (var child in children)
            {
                if (!visited.Add(child.EventId.Value)) continue;
                allDescendants.Add(child);
                nextFrontier.Add(child.EventId.Value);
                if (allDescendants.Count >= maxNodes) return allDescendants;
            }
            frontier = nextFrontier;
        }

        return allDescendants;
    }

    /// <summary>Looks up a single event by its event_id primary key.</summary>
    public static Task<EventRecord?> LookupEventAsync(
        PooledMultiIntervalConnection conn,
        ulong eventId,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.Run(() =>
        {
            var sql = conn.WithEventsCte("""
                SELECT event_id, trace_id, parent_event_id, sequence_number,
                       publish_wallclock, receive_wallclock, publisher_node, subscriber_node,
                       topic, entity_id, owning_player_id, scenario_phase, severity, notable_label, payload
                FROM events
                WHERE event_id = $eventId
                LIMIT 1
                """);

            using var cmd = conn.Connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.Add(new DuckDBParameter("eventId", (long)eventId));

            using var reader = cmd.ExecuteReader();
            return reader.Read() ? EventRecordMapper.FromReader(reader) : null;
        }, ct);
    }

    private static Task<IReadOnlyList<EventRecord>> FetchChildrenAsync(
        PooledMultiIntervalConnection conn,
        IReadOnlyList<ulong> parentIds,
        CancellationToken ct)
    {
        if (parentIds.Count == 0) return Task.FromResult<IReadOnlyList<EventRecord>>(Array.Empty<EventRecord>());

        ct.ThrowIfCancellationRequested();
        return Task.Run(() =>
        {
            // Build IN-clause parameters
            var inParams = string.Join(", ", Enumerable.Range(0, parentIds.Count).Select(i => $"$p{i}"));
            var sql = conn.WithEventsCte($"""
                SELECT event_id, trace_id, parent_event_id, sequence_number,
                       publish_wallclock, receive_wallclock, publisher_node, subscriber_node,
                       topic, entity_id, owning_player_id, scenario_phase, severity, notable_label, payload
                FROM events
                WHERE parent_event_id IN ({inParams})
                """);

            using var cmd = conn.Connection.CreateCommand();
            cmd.CommandText = sql;
            for (int i = 0; i < parentIds.Count; i++)
                cmd.Parameters.Add(new DuckDBParameter($"p{i}", (long)parentIds[i]));

            var children = new List<EventRecord>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                children.Add(EventRecordMapper.FromReader(reader));

            return (IReadOnlyList<EventRecord>)children;
        }, ct);
    }
}
