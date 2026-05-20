using DuckDB.NET.Data;
using Microsoft.Extensions.Logging;
using Tracer.Core.Records;
using Tracer.Storage.DuckDB.MultiInterval;
using EventId = Tracer.Core.Identity.EventId;

namespace Tracer.WebApi.Queries;

/// <summary>
/// Service for building <see cref="TraceTree"/> objects from cross-interval DuckDB queries.
/// </summary>
public sealed class TraceQueryService(LiveMultiIntervalReader reader, ILogger<TraceQueryService> logger)
{
    private readonly LiveMultiIntervalReader _reader = reader;
    private readonly ILogger<TraceQueryService> _logger = logger;

    /// <summary>Retrieves all events with the given trace_id and assembles them into a tree.</summary>
    public async Task<TraceTree?> GetTraceTreeAsync(
        ulong traceId,
        int maxEvents,
        CancellationToken ct)
    {
        await using var conn = await _reader.AcquireAsync(ct);

        var sql = conn.WithEventsCte("""
            SELECT event_id, trace_id, parent_event_id, sequence_number,
                   publish_wallclock, receive_wallclock, publisher_node, subscriber_node,
                   topic, entity_id, owning_player_id, scenario_phase, severity, notable_label, payload
            FROM events
            WHERE trace_id = $traceId
            ORDER BY publish_wallclock
            LIMIT $limit
            """);

        var events = await Task.Run(() =>
        {
            using var cmd = conn.Connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.Parameters.Add(new DuckDBParameter("traceId", (long)traceId));
            cmd.Parameters.Add(new DuckDBParameter("limit", maxEvents + 1));  // +1 to detect truncation

            var list = new List<EventRecord>();
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(EventRecordMapper.FromReader(r));
            return list;
        }, ct);

        if (events.Count == 0) return null;

        var truncated = events.Count > maxEvents;
        if (truncated) events.RemoveAt(events.Count - 1);

        return BuildTree(events, truncated, traceId);
    }

    /// <summary>
    /// Looks up the event by ID to find its trace_id, then returns the full trace tree.
    /// If the event has trace_id = 0, returns a singleton tree.
    /// </summary>
    public async Task<TraceTree?> GetTraceTreeForEventAsync(
        EventId eventId,
        int maxEvents,
        CancellationToken ct)
    {
        EventRecord? ev;
        {
            await using var conn = await _reader.AcquireAsync(ct);
            ev = await TraceWalker.LookupEventAsync(conn, eventId.Value, ct);
        }
        if (ev is null) return null;
        if (ev.TraceId.Value == 0) return BuildSingletonTree(ev);
        return await GetTraceTreeAsync(ev.TraceId.Value, maxEvents, ct);
    }

    /// <summary>Walks ancestors from <paramref name="eventId"/> up to <paramref name="maxDepth"/>.</summary>
    public async Task<TraceTree?> GetAncestorTreeAsync(
        EventId eventId,
        int maxDepth,
        CancellationToken ct)
    {
        await using var conn = await _reader.AcquireAsync(ct);

        var chain = await TraceWalker.WalkAncestorsAsync(conn, eventId, maxDepth, ct);
        if (chain.Count == 0) return null;

        var traceId = chain[0].TraceId.Value;
        return BuildTree(chain.ToList(), truncated: false, traceId);
    }

    /// <summary>Walks descendants from <paramref name="eventId"/> using BFS.</summary>
    public async Task<TraceTree?> GetDescendantTreeAsync(
        EventId eventId,
        int maxDepth,
        int maxNodes,
        CancellationToken ct)
    {
        await using var conn = await _reader.AcquireAsync(ct);

        var root = await TraceWalker.LookupEventAsync(conn, eventId.Value, ct);
        if (root is null) return null;

        var descendants = await TraceWalker.WalkDescendantsAsync(conn, eventId, maxDepth, maxNodes, ct);

        var all = new List<EventRecord>(descendants.Count + 1) { root };
        all.AddRange(descendants);

        var truncated = descendants.Count >= maxNodes;
        return BuildTree(all, truncated, root.TraceId.Value);
    }

    private static TraceTree BuildTree(
        IReadOnlyList<EventRecord> events,
        bool truncated,
        ulong traceId)
    {
        var nodes = events.Select(e => new TraceNode(e)).ToList();
        var nodeById = nodes.ToDictionary(n => n.Event.EventId.Value);

        var edges = new List<TraceEdge>();
        foreach (var node in nodes)
        {
            var parentId = node.Event.ParentEventId?.Value ?? 0;
            if (parentId == 0) continue;
            if (!nodeById.TryGetValue(parentId, out var parent)) continue;

            var latencyMs = (node.Event.PublishWallclock.ToDateTimeOffset() -
                             parent.Event.PublishWallclock.ToDateTimeOffset()).TotalMilliseconds;
            edges.Add(new TraceEdge(parent.Event.EventId, node.Event.EventId, latencyMs));
        }

        var childSet = new HashSet<ulong>(edges.Select(e => e.ChildEventId.Value));
        var parentSet = new HashSet<ulong>(edges.Select(e => e.ParentEventId.Value));

        var roots = nodes.Where(n => !childSet.Contains(n.Event.EventId.Value)).ToList();
        var leaves = nodes.Where(n => !parentSet.Contains(n.Event.EventId.Value)).ToList();

        var participatingNodes = events
            .Select(e => e.PublisherNode.Value)
            .Distinct()
            .OrderBy(n => n)
            .ToList();

        double totalSpanMs = 0;
        DateTimeOffset? firstEventUtc = null;
        DateTimeOffset? lastEventUtc = null;

        if (events.Count > 0)
        {
            var times = events.Select(e => e.PublishWallclock.ToDateTimeOffset()).ToList();
            firstEventUtc = times.Min();
            lastEventUtc = times.Max();
            totalSpanMs = (lastEventUtc.Value - firstEventUtc.Value).TotalMilliseconds;
        }

        return new TraceTree
        {
            TraceId = traceId,
            Nodes = nodes,
            Edges = edges,
            Roots = roots,
            Leaves = leaves,
            Summary = new TraceSummary
            {
                TraceId = traceId,
                TotalEvents = events.Count,
                Truncated = truncated,
                TotalSpanMs = totalSpanMs,
                ParticipatingNodes = participatingNodes,
                RootCount = roots.Count,
                LeafCount = leaves.Count,
                FirstEventUtc = firstEventUtc,
                LastEventUtc = lastEventUtc,
                TotalEventsAvailable = truncated ? events.Count + 1 : null,
            },
        };
    }

    private static TraceTree BuildSingletonTree(EventRecord ev)
    {
        var node = new TraceNode(ev);
        return new TraceTree
        {
            TraceId = ev.TraceId.Value,
            Nodes = [node],
            Edges = [],
            Roots = [node],
            Leaves = [node],
            Summary = new TraceSummary
            {
                TraceId = ev.TraceId.Value,
                TotalEvents = 1,
                Truncated = false,
                TotalSpanMs = 0,
                ParticipatingNodes = [ev.PublisherNode.Value],
                RootCount = 1,
                LeafCount = 1,
                FirstEventUtc = ev.PublishWallclock.ToDateTimeOffset(),
                LastEventUtc = ev.PublishWallclock.ToDateTimeOffset(),
            },
        };
    }
}
