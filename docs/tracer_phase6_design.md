# Tracer Phase 6 — Detailed Design
## Causal Tree View, Trace Walking, Cross-View Navigation

*Companion to `tracer_architecture_v1.md` and `tracer_phase1_design.md` through `tracer_phase5_design.md`*
*Phase 6 of the build sequence (architecture §18)*
*C# / .NET 8 backend · Vue 3 / TypeScript frontend · May 2026*

*Phase 6 is the payoff for the trace-context machinery. Every event has a `trace_id` (which causal chain) and a `parent_event_id` (what directly caused this). Phase 6 turns those numbers into a visual: a tree centered on any event, showing what caused it (ancestors), what it caused (descendants), the latencies on each edge, and the node each event occurred on. Combined with cross-view navigation, an engineer can pivot from a timeline anomaly into "what caused this?" and back, weaving across views to build understanding.*

*This is the phase where the small, opinionated decisions from Phase 1 — uint64 IDs, parent pointer instead of child list, propagation discipline as integration-project responsibility — pay off in user-visible ways. Phase 6 also adds the first cross-view pivots that wire the SPA together as more than a collection of independent views.*

---

## 1. Phase 6 Scope and Goals

### 1.1 What Phase 6 Delivers

- **`CausalTreeView.vue`** — the new view. Renders a tree (or DAG when convergence is detected) centered on a chosen event or trace.
- **`/api/traces/{traceId}/tree`** — backend endpoint returning the tree structure for a trace, with optional centering and depth limits.
- **`/api/events/{eventId}/ancestors`** and **`/api/events/{eventId}/descendants`** — focused walking endpoints for incremental exploration.
- **DAG layout algorithm** — layered topological layout with reasonable defaults for fanout, fanin, and depth. Renders both single-rooted trees and multi-rooted DAGs (convergent traces from multiple sources).
- **Latency annotations** — every edge labeled with the wall-clock duration between parent emission and child emission.
- **Node coloring by publisher** — re-uses Phase 5's per-node palette so the same node shows the same color across all views.
- **Cross-view navigation** — clicking a node in the tree pivots to timeline, scenario, or (when available) entity history. The reverse pivot from Phase 5's `EventInspector` becomes functional.
- **The "trace summary" panel** — total span, span across nodes, fanout statistics, root and leaf counts.
- **Shareable URL** — `/v/causal/{eventId}` or `/v/trace/{traceId}` resolves to a specific view.
- **Index on `parent_event_id`** — added to the events schema so descendant walks are efficient.

### 1.2 What Phase 6 Does NOT Deliver

- **No entity history view** (Phase 7)
- **No fast state inspection** (Phase 7)
- **No replication latency analysis** (Phase 9)
- **No annotations on events or traces** (Phase 8 — but Phase 6's URL pattern is forward-compatible)
- **No causal-tree comparison across traces** — Phase 6 shows one trace at a time. Comparing two traces side by side is a Phase 10+ exploration.
- **No automatic anomaly highlighting** — Phase 6 doesn't flag "this trace has unusually high latency" or similar. The view shows the data; analysis is the engineer's job.
- **No editing or annotation of trees** — the view is read-only.
- **No support for very large traces (>5,000 events)** at full fidelity — when a trace exceeds the configured threshold, the view shows a summary plus a focused sub-tree around the selected event, not the full tree. Configurable but conservative defaults.

### 1.3 Success Criteria

1. **Open a causal tree from the timeline**: clicking "Show causal tree" in `EventInspector` opens `CausalTreeView` centered on that event. Loads in < 300 ms for traces under 100 events.
2. **Walk the chain**: ancestors and descendants of any clicked node load and expand in-place in < 200 ms.
3. **Convergence detected and rendered**: when two parents lead to one child (or two roots merge into one trace via state mediation), the view shows a DAG, not duplicate nodes.
4. **Latency on every edge**: each edge displays `(child.publish_wallclock - parent.publish_wallclock)` in human-readable form (e.g. "3 ms", "1.2 s").
5. **Cross-view pivots work**: from a causal tree node, "Show in timeline" returns to the timeline focused on this event's time and node; "Show in scenario" returns to the scenario view.
6. **Shareable URLs**: `/v/causal/{eventId}` loads the same view on any machine with access to the data.
7. **Trace summary panel**: shows total span, participating nodes, root and leaf counts.
8. **Large-trace truncation**: traces over the configured threshold render a focused sub-tree without crashing the browser; the summary panel reports the truncation.
9. **All Phase 1-5 tests still pass.**
10. **Performance**: traces under 500 events render in < 200 ms after the data arrives; 5,000-event truncated views render in < 500 ms.

### 1.4 Estimated Duration

Two to three calendar weeks for one developer. Distribution:
- Week 1: backend trace walking endpoints; index on `parent_event_id`; SQL for ancestors/descendants
- Week 2: tree layout algorithm; canvas renderer for nodes and edges
- Week 3: cross-view navigation pivots; URL state; trace summary panel; performance pass

---

## 2. Project Layout Additions

Building on Phase 5:

```
tracer/
  src/
    Tracer.Storage.DuckDB/                        (additions to schema for parent_event_id index)
      Schema/
        SchemaV1.cs                               EXTENDED — adds parent_event_id index
    Tracer.WebApi/
      Endpoints/
        TraceEndpoints.cs                         NEW
      Queries/
        TraceQueryService.cs                      NEW
        TraceWalker.cs                            NEW — ancestor/descendant walk logic
      Contracts/Dto/
        TraceTreeDto.cs                           NEW
        TraceNodeDto.cs
        TraceEdgeDto.cs
        TraceSummaryDto.cs
  tracer-viewer/
    src/
      views/
        CausalTreeView.vue                        NEW
      components/
        CausalTreeCanvas.vue                      NEW — canvas renderer
        TraceSummaryPanel.vue                     NEW
        TraceNodeTooltip.vue
        TraceSearchInput.vue                      NEW — open by event ID or trace ID
      composables/
        useCausalTreeQuery.ts                     NEW
        useCausalTreeLayout.ts                    NEW
        useCausalTreeUrl.ts                       NEW
      rendering/
        causalTreeLayout.ts                       NEW — layered DAG layout algorithm
        causalTreeRenderer.ts                     NEW — pure canvas drawing
        causalTreeHitTest.ts                      NEW
      stores/
        causalTreeStore.ts                        NEW
      types/
        causalTree.ts                             NEW
  tests/
    Tracer.Tests.Unit/
      WebApi/
        TraceQueryServiceTests.cs
        TraceWalkerTests.cs
        TraceEndpointsTests.cs
    Tracer.Tests.Integration/
      CausalTreeRoundTripTests.cs
  tracer-viewer/tests/
    unit/
      causalTreeLayout.spec.ts
      causalTreeRenderer.spec.ts
      causalTreeHitTest.spec.ts
      useCausalTreeQuery.spec.ts
    e2e/
      causal-tree-view.spec.ts
```

### 2.1 Dependencies

No new NuGet or npm packages.

---

## 3. Schema Extension: parent_event_id Index

Phase 1 designed the events schema with `parent_event_id` as a column but didn't index it. Why: at design time, no view walked children. Phase 6 walks children, so the index is justified now.

### 3.1 The Lookup

The descendants walk asks "for event X, what are its children?" — that's `SELECT * FROM events WHERE parent_event_id = X`. Without an index this is a full scan of all events. With an index, it's a point lookup.

```sql
CREATE INDEX IF NOT EXISTS idx_events_parent_event_id
ON events (parent_event_id)
WHERE parent_event_id != 0;
```

The `WHERE parent_event_id != 0` partial-index clause excludes root events (which have `parent_event_id = 0`). In a typical trace, root events are 5-20% of the total — excluding them halves the index size with no impact on query semantics (children of root 0 are never sought).

### 3.2 Where the Index Is Created

In `Tracer.Storage.DuckDB.Schema.SchemaV1.CreateIndexes`:

```csharp
public static class SchemaV1
{
    public const string CreateIndexes = """
        -- Phase 1
        CREATE INDEX IF NOT EXISTS idx_events_publish_wallclock
            ON events (publish_wallclock);
        CREATE INDEX IF NOT EXISTS idx_events_trace_id
            ON events (trace_id) WHERE trace_id != 0;
        CREATE INDEX IF NOT EXISTS idx_events_entity_id
            ON events (entity_id) WHERE entity_id IS NOT NULL;
        CREATE INDEX IF NOT EXISTS idx_events_topic_time
            ON events (topic, publish_wallclock);
        CREATE INDEX IF NOT EXISTS idx_events_player_id
            ON events (owning_player_id) WHERE owning_player_id IS NOT NULL;
        
        -- Phase 6
        CREATE INDEX IF NOT EXISTS idx_events_parent_event_id
            ON events (parent_event_id) WHERE parent_event_id != 0;
        """;
}
```

The same index is created in three places:
- Agent's per-interval `events.duckdb` (Phase 2 writer)
- Aggregator's consolidated bundle `events.duckdb` (Phase 4 EventsConsolidator)
- Observer's per-interval `events.duckdb` (Phase 3 observer ingestion)

All three reach `SchemaV1.CreateIndexes`; updating the constant propagates everywhere.

### 3.3 Migration

New intervals (and new bundles) get the index automatically. Existing intervals from Phase 5 do not — the database file already exists without the index.

**Migration approach for Phase 6**:
- The `CREATE INDEX IF NOT EXISTS` is idempotent and cheap on an empty index.
- For pre-existing intervals: when the connection pool opens a read-only attachment, the index doesn't get created (read-only mode disallows DDL).
- Two options:
  - **Option A**: do nothing. Pre-Phase-6 intervals are queryable without the index; descendant walks against them are slower but functionally correct. Retention will evict them within hours-to-days.
  - **Option B**: a one-time migration that re-opens each pre-existing interval read-write, creates the index, checkpoints, closes. Runs on Observer startup.

**Phase 6 chooses Option A.** Reasons:
- Pre-existing intervals have a bounded lifetime (retention).
- The slowdown applies only to descendant walks, not ancestor walks (those use the existing `event_id` PRIMARY KEY).
- Option B introduces a write to read-only-by-design data; the risk-reward isn't there.
- Bundles built before Phase 6 are forever indexed without the parent_event_id index. Acceptable: bundle re-build is cheap.

The handoff note in Phase 5 §14 ("add an index on `parent_event_id` if profiling shows the need") is satisfied by Phase 6's addition.

---

## 4. Trace Walking Backend

### 4.1 The Walk Operations

Three distinct walks are needed:

| Walk | Direction | Input | Output |
|---|---|---|---|
| **Ancestors** | Parent → grandparent → ... | event_id | Linear chain ending at a root |
| **Descendants** | Children → grandchildren → ... | event_id | Tree (branching) |
| **Trace** | All events with a given trace_id | trace_id | Set of events forming a DAG |

Each walk has different SQL characteristics:

- **Ancestors**: bounded by depth (typically < 20); each step is one indexed lookup by `event_id` (the primary key). Fast.
- **Descendants**: unbounded fanout; each step requires the `parent_event_id` index. Can be large.
- **Trace**: a single indexed lookup by `trace_id`. Returns all events in the trace at once.

Different views call different walks:
- Causal tree centered on event E: ancestors(E) ∪ descendants(E)
- Causal tree centered on trace T: all events in trace(T)

### 4.2 The Trace Walk Implementation

The trace walk is the simplest and primary path. One query returns everything.

```csharp
namespace Tracer.WebApi.Queries;

public sealed class TraceQueryService
{
    private readonly LiveMultiIntervalReader _reader;
    private readonly ILogger<TraceQueryService> _logger;

    public TraceQueryService(LiveMultiIntervalReader reader, ILogger<TraceQueryService> logger)
    {
        _reader = reader;
        _logger = logger;
    }

    public async Task<TraceTree> GetTraceTreeAsync(
        ulong traceId,
        int maxEvents,
        CancellationToken ct)
    {
        await using var conn = await _reader.AcquireAsync(ct);
        
        // Query all events on this trace across attached intervals
        var whereClause = "WHERE trace_id = $traceId";
        var unionSql = conn.BuildEventsUnionSql(whereClause: whereClause);
        
        var sql = $"""
            WITH unioned AS ({unionSql})
            SELECT * FROM unioned
            ORDER BY publish_wallclock
            LIMIT $limit;
            """;
        
        await using var cmd = conn.Connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new DuckDBParameter("traceId", (long)traceId));
        cmd.Parameters.Add(new DuckDBParameter("limit", maxEvents + 1));  // +1 to detect truncation
        
        var events = new List<EventRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            events.Add(EventRecordMapper.FromReader(reader));
        
        var truncated = events.Count > maxEvents;
        if (truncated) events.RemoveAt(events.Count - 1);
        
        return BuildTreeFromEventSet(events, truncated, traceId);
    }
    
    private static TraceTree BuildTreeFromEventSet(
        IReadOnlyList<EventRecord> events, bool truncated, ulong traceId)
    {
        // Build node lookup
        var nodes = events.ToDictionary(e => e.EventId.Value, e => new TraceNode(e));
        
        // Build edges from parent_event_id; ignore parents that aren't in the set
        // (could happen if a trace's ancestor predates the queryable interval window)
        var edges = new List<TraceEdge>();
        foreach (var node in nodes.Values)
        {
            var parentId = node.Event.ParentEventId.Value;
            if (parentId == 0) continue;
            if (!nodes.TryGetValue(parentId, out var parent)) continue;
            
            var latencyMs = (node.Event.PublishWallclock - parent.Event.PublishWallclock).TotalMilliseconds;
            edges.Add(new TraceEdge(parent.Event.EventId, node.Event.EventId, latencyMs));
        }
        
        // Identify roots: nodes with no parent in the set
        var hasParent = new HashSet<ulong>(edges.Select(e => e.ChildEventId.Value));
        var roots = nodes.Values.Where(n => !hasParent.Contains(n.Event.EventId.Value)).ToList();
        
        // Identify leaves: nodes with no children
        var hasChild = new HashSet<ulong>(edges.Select(e => e.ParentEventId.Value));
        var leaves = nodes.Values.Where(n => !hasChild.Contains(n.Event.EventId.Value)).ToList();
        
        var participatingNodes = events
            .Select(e => e.PublisherNode)
            .Distinct()
            .OrderBy(n => n)
            .ToList();
        
        var totalSpanMs = events.Count == 0
            ? 0.0
            : (events.Max(e => e.PublishWallclock) - events.Min(e => e.PublishWallclock)).TotalMilliseconds;
        
        return new TraceTree
        {
            TraceId = traceId,
            Nodes = nodes.Values.ToList(),
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
                FirstEventUtc = events.FirstOrDefault()?.PublishWallclock.ToDateTimeOffset(),
                LastEventUtc = events.LastOrDefault()?.PublishWallclock.ToDateTimeOffset()
            }
        };
    }
}

public sealed record TraceTree
{
    public required ulong TraceId { get; init; }
    public required IReadOnlyList<TraceNode> Nodes { get; init; }
    public required IReadOnlyList<TraceEdge> Edges { get; init; }
    public required IReadOnlyList<TraceNode> Roots { get; init; }
    public required IReadOnlyList<TraceNode> Leaves { get; init; }
    public required TraceSummary Summary { get; init; }
}

public sealed record TraceNode(EventRecord Event);

public sealed record TraceEdge(EventId ParentEventId, EventId ChildEventId, double LatencyMs);

public sealed record TraceSummary
{
    public required ulong TraceId { get; init; }
    public required int TotalEvents { get; init; }
    public required bool Truncated { get; init; }
    public required double TotalSpanMs { get; init; }
    public required IReadOnlyList<string> ParticipatingNodes { get; init; }
    public required int RootCount { get; init; }
    public required int LeafCount { get; init; }
    public DateTimeOffset? FirstEventUtc { get; init; }
    public DateTimeOffset? LastEventUtc { get; init; }
}
```

### 4.3 The Ancestor Walk

When the user opens the view focused on a specific event (not a full trace), we may not want the entire trace — just this event's lineage. The ancestor walk climbs from a node to its roots.

```csharp
namespace Tracer.WebApi.Queries;

public static class TraceWalker
{
    /// <summary>
    /// Walks ancestors from a starting event upward until reaching a root or depth limit.
    /// Each step is a primary-key lookup by event_id.
    /// </summary>
    public static async Task<IReadOnlyList<EventRecord>> WalkAncestorsAsync(
        PooledMultiIntervalConnection conn,
        EventId startEventId,
        int maxDepth,
        CancellationToken ct)
    {
        var chain = new List<EventRecord>();
        var currentId = startEventId.Value;
        var visited = new HashSet<ulong>();  // cycle protection
        
        for (int depth = 0; depth < maxDepth; depth++)
        {
            if (currentId == 0) break;
            if (!visited.Add(currentId)) break;  // cycle detected
            
            var ev = await LookupEventAsync(conn, currentId, ct);
            if (ev is null) break;
            chain.Add(ev);
            currentId = ev.ParentEventId.Value;
        }
        
        return chain;
    }

    /// <summary>
    /// Walks descendants from a starting event downward to all leaves or depth limit.
    /// Uses BFS to fan out; each level is one indexed query.
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
    
    private static async Task<EventRecord?> LookupEventAsync(
        PooledMultiIntervalConnection conn, ulong eventId, CancellationToken ct)
    {
        var whereClause = $"WHERE event_id = $eventId";
        var unionSql = conn.BuildEventsUnionSql(whereClause: whereClause);
        var sql = $"WITH u AS ({unionSql}) SELECT * FROM u LIMIT 1";
        
        await using var cmd = conn.Connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new DuckDBParameter("eventId", (long)eventId));
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? EventRecordMapper.FromReader(reader) : null;
    }
    
    private static async Task<IReadOnlyList<EventRecord>> FetchChildrenAsync(
        PooledMultiIntervalConnection conn, IReadOnlyList<ulong> parentIds, CancellationToken ct)
    {
        // Batch lookup using IN clause
        if (parentIds.Count == 0) return Array.Empty<EventRecord>();
        
        var inList = string.Join(",", parentIds.Select((_, i) => $"$p{i}"));
        var whereClause = $"WHERE parent_event_id IN ({inList})";
        var unionSql = conn.BuildEventsUnionSql(whereClause: whereClause);
        var sql = $"WITH u AS ({unionSql}) SELECT * FROM u";
        
        await using var cmd = conn.Connection.CreateCommand();
        cmd.CommandText = sql;
        for (int i = 0; i < parentIds.Count; i++)
            cmd.Parameters.Add(new DuckDBParameter($"p{i}", (long)parentIds[i]));
        
        var children = new List<EventRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            children.Add(EventRecordMapper.FromReader(reader));
        return children;
    }
}
```

**BFS over DFS**: descendants form a tree (or DAG), and we want to bound the result by total node count (not just depth). BFS lets us stop at any frontier without losing breadth at lower depths.

**Cycle protection**: trace context propagation rules (architecture §7.3) prohibit cycles in principle, but defensive code in production is right. The `visited` set ensures we never reprocess a node.

**Batched children lookups**: each BFS level fetches children of all parents at that level in one query rather than one per parent. For a fanout of 10 at each level over 5 levels, this is 5 queries instead of 11,111. The IN-clause approach is well within DuckDB's parameter limits at Phase 6's scale.

### 4.4 The Trace-via-Event Path

When the caller has an event ID but wants the whole trace (the natural case from the timeline "Show causal tree" pivot), the flow is:

1. Look up the event by ID → get its `trace_id`.
2. Call `GetTraceTreeAsync(traceId, ...)`.

```csharp
public async Task<TraceTree?> GetTraceTreeForEventAsync(
    EventId eventId, int maxEvents, CancellationToken ct)
{
    await using var conn = await _reader.AcquireAsync(ct);
    var ev = await TraceWalker.LookupEventInternalAsync(conn, eventId.Value, ct);
    if (ev is null) return null;
    if (ev.TraceId.Value == 0)
    {
        // Event is not part of any trace (root-only, no descendants either).
        // Return a singleton tree.
        return BuildSingletonTree(ev);
    }
    return await GetTraceTreeAsync(ev.TraceId.Value, maxEvents, ct);
}
```

### 4.5 Choosing the Max Events Threshold

The trace might be huge. Architecture §1.2 mentions "production-scale traces of thousands of events". The Phase 6 view doesn't render every node usefully past ~5,000 markers (canvas budget, mental model budget for the engineer). Two tiers:

- **Default `maxEvents = 1000`**: comfortable for the layout algorithm and the canvas.
- **Configured upper bound `maxEvents = 5000`**: hard cap. The caller can request up to this; beyond, the request fails with a 400.

When truncated, the response carries `summary.truncated = true` and `summary.totalEventsAvailable = ...` (an additional COUNT query when truncation occurs). The view surfaces this and offers to widen the cap or focus on a sub-tree around the user's event of interest.

For Phase 6, "focused sub-tree" is implemented as: ancestors-then-descendants-from-the-clicked-event, capping descendants at `maxNodes` — distinct from the trace-wide walk. The view-side logic picks which walk to issue.

---

## 5. The Trace API Endpoints

### 5.1 Endpoint Surface

```
GET  /api/traces/{traceId}                       trace summary (lightweight, no nodes/edges)
GET  /api/traces/{traceId}/tree                  full tree (nodes + edges + summary)
GET  /api/events/{eventId}/trace                 trace tree for the event's trace_id
GET  /api/events/{eventId}/ancestors             ancestor chain only
GET  /api/events/{eventId}/descendants           descendants tree only (BFS-walked)
```

The full-trace endpoint is the primary one. Ancestors/descendants are exposed for cases where the engineer wants tighter scope (e.g. very large traces).

### 5.2 TraceEndpoints.cs

```csharp
namespace Tracer.WebApi.Endpoints;

public static class TraceEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/traces/{traceId}",            HandleGetTraceSummaryAsync).WithOpenApi();
        app.MapGet("/api/traces/{traceId}/tree",       HandleGetTraceTreeAsync).WithOpenApi();
        app.MapGet("/api/events/{eventId}/trace",      HandleGetTraceByEventAsync).WithOpenApi();
        app.MapGet("/api/events/{eventId}/ancestors",  HandleAncestorsAsync).WithOpenApi();
        app.MapGet("/api/events/{eventId}/descendants", HandleDescendantsAsync).WithOpenApi();
    }

    public static async Task<Results<Ok<TraceSummaryDto>, NotFound, ProblemHttpResult>>
        HandleGetTraceSummaryAsync(
            string traceId,
            [FromServices] TraceQueryService traces,
            CancellationToken ct)
    {
        if (!ulong.TryParse(traceId, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var id))
            return TypedResults.Problem(BadHexProblem("traceId"));
        
        var summary = await traces.GetTraceSummaryAsync(id, ct);
        return summary is null ? TypedResults.NotFound() : TypedResults.Ok(TraceDtoMapper.Map(summary));
    }

    public static async Task<Results<Ok<TraceTreeDto>, NotFound, ProblemHttpResult>>
        HandleGetTraceTreeAsync(
            string traceId,
            [FromQuery] int? maxEvents,
            [FromServices] TraceQueryService traces,
            CancellationToken ct)
    {
        if (!ulong.TryParse(traceId, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var id))
            return TypedResults.Problem(BadHexProblem("traceId"));
        
        var cap = ClampMaxEvents(maxEvents ?? 1000);
        var tree = await traces.GetTraceTreeAsync(id, cap, ct);
        if (tree.Nodes.Count == 0) return TypedResults.NotFound();
        return TypedResults.Ok(TraceDtoMapper.Map(tree));
    }

    public static async Task<Results<Ok<TraceTreeDto>, NotFound, ProblemHttpResult>>
        HandleGetTraceByEventAsync(
            string eventId,
            [FromQuery] int? maxEvents,
            [FromServices] TraceQueryService traces,
            CancellationToken ct)
    {
        if (!ulong.TryParse(eventId, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var id))
            return TypedResults.Problem(BadHexProblem("eventId"));
        
        var cap = ClampMaxEvents(maxEvents ?? 1000);
        var tree = await traces.GetTraceTreeForEventAsync(new EventId(id), cap, ct);
        return tree is null ? TypedResults.NotFound() : TypedResults.Ok(TraceDtoMapper.Map(tree));
    }

    public static async Task<Results<Ok<TraceTreeDto>, NotFound, ProblemHttpResult>>
        HandleAncestorsAsync(
            string eventId,
            [FromQuery] int? maxDepth,
            [FromServices] TraceQueryService traces,
            CancellationToken ct)
    {
        if (!ulong.TryParse(eventId, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var id))
            return TypedResults.Problem(BadHexProblem("eventId"));
        
        var depth = Math.Clamp(maxDepth ?? 50, 1, 100);
        var tree = await traces.GetAncestorTreeAsync(new EventId(id), depth, ct);
        return tree is null ? TypedResults.NotFound() : TypedResults.Ok(TraceDtoMapper.Map(tree));
    }

    public static async Task<Results<Ok<TraceTreeDto>, NotFound, ProblemHttpResult>>
        HandleDescendantsAsync(
            string eventId,
            [FromQuery] int? maxDepth,
            [FromQuery] int? maxNodes,
            [FromServices] TraceQueryService traces,
            CancellationToken ct)
    {
        if (!ulong.TryParse(eventId, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var id))
            return TypedResults.Problem(BadHexProblem("eventId"));
        
        var depth = Math.Clamp(maxDepth ?? 30, 1, 100);
        var nodes = Math.Clamp(maxNodes ?? 1000, 1, 5000);
        var tree = await traces.GetDescendantTreeAsync(new EventId(id), depth, nodes, ct);
        return tree is null ? TypedResults.NotFound() : TypedResults.Ok(TraceDtoMapper.Map(tree));
    }

    private static int ClampMaxEvents(int requested) => Math.Clamp(requested, 1, 5000);
    
    private static ProblemDetails BadHexProblem(string field) => new()
    {
        Title = $"Invalid {field}",
        Detail = $"{field} must be a 16-character hex string",
        Status = StatusCodes.Status400BadRequest
    };
}
```

### 5.3 DTOs

```csharp
namespace Tracer.WebApi.Contracts.Dto;

public sealed record TraceTreeDto
{
    public required string TraceId { get; init; }                  // 16-char hex
    public required IReadOnlyList<TraceNodeDto> Nodes { get; init; }
    public required IReadOnlyList<TraceEdgeDto> Edges { get; init; }
    public required IReadOnlyList<string> RootEventIds { get; init; }
    public required IReadOnlyList<string> LeafEventIds { get; init; }
    public required TraceSummaryDto Summary { get; init; }
}

public sealed record TraceNodeDto
{
    public required string EventId { get; init; }                  // 16-char hex
    public required string TraceId { get; init; }
    public string? ParentEventId { get; init; }                    // null when root
    public required DateTimeOffset PublishWallclock { get; init; }
    public required string PublisherNode { get; init; }
    public required string Topic { get; init; }
    public string? EntityId { get; init; }
    public string? Severity { get; init; }
    public string? NotableLabel { get; init; }
    public string? PayloadJson { get; init; }                      // included for inspector pivots
}

public sealed record TraceEdgeDto
{
    public required string ParentEventId { get; init; }
    public required string ChildEventId { get; init; }
    public required double LatencyMs { get; init; }
}

public sealed record TraceSummaryDto
{
    public required string TraceId { get; init; }
    public required int TotalEvents { get; init; }                 // events in the response (post-truncation)
    public int? TotalEventsAvailable { get; init; }                // present if truncated
    public required bool Truncated { get; init; }
    public required double TotalSpanMs { get; init; }
    public required IReadOnlyList<string> ParticipatingNodes { get; init; }
    public required int RootCount { get; init; }
    public required int LeafCount { get; init; }
    public DateTimeOffset? FirstEventUtc { get; init; }
    public DateTimeOffset? LastEventUtc { get; init; }
}
```

**Including `PayloadJson` in `TraceNodeDto`**: clicking a tree node opens an inspector. We have two options:
- Include payload in the tree response (more bandwidth, no follow-up query)
- Fetch payload on click via `/api/events/{id}` (less bandwidth, one extra round-trip per inspect)

For Phase 6 we **include the payload** because it keeps interactions instant. A 1000-node tree with ~500-byte payloads is ~500 KB — negligible. If we hit payload-heavy traces later, the response can omit payload by default and accept an `includePayload=false` parameter.

### 5.4 Wiring into ObserverHostBuilder

```csharp
// In ObserverHostBuilder, additions:
builder.Services.AddSingleton<TraceQueryService>();

// And in ConfigureMiddleware:
TraceEndpoints.Map(app);
```

Same wiring for the offline viewer (`OfflineViewerHostBuilder` from Phase 4 §8.3) — the API surface is identical between live and bundle modes.

---

## 6. The Layout Algorithm

The frontend renders the tree as a directed acyclic graph (DAG). Most real traces are trees (single parent per node), but convergent causation (state-mediated) and edge cases produce DAGs. The layout algorithm handles both.

### 6.1 Layout Choice: Layered Topological

The Sugiyama-style "layered" approach: assign each node a `layer` (depth from roots), assign each node an `x` position within its layer, draw edges as straight or curved lines. Simple, fast, and produces readable results for traces up to a few thousand nodes.

```
Layer 0:    [R1]         [R2]
              |            |
Layer 1:   [A]  [B]      [C]
              \  /         |
Layer 2:      [X]       [Y]
                \       /
Layer 3:         [Z]   (this node has two parents; DAG, not tree)
```

The algorithm:

1. **Layer assignment**: topological sort gives each node a layer based on its longest path from any root.
2. **X-position within layer**: minimize edge crossings using a heuristic (median-of-parents).
3. **Final coordinate mapping**: layers → y positions (top to bottom), x-positions → screen x.

### 6.2 Implementation: causalTreeLayout.ts

```typescript
// src/rendering/causalTreeLayout.ts

import type { TraceTreeDto, TraceNodeDto, TraceEdgeDto } from '@/types/causalTree';

export interface LaidOutNode {
  eventId: string;
  layer: number;       // 0 = root layer
  layerIndex: number;  // position within layer
  x: number;           // screen x (pixels)
  y: number;           // screen y (pixels)
  node: TraceNodeDto;
}

export interface LaidOutEdge {
  parentId: string;
  childId: string;
  fromX: number; fromY: number;
  toX: number;   toY: number;
  latencyMs: number;
}

export interface LayoutResult {
  nodes: Map<string, LaidOutNode>;
  edges: LaidOutEdge[];
  widthPx: number;
  heightPx: number;
}

export interface LayoutConfig {
  nodeRadiusPx: number;
  hSpacingPx: number;       // horizontal gap between adjacent nodes in same layer
  vSpacingPx: number;       // vertical gap between layers
  paddingPx: number;
}

export function layout(tree: TraceTreeDto, config: LayoutConfig): LayoutResult {
  // 1. Build adjacency maps
  const childrenOf = new Map<string, string[]>();
  const parentsOf  = new Map<string, string[]>();
  for (const e of tree.edges) {
    if (!childrenOf.has(e.parentEventId)) childrenOf.set(e.parentEventId, []);
    childrenOf.get(e.parentEventId)!.push(e.childEventId);
    if (!parentsOf.has(e.childEventId)) parentsOf.set(e.childEventId, []);
    parentsOf.get(e.childEventId)!.push(e.parentEventId);
  }
  
  const nodeById = new Map<string, TraceNodeDto>();
  for (const n of tree.nodes) nodeById.set(n.eventId, n);
  
  // 2. Assign layers via longest-path-from-roots (so converging branches align)
  const layerOf = new Map<string, number>();
  const visiting = new Set<string>();
  
  function computeLayer(id: string): number {
    if (layerOf.has(id)) return layerOf.get(id)!;
    if (visiting.has(id)) return 0;  // cycle defense; shouldn't happen
    visiting.add(id);
    
    const parents = parentsOf.get(id) ?? [];
    const layer = parents.length === 0
      ? 0
      : Math.max(...parents.map(p => computeLayer(p))) + 1;
    
    layerOf.set(id, layer);
    visiting.delete(id);
    return layer;
  }
  
  for (const id of nodeById.keys()) computeLayer(id);
  
  // 3. Bucket nodes by layer
  const layers: string[][] = [];
  for (const [id, layer] of layerOf) {
    while (layers.length <= layer) layers.push([]);
    layers[layer].push(id);
  }
  
  // 4. Within-layer ordering: by median of parents' x positions, iteratively
  // First pass: sort layer 0 by publish time (chronological-by-default)
  layers[0].sort((a, b) => {
    const ta = new Date(nodeById.get(a)!.publishWallclock).getTime();
    const tb = new Date(nodeById.get(b)!.publishWallclock).getTime();
    return ta - tb;
  });
  
  // Subsequent layers: sort by median parent index in previous layer
  for (let l = 1; l < layers.length; l++) {
    const prev = layers[l - 1];
    const prevIndex = new Map(prev.map((id, i) => [id, i]));
    layers[l].sort((a, b) => {
      const pa = (parentsOf.get(a) ?? []).map(p => prevIndex.get(p) ?? 0);
      const pb = (parentsOf.get(b) ?? []).map(p => prevIndex.get(p) ?? 0);
      const ma = median(pa);
      const mb = median(pb);
      if (ma !== mb) return ma - mb;
      // Tiebreak by publish time
      const ta = new Date(nodeById.get(a)!.publishWallclock).getTime();
      const tb = new Date(nodeById.get(b)!.publishWallclock).getTime();
      return ta - tb;
    });
  }
  
  // 5. Assign coordinates
  const cellW = config.nodeRadiusPx * 2 + config.hSpacingPx;
  const cellH = config.nodeRadiusPx * 2 + config.vSpacingPx;
  const maxLayerWidth = Math.max(...layers.map(l => l.length));
  const totalWidth  = maxLayerWidth * cellW + config.paddingPx * 2;
  const totalHeight = layers.length * cellH + config.paddingPx * 2;
  
  const laidOutNodes = new Map<string, LaidOutNode>();
  for (let l = 0; l < layers.length; l++) {
    const layer = layers[l];
    // Center each layer within the canvas width
    const layerWidth = layer.length * cellW;
    const offsetX = (totalWidth - layerWidth) / 2;
    
    for (let i = 0; i < layer.length; i++) {
      const id = layer[i];
      const x = offsetX + i * cellW + cellW / 2;
      const y = config.paddingPx + l * cellH + cellH / 2;
      laidOutNodes.set(id, {
        eventId: id,
        layer: l,
        layerIndex: i,
        x, y,
        node: nodeById.get(id)!,
      });
    }
  }
  
  // 6. Compute edge endpoints
  const laidOutEdges: LaidOutEdge[] = tree.edges.map(e => {
    const parent = laidOutNodes.get(e.parentEventId)!;
    const child  = laidOutNodes.get(e.childEventId)!;
    return {
      parentId: e.parentEventId,
      childId:  e.childEventId,
      fromX: parent.x, fromY: parent.y + config.nodeRadiusPx,
      toX:   child.x,  toY:   child.y - config.nodeRadiusPx,
      latencyMs: e.latencyMs,
    };
  });
  
  return {
    nodes: laidOutNodes,
    edges: laidOutEdges,
    widthPx:  totalWidth,
    heightPx: totalHeight,
  };
}

function median(xs: number[]): number {
  if (xs.length === 0) return 0;
  const sorted = [...xs].sort((a, b) => a - b);
  const mid = Math.floor(sorted.length / 2);
  return sorted.length % 2 === 0
    ? (sorted[mid - 1] + sorted[mid]) / 2
    : sorted[mid];
}
```

### 6.3 Quality vs. Speed Tradeoffs

The above is "single-pass barycentric" — fast and good enough for traces under ~500 nodes. For larger traces, a few enhancements help:

- **Multiple sweeps**: after computing layer-order with median-of-parents, do a second sweep using median-of-children. Repeat until stable (typically 2-4 iterations). Reduces edge crossings further.
- **Edge bundling**: when many edges go from layer L to layer L+2 (skipping L+1), insert "dummy" nodes in L+1 so all edges are between adjacent layers. Then routing is straight.

Phase 6 ships with single-pass. Multiple sweeps are an easy follow-up if real traces look messy.

### 6.4 Coordinate-Based Hit Testing

Each laid-out node has `(x, y)` and a radius. Hit testing is a point-in-circle check across all visible nodes — at < 1000 nodes, linear scan is fast enough.

```typescript
// src/rendering/causalTreeHitTest.ts

export function findNodeAt(layout: LayoutResult, x: number, y: number, radius: number): LaidOutNode | null {
  let best: LaidOutNode | null = null;
  let bestDist = radius * radius;
  for (const node of layout.nodes.values()) {
    const dx = node.x - x;
    const dy = node.y - y;
    const d2 = dx * dx + dy * dy;
    if (d2 < bestDist) { bestDist = d2; best = node; }
  }
  return best;
}
```

For larger trees we'd build a spatial index (same as Phase 5 §5.7); Phase 6 doesn't need to. The performance budget is hit-test-on-hover at 60 Hz: 1000 nodes × 60 Hz = 60,000 distance comparisons per second. Trivial.

---

## 7. Frontend Rendering

### 7.1 Component Layout

```
+---------------------------------------------------------------+
| AppHeader                                                     |
+---------------------------------------------------------------+
| TraceSearchInput: paste eventId or traceId                    |
+----------+------------------------------------+----------------+
|          |                                    |                |
|  Trace   |       CausalTreeCanvas             |   Event        |
|  Summary |   (DAG rendering, pan, zoom)       |  Inspector     |
|  Panel   |                                    |   (selected    |
|          |                                    |    node)       |
| (~280px) |   (main content)                   |   (~400px)     |
|          |                                    |                |
+----------+------------------------------------+----------------+
```

Same shell pattern as Phase 5's TimelineView for consistency. The summary panel replaces FilterPanel — different content for different view.

### 7.2 CausalTreeView.vue

```vue
<!-- src/views/CausalTreeView.vue -->
<script setup lang="ts">
import { computed, ref } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { useCausalTreeStore } from '@/stores/causalTreeStore';
import { useCausalTreeQuery } from '@/composables/useCausalTreeQuery';
import { useCausalTreeUrl } from '@/composables/useCausalTreeUrl';
import CausalTreeCanvas from '@/components/CausalTreeCanvas.vue';
import TraceSummaryPanel from '@/components/TraceSummaryPanel.vue';
import EventInspector from '@/components/EventInspector.vue';
import TraceSearchInput from '@/components/TraceSearchInput.vue';
import LoadingSpinner from '@/components/LoadingSpinner.vue';
import ErrorMessage from '@/components/ErrorMessage.vue';

const store = useCausalTreeStore();
useCausalTreeUrl();
useCausalTreeQuery();

const selectedNode = computed(() => {
  if (!store.selectedEventId || !store.tree) return null;
  return store.tree.nodes.find(n => n.eventId === store.selectedEventId) ?? null;
});
</script>

<template>
  <div class="causal-tree-view">
    <header class="causal-tree-view__header">
      <h1>Causal tree</h1>
      <TraceSearchInput />
    </header>

    <LoadingSpinner v-if="store.loading && !store.tree" />
    <ErrorMessage v-else-if="store.error" :message="store.error" @retry="store.retry" />
    
    <div v-else-if="store.tree" class="causal-tree-view__grid">
      <TraceSummaryPanel
        class="causal-tree-view__summary"
        :summary="store.tree.summary"
      />
      <CausalTreeCanvas
        class="causal-tree-view__canvas"
        :tree="store.tree"
        :selected-event-id="store.selectedEventId"
        @select="store.selectEvent"
      />
      <EventInspector
        v-if="selectedNode"
        class="causal-tree-view__inspector"
        :event="selectedNode"
        :show-causal-tree-pivot="false"
        @pivot-timeline="navigateToTimeline"
        @pivot-scenario="navigateToScenario"
      />
    </div>
    
    <div v-else class="causal-tree-view__empty">
      Open a causal tree from the timeline, or paste an event ID above.
    </div>
  </div>
</template>

<style lang="scss">
.causal-tree-view {
  max-width: 1600px;
  margin: 0 auto;
  padding: 1.5rem;
  
  &__header {
    display: flex;
    align-items: center;
    gap: 1.5rem;
    margin-bottom: 1rem;
    h1 { margin: 0; }
  }
  
  &__grid {
    display: grid;
    grid-template-columns: 280px 1fr;
    grid-template-areas: "summary canvas";
    gap: 1.5rem;
    
    &:has(.causal-tree-view__inspector) {
      grid-template-columns: 280px 1fr 400px;
      grid-template-areas: "summary canvas inspector";
    }
  }
  
  &__summary  { grid-area: summary; }
  &__canvas   { grid-area: canvas; }
  &__inspector { grid-area: inspector; }
}
</style>
```

### 7.3 The Canvas Component

```vue
<!-- src/components/CausalTreeCanvas.vue -->
<script setup lang="ts">
import { ref, watch, onMounted } from 'vue';
import { layout, type LayoutResult } from '@/rendering/causalTreeLayout';
import { renderTree } from '@/rendering/causalTreeRenderer';
import { findNodeAt } from '@/rendering/causalTreeHitTest';
import type { TraceTreeDto } from '@/types/causalTree';
import { useResizeObserver } from '@/composables/useResizeObserver';
import { buildNodeColorMap } from '@/rendering/colorScheme';

const props = defineProps<{
  tree: TraceTreeDto;
  selectedEventId: string | null;
}>();

const emit = defineEmits<{ select: [eventId: string | null] }>();

const containerRef = ref<HTMLDivElement | null>(null);
const canvasRef = ref<HTMLCanvasElement | null>(null);
const layoutResult = ref<LayoutResult | null>(null);

// Pan/zoom state
const viewport = ref({ tx: 0, ty: 0, scale: 1 });

// Compute layout when the tree changes
watch(() => props.tree, (tree) => {
  layoutResult.value = layout(tree, {
    nodeRadiusPx: 14,
    hSpacingPx: 40,
    vSpacingPx: 80,
    paddingPx: 40,
  });
}, { immediate: true });

// Render when layout, viewport, or selection change
function draw() {
  const canvas = canvasRef.value;
  const layoutR = layoutResult.value;
  if (!canvas || !layoutR) return;
  const ctx = canvas.getContext('2d');
  if (!ctx) return;
  
  // DPI-correct sizing
  const dpr = window.devicePixelRatio || 1;
  const cssWidth  = canvas.clientWidth;
  const cssHeight = canvas.clientHeight;
  if (canvas.width  !== cssWidth  * dpr) canvas.width  = cssWidth  * dpr;
  if (canvas.height !== cssHeight * dpr) canvas.height = cssHeight * dpr;
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
  
  // Clear, apply viewport transform
  ctx.clearRect(0, 0, cssWidth, cssHeight);
  ctx.save();
  ctx.translate(viewport.value.tx, viewport.value.ty);
  ctx.scale(viewport.value.scale, viewport.value.scale);
  
  const nodeColors = buildNodeColorMap(props.tree.summary.participatingNodes);
  renderTree(ctx, layoutR, {
    selectedEventId: props.selectedEventId,
    nodeColors,
  });
  
  ctx.restore();
}

watch([layoutResult, viewport, () => props.selectedEventId], draw, { deep: true });

useResizeObserver(containerRef, draw);
onMounted(draw);

// Pan
let dragging = false;
let lastX = 0, lastY = 0;
function onPointerDown(e: PointerEvent) {
  dragging = true;
  lastX = e.clientX;
  lastY = e.clientY;
  (e.target as Element).setPointerCapture(e.pointerId);
}
function onPointerMove(e: PointerEvent) {
  if (!dragging) {
    // Hover handled by CSS cursor change (set on hit-test)
    return;
  }
  viewport.value.tx += e.clientX - lastX;
  viewport.value.ty += e.clientY - lastY;
  lastX = e.clientX;
  lastY = e.clientY;
}
function onPointerUp(e: PointerEvent) {
  dragging = false;
  (e.target as Element).releasePointerCapture(e.pointerId);
}

// Zoom
function onWheel(e: WheelEvent) {
  e.preventDefault();
  const canvas = canvasRef.value!;
  const rect = canvas.getBoundingClientRect();
  const cursorX = e.clientX - rect.left;
  const cursorY = e.clientY - rect.top;
  
  // Compute world-space cursor position before zoom
  const worldX = (cursorX - viewport.value.tx) / viewport.value.scale;
  const worldY = (cursorY - viewport.value.ty) / viewport.value.scale;
  
  const factor = e.deltaY > 0 ? 0.85 : 1.18;
  const newScale = Math.max(0.2, Math.min(4, viewport.value.scale * factor));
  
  // Keep the cursor's world position stationary on screen
  viewport.value.scale = newScale;
  viewport.value.tx = cursorX - worldX * newScale;
  viewport.value.ty = cursorY - worldY * newScale;
}

// Click → select node
function onClick(e: PointerEvent) {
  if (!layoutResult.value || !canvasRef.value) return;
  const rect = canvasRef.value.getBoundingClientRect();
  const cursorX = e.clientX - rect.left;
  const cursorY = e.clientY - rect.top;
  // Inverse of pan+zoom
  const worldX = (cursorX - viewport.value.tx) / viewport.value.scale;
  const worldY = (cursorY - viewport.value.ty) / viewport.value.scale;
  
  const hit = findNodeAt(layoutResult.value, worldX, worldY, 14);
  emit('select', hit?.eventId ?? null);
}
</script>

<template>
  <div ref="containerRef" class="causal-tree-canvas">
    <canvas
      ref="canvasRef"
      @pointerdown="onPointerDown"
      @pointermove="onPointerMove"
      @pointerup="onPointerUp"
      @wheel="onWheel"
      @click="onClick"
    />
  </div>
</template>

<style lang="scss">
.causal-tree-canvas {
  position: relative;
  background: var(--c-bg-surface);
  border-radius: 12px;
  overflow: hidden;
  min-height: 500px;
  
  canvas {
    width: 100%;
    height: 100%;
    display: block;
    cursor: grab;
    &:active { cursor: grabbing; }
  }
}
</style>
```

### 7.4 The Renderer

```typescript
// src/rendering/causalTreeRenderer.ts

import type { LayoutResult, LaidOutNode, LaidOutEdge } from './causalTreeLayout';

export interface CausalTreeRenderInput {
  selectedEventId: string | null;
  nodeColors: Map<string, string>;
}

export function renderTree(
  ctx: CanvasRenderingContext2D,
  layout: LayoutResult,
  input: CausalTreeRenderInput
) {
  drawEdges(ctx, layout, input);
  drawNodes(ctx, layout, input);
}

function drawEdges(ctx: CanvasRenderingContext2D, layout: LayoutResult, input: CausalTreeRenderInput) {
  ctx.lineWidth = 1.5;
  ctx.strokeStyle = 'rgba(255,255,255,0.25)';
  for (const e of layout.edges) {
    // Bezier curve: control points pull the line vertically
    const cp1y = e.fromY + (e.toY - e.fromY) * 0.4;
    const cp2y = e.fromY + (e.toY - e.fromY) * 0.6;
    ctx.beginPath();
    ctx.moveTo(e.fromX, e.fromY);
    ctx.bezierCurveTo(e.fromX, cp1y, e.toX, cp2y, e.toX, e.toY);
    ctx.stroke();
    
    drawEdgeLatencyLabel(ctx, e);
  }
}

function drawEdgeLatencyLabel(ctx: CanvasRenderingContext2D, e: LaidOutEdge) {
  const midX = (e.fromX + e.toX) / 2;
  const midY = (e.fromY + e.toY) / 2;
  
  const label = formatLatency(e.latencyMs);
  ctx.font = '11px var(--font-mono, monospace)';
  ctx.fillStyle = 'rgba(255,255,255,0.55)';
  ctx.textAlign = 'center';
  ctx.textBaseline = 'middle';
  
  // Background pill for legibility
  const metrics = ctx.measureText(label);
  const pad = 4;
  ctx.fillStyle = 'rgba(0,0,0,0.5)';
  ctx.fillRect(
    midX - metrics.width / 2 - pad,
    midY - 7,
    metrics.width + pad * 2,
    14
  );
  
  ctx.fillStyle = 'rgba(255,255,255,0.85)';
  ctx.fillText(label, midX, midY);
}

function drawNodes(ctx: CanvasRenderingContext2D, layout: LayoutResult, input: CausalTreeRenderInput) {
  for (const node of layout.nodes.values()) {
    drawNode(ctx, node, input);
  }
}

function drawNode(ctx: CanvasRenderingContext2D, node: LaidOutNode, input: CausalTreeRenderInput) {
  const isSelected = node.eventId === input.selectedEventId;
  const color = input.nodeColors.get(node.node.publisherNode) ?? '#888';
  
  // Severity overlay
  const severityColor = node.node.severity === 'error'   ? '#e85c5c'
                     :  node.node.severity === 'warning' ? '#e8b048'
                     :  null;
  
  // Outer ring on selected
  if (isSelected) {
    ctx.lineWidth = 3;
    ctx.strokeStyle = '#fff';
    ctx.beginPath();
    ctx.arc(node.x, node.y, 18, 0, Math.PI * 2);
    ctx.stroke();
  }
  
  // Filled circle, color = publisher
  ctx.fillStyle = color;
  ctx.beginPath();
  ctx.arc(node.x, node.y, 14, 0, Math.PI * 2);
  ctx.fill();
  
  // Inner severity dot (if warning or error)
  if (severityColor) {
    ctx.fillStyle = severityColor;
    ctx.beginPath();
    ctx.arc(node.x, node.y, 5, 0, Math.PI * 2);
    ctx.fill();
  }
  
  // Notable marker: small square at corner
  if (node.node.notableLabel) {
    ctx.fillStyle = '#fff';
    ctx.fillRect(node.x + 8, node.y - 16, 8, 8);
  }
  
  // Topic label below node (small, monospace, truncated)
  const label = truncate(node.node.topic, 16);
  ctx.font = '10px var(--font-mono, monospace)';
  ctx.fillStyle = 'rgba(255,255,255,0.7)';
  ctx.textAlign = 'center';
  ctx.textBaseline = 'top';
  ctx.fillText(label, node.x, node.y + 18);
}

function formatLatency(ms: number): string {
  if (ms < 1)    return `${(ms * 1000).toFixed(0)}μs`;
  if (ms < 10)   return `${ms.toFixed(1)}ms`;
  if (ms < 1000) return `${ms.toFixed(0)}ms`;
  return `${(ms / 1000).toFixed(2)}s`;
}

function truncate(s: string, max: number): string {
  return s.length > max ? s.slice(0, max - 1) + '…' : s;
}
```

**Encoding choices in the visual**:
- **Node circle color = publisher node**. Same palette as Phase 5's timeline — visual consistency across views.
- **Inner dot = severity**. Warning/error stand out without overwhelming the publisher signal.
- **Corner square = notable**. Small, peripheral — visible without dominating.
- **Topic label below node**. Engineers reading the tree need to see "what kind of event is this?" without clicking. Topic is the answer 95% of the time.
- **Latency on every edge**. The fundamental causality question is "how long?" — we surface it on the edge directly. Pill background for legibility against any edge color.

### 7.5 The Trace Summary Panel

```vue
<!-- src/components/TraceSummaryPanel.vue -->
<script setup lang="ts">
import { computed } from 'vue';
import type { TraceSummaryDto } from '@/types/causalTree';
import { formatDuration } from '@/utils/time';
import { buildNodeColorMap } from '@/rendering/colorScheme';

const props = defineProps<{ summary: TraceSummaryDto }>();

const nodeColors = computed(() => buildNodeColorMap(props.summary.participatingNodes));
const spanDisplay = computed(() => formatDuration(props.summary.totalSpanMs));
</script>

<template>
  <section class="trace-summary">
    <div class="trace-summary__field">
      <div class="trace-summary__label">Trace ID</div>
      <div class="trace-summary__value trace-summary__value--mono">
        {{ summary.traceId }}
      </div>
    </div>
    
    <div class="trace-summary__row">
      <div class="trace-summary__field">
        <div class="trace-summary__label">Events</div>
        <div class="trace-summary__value">
          {{ summary.totalEvents.toLocaleString() }}
          <span v-if="summary.truncated" class="trace-summary__warn">
            (of {{ summary.totalEventsAvailable?.toLocaleString() ?? 'many' }})
          </span>
        </div>
      </div>
      <div class="trace-summary__field">
        <div class="trace-summary__label">Span</div>
        <div class="trace-summary__value">{{ spanDisplay }}</div>
      </div>
    </div>
    
    <div class="trace-summary__row">
      <div class="trace-summary__field">
        <div class="trace-summary__label">Roots</div>
        <div class="trace-summary__value">{{ summary.rootCount }}</div>
      </div>
      <div class="trace-summary__field">
        <div class="trace-summary__label">Leaves</div>
        <div class="trace-summary__value">{{ summary.leafCount }}</div>
      </div>
    </div>
    
    <div class="trace-summary__field">
      <div class="trace-summary__label">
        Nodes ({{ summary.participatingNodes.length }})
      </div>
      <div class="trace-summary__nodes">
        <span
          v-for="node in summary.participatingNodes"
          :key="node"
          class="trace-summary__node"
          :style="{ borderColor: nodeColors.get(node) }"
        >
          {{ node }}
        </span>
      </div>
    </div>
    
    <div v-if="summary.truncated" class="trace-summary__truncation-notice">
      This trace was truncated. Showing {{ summary.totalEvents }} of
      {{ summary.totalEventsAvailable }} events. Open a focused sub-tree
      to see specific lineage.
    </div>
  </section>
</template>

<style lang="scss">
.trace-summary {
  background: var(--c-bg-surface);
  border-radius: 12px;
  padding: 1.5rem;
  display: flex;
  flex-direction: column;
  gap: 1rem;
  
  &__row {
    display: grid;
    grid-template-columns: 1fr 1fr;
    gap: 1rem;
  }
  
  &__label {
    font-size: 0.75rem;
    color: var(--c-text-muted);
    text-transform: uppercase;
    letter-spacing: 0.05em;
    margin-bottom: 0.25rem;
  }
  
  &__value {
    font-size: 1.25rem;
    font-weight: 500;
    
    &--mono {
      font-family: var(--font-mono);
      font-size: 0.875rem;
      word-break: break-all;
    }
  }
  
  &__warn {
    color: var(--c-warning);
    font-size: 0.875rem;
    margin-left: 0.5rem;
  }
  
  &__nodes {
    display: flex;
    flex-wrap: wrap;
    gap: 0.5rem;
  }
  
  &__node {
    padding: 0.25rem 0.5rem;
    background: var(--c-bg-subtle);
    border-left: 3px solid;
    border-radius: 4px;
    font-size: 0.875rem;
    font-family: var(--font-mono);
  }
  
  &__truncation-notice {
    padding: 0.75rem;
    background: rgba(232, 176, 72, 0.1);
    border: 1px solid var(--c-warning);
    border-radius: 6px;
    font-size: 0.875rem;
    color: var(--c-warning);
  }
}
</style>
```

---

## 8. Stores, Composables, and URL Binding

### 8.1 causalTreeStore

```typescript
// src/stores/causalTreeStore.ts
import { defineStore } from 'pinia';
import type { TraceTreeDto } from '@/types/causalTree';

interface CausalTreeRequest {
  kind: 'trace' | 'event' | 'ancestors' | 'descendants';
  id: string;                    // traceId or eventId
  maxEvents?: number;
  maxDepth?: number;
  maxNodes?: number;
}

export const useCausalTreeStore = defineStore('causalTree', {
  state: () => ({
    request: null as CausalTreeRequest | null,
    tree: null as TraceTreeDto | null,
    loading: false,
    error: null as string | null,
    selectedEventId: null as string | null,
  }),
  actions: {
    openTrace(traceId: string) {
      this.request = { kind: 'trace', id: traceId };
      this.tree = null;
      this.selectedEventId = null;
    },
    openByEvent(eventId: string) {
      this.request = { kind: 'event', id: eventId };
      this.tree = null;
      this.selectedEventId = eventId;
    },
    openAncestors(eventId: string, maxDepth = 50) {
      this.request = { kind: 'ancestors', id: eventId, maxDepth };
      this.tree = null;
      this.selectedEventId = eventId;
    },
    openDescendants(eventId: string, maxDepth = 30, maxNodes = 1000) {
      this.request = { kind: 'descendants', id: eventId, maxDepth, maxNodes };
      this.tree = null;
      this.selectedEventId = eventId;
    },
    selectEvent(eventId: string | null) {
      this.selectedEventId = eventId;
    },
    setResult(tree: TraceTreeDto) {
      this.tree = tree;
      // If selected event isn't in the tree (e.g. user pasted an ID for trace mode),
      // default to centering on a notable, then the first event.
      if (this.selectedEventId && !tree.nodes.some(n => n.eventId === this.selectedEventId)) {
        this.selectedEventId = pickInitialSelection(tree);
      } else if (!this.selectedEventId) {
        this.selectedEventId = pickInitialSelection(tree);
      }
    },
    setError(message: string) {
      this.error = message;
    },
    clear() {
      this.request = null;
      this.tree = null;
      this.selectedEventId = null;
      this.error = null;
    },
    retry() {
      // Re-trigger the current request
      const r = this.request;
      this.request = null;
      this.request = r;
    },
  },
});

function pickInitialSelection(tree: TraceTreeDto): string | null {
  // Prefer a notable
  const notable = tree.nodes.find(n => n.notableLabel);
  if (notable) return notable.eventId;
  // Otherwise the first event chronologically
  return tree.nodes[0]?.eventId ?? null;
}
```

### 8.2 useCausalTreeQuery

```typescript
// src/composables/useCausalTreeQuery.ts
import { watch } from 'vue';
import { useCausalTreeStore } from '@/stores/causalTreeStore';
import { useApi } from '@/api/useApi';

export function useCausalTreeQuery() {
  const store = useCausalTreeStore();
  const api = useApi();
  let abortCtrl: AbortController | null = null;

  watch(() => store.request, async (req) => {
    if (!req) return;
    abortCtrl?.abort();
    abortCtrl = new AbortController();
    
    store.loading = true;
    store.error = null;
    
    try {
      let tree;
      switch (req.kind) {
        case 'trace':
          tree = await api.getTraceTree(req.id, req.maxEvents ?? 1000, { signal: abortCtrl.signal });
          break;
        case 'event':
          tree = await api.getTraceByEvent(req.id, req.maxEvents ?? 1000, { signal: abortCtrl.signal });
          break;
        case 'ancestors':
          tree = await api.getEventAncestors(req.id, req.maxDepth ?? 50, { signal: abortCtrl.signal });
          break;
        case 'descendants':
          tree = await api.getEventDescendants(
            req.id, req.maxDepth ?? 30, req.maxNodes ?? 1000,
            { signal: abortCtrl.signal });
          break;
      }
      store.setResult(tree);
    } catch (err: any) {
      if (err.name === 'AbortError') return;
      store.setError(err.message ?? 'Failed to load causal tree');
    } finally {
      store.loading = false;
    }
  }, { immediate: true });
}
```

### 8.3 URL Patterns

Two primary URLs:

```
/v/trace/{traceId}                  -- open by trace ID
/v/causal/{eventId}                 -- open by event ID (resolves to its trace)
```

With optional parameters:

```
/v/trace/{traceId}?select={eventId}&maxEvents=2000
/v/causal/{eventId}?maxEvents=500
/v/causal/{eventId}?mode=ancestors&maxDepth=10
/v/causal/{eventId}?mode=descendants&maxDepth=20&maxNodes=500
```

### 8.4 useCausalTreeUrl

```typescript
// src/composables/useCausalTreeUrl.ts
import { watch } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { useCausalTreeStore } from '@/stores/causalTreeStore';
import { debounce } from '@/utils/debounce';

export function useCausalTreeUrl() {
  const route = useRoute();
  const router = useRouter();
  const store = useCausalTreeStore();
  
  // URL → store (on route change or initial load)
  watch(
    () => ({ name: route.name, params: route.params, query: route.query }),
    ({ name, params, query }) => {
      if (name === 'causal-by-event') {
        const eventId = params.eventId as string;
        const mode = query.mode as string | undefined;
        if (mode === 'ancestors') {
          store.openAncestors(eventId, parseInt(query.maxDepth as string) || undefined);
        } else if (mode === 'descendants') {
          store.openDescendants(
            eventId,
            parseInt(query.maxDepth as string) || undefined,
            parseInt(query.maxNodes as string) || undefined);
        } else {
          store.openByEvent(eventId);
        }
      } else if (name === 'causal-by-trace') {
        const traceId = params.traceId as string;
        store.openTrace(traceId);
        if (query.select) store.selectedEventId = query.select as string;
      }
    },
    { immediate: true }
  );
  
  // Store → URL (when user clicks within the tree, update ?select=...)
  const writeUrl = debounce(() => {
    if (!store.selectedEventId) return;
    router.replace({ query: { ...route.query, select: store.selectedEventId } });
  }, 250);
  
  watch(() => store.selectedEventId, writeUrl);
}
```

### 8.5 Router Configuration

```typescript
// In src/router/index.ts, additions:
{
  path: '/v/trace/:traceId',
  name: 'causal-by-trace',
  component: () => import('@/views/CausalTreeView.vue'),
},
{
  path: '/v/causal/:eventId',
  name: 'causal-by-event',
  component: () => import('@/views/CausalTreeView.vue'),
},
```

---

## 9. Cross-View Navigation

Phase 5's `EventInspector` had pivot buttons; "Show causal tree" was disabled. Phase 6 enables them. And the causal tree's inspector adds the reverse pivots back.

### 9.1 Pivot Catalog

| Source view | Pivot | Target | Action |
|---|---|---|---|
| Timeline (Phase 5) | "Show causal tree" | CausalTreeView | router.push to `/v/causal/{eventId}` |
| Timeline | "Filter to this trace" | Timeline | adds trace_id filter (Phase 5) |
| CausalTreeView | "Show in timeline" | Timeline | router.push to `/v/timeline/{sessionId}?select={eventId}&from={t-2s}&to={t+2s}` |
| CausalTreeView | "Show in scenario" | ScenarioView | router.push to `/scenario/{sessionId}` (Phase 3) |
| CausalTreeView | "Show entity history" | EntityHistoryView | **disabled in Phase 6**, enabled by Phase 7 |
| Scenario notable click (Phase 3) | "Show causal tree" | CausalTreeView | router.push to `/v/causal/{eventId}` |

### 9.2 Resolving Session ID From Event

A practical concern: the causal tree URL contains just the event ID. To pivot back to the timeline, we need the session ID. We have two options:

**Option A**: include session ID in the trace tree response. Add `sessionContext: { sessionId, label }` to `TraceTreeDto`.

**Option B**: fetch it lazily — call `GET /api/events/{eventId}` from the inspector to get the event's full record, including a derivable session context.

**Phase 6 chooses Option A.** Reasons:
- The session-ID lookup is essentially free at trace-tree-build time (we have the event records already).
- Avoiding the extra round-trip keeps pivots instant.
- The DTO grows by ~50 bytes.

`TraceTreeDto` gains:

```csharp
public sealed record TraceTreeDto
{
    // ... existing ...
    public required string SessionId { get; init; }   // resolved from the events' time range
}
```

The query service computes session context by running a small lookup: `find the session whose [startUtc, endUtc) contains tree.summary.firstEventUtc`.

If a trace spans multiple sessions (unusual), pick the session containing the trace's first event. Document this edge case in the API spec.

### 9.3 Pivot Implementation

```vue
<!-- EventInspector.vue, additions -->
<script setup lang="ts">
import { useRouter } from 'vue-router';
import type { TraceNodeDto } from '@/types/causalTree';

const props = defineProps<{
  event: TraceNodeDto;
  sessionId?: string;
  showCausalTreePivot?: boolean;       // false in CausalTreeView itself
  showTimelinePivot?: boolean;
}>();

const router = useRouter();

function pivotToTimeline() {
  if (!props.sessionId) return;
  const t = new Date(props.event.publishWallclock).getTime();
  router.push({
    name: 'timeline',
    params: { sessionId: props.sessionId },
    query: {
      from: new Date(t - 2000).toISOString(),
      to:   new Date(t + 2000).toISOString(),
      select: props.event.eventId,
    },
  });
}

function pivotToCausalTree() {
  router.push({ name: 'causal-by-event', params: { eventId: props.event.eventId } });
}

function pivotToScenario() {
  if (!props.sessionId) return;
  router.push({ name: 'scenario', params: { sessionId: props.sessionId } });
}
</script>

<template>
  <section class="event-inspector">
    <!-- existing payload display -->
    
    <div class="event-inspector__pivots">
      <button
        v-if="showCausalTreePivot && event.traceId !== '0000000000000000'"
        class="event-inspector__pivot"
        @click="pivotToCausalTree"
      >
        Show causal tree
      </button>
      <button
        v-if="showTimelinePivot && sessionId"
        class="event-inspector__pivot"
        @click="pivotToTimeline"
      >
        Show in timeline
      </button>
      <button
        v-if="sessionId"
        class="event-inspector__pivot"
        @click="pivotToScenario"
      >
        Show in scenario
      </button>
    </div>
  </section>
</template>
```

The inspector is the same component across Timeline and CausalTree views; the `showCausalTreePivot` prop disables that pivot in the CausalTreeView (where it'd loop on itself).

### 9.4 Sessions Without a Causal Tree

Events with `trace_id = 0` are not part of any trace (per architecture §7.2). The pivot "Show causal tree" must be hidden for such events — there's nothing to show.

The inspector hides the button when `event.traceId === '0000000000000000'` (the hex representation of trace ID 0). The backend's `/api/events/{id}/trace` returns 404 for such events.

---

## 10. TraceSearchInput

The view header has an input field where the engineer can paste an event ID or trace ID to open. Useful for sharing IDs in chat without needing to construct URLs by hand.

```vue
<!-- src/components/TraceSearchInput.vue -->
<script setup lang="ts">
import { ref } from 'vue';
import { useRouter } from 'vue-router';

const router = useRouter();
const input = ref('');
const error = ref<string | null>(null);

function submit() {
  error.value = null;
  const value = input.value.trim();
  if (!value) return;
  
  if (!/^[0-9a-fA-F]{16}$/.test(value)) {
    error.value = 'Expected a 16-character hex ID';
    return;
  }
  
  // Heuristic: try as event ID first; if the user really has a trace ID,
  // the resolution endpoint will treat it as one. Phase 6 simplification:
  // event-ID path always; user picks "is this a trace ID?" via a toggle if needed.
  router.push({ name: 'causal-by-event', params: { eventId: value.toLowerCase() } });
  input.value = '';
}
</script>

<template>
  <form class="trace-search" @submit.prevent="submit">
    <input
      v-model="input"
      type="text"
      placeholder="Paste event ID or trace ID (16-char hex)"
      class="trace-search__input"
      :class="{ 'trace-search__input--error': error }"
    />
    <button type="submit" class="trace-search__btn" :disabled="!input">Open</button>
    <div v-if="error" class="trace-search__error">{{ error }}</div>
  </form>
</template>

<style lang="scss">
.trace-search {
  display: flex;
  gap: 0.5rem;
  flex: 1;
  position: relative;
  
  &__input {
    flex: 1;
    padding: 0.5rem 0.75rem;
    background: var(--c-bg-subtle);
    border: 1px solid var(--c-bg-subtle);
    border-radius: 6px;
    color: var(--c-text);
    font-family: var(--font-mono);
    font-size: 0.875rem;
    
    &--error { border-color: var(--c-danger); }
  }
  
  &__btn {
    padding: 0.5rem 1rem;
    background: var(--c-accent);
    color: white;
    border: none;
    border-radius: 6px;
    cursor: pointer;
    &:disabled { opacity: 0.5; cursor: not-allowed; }
  }
  
  &__error {
    position: absolute;
    top: 100%;
    left: 0;
    margin-top: 0.25rem;
    color: var(--c-danger);
    font-size: 0.75rem;
  }
}
```

### 10.1 Distinguishing Event ID From Trace ID

Both are 16-char hex. Same shape, different meaning. We have two options:

**Option A**: try as event ID first; if 404, retry as trace ID.

**Option B**: provide a toggle in the search UI: "Event" / "Trace".

**Phase 6 chooses Option B** for explicitness — a toggle in the UI removes ambiguity. The toggle defaults to "Event" because that's the more common case (engineers paste event IDs from logs more often than trace IDs).

```vue
<!-- Updated TraceSearchInput.vue (excerpt) -->
<template>
  <form class="trace-search" @submit.prevent="submit">
    <select v-model="kind" class="trace-search__kind">
      <option value="event">Event</option>
      <option value="trace">Trace</option>
    </select>
    <input ... />
    <button ...>Open</button>
  </form>
</template>

<script setup lang="ts">
const kind = ref<'event' | 'trace'>('event');

function submit() {
  // ...
  if (kind.value === 'event') {
    router.push({ name: 'causal-by-event', params: { eventId: value.toLowerCase() } });
  } else {
    router.push({ name: 'causal-by-trace', params: { traceId: value.toLowerCase() } });
  }
}
</script>
```

---

## 11. Test Plan for Phase 6

### 11.1 Backend Unit Tests

**WebApi/TraceQueryServiceTests.cs**
- `GetTraceTreeAsync` with empty trace ID: returns empty tree
- Single root event with no descendants: returns tree with one node, zero edges, root count 1, leaf count 1
- Linear chain of 5 events: returns 5 nodes, 4 edges, root count 1, leaf count 1
- Branching tree: 1 root → 3 children → 2 grandchildren each: 1 + 3 + 6 = 10 nodes, root count 1, leaf count 6
- DAG with convergence (2 parents, 1 child): identified correctly; both parents-of-child edges present
- Truncation at `maxEvents`: `truncated=true`, `totalEventsAvailable` reflects true count
- Latency on edges computed correctly from `child.publish_wallclock - parent.publish_wallclock`
- Cross-interval trace (events span 2 intervals): all events returned, edges intact
- `GetTraceTreeForEventAsync` with non-existent event: returns null
- `GetTraceTreeForEventAsync` with event having `trace_id = 0`: returns singleton tree

**WebApi/TraceWalkerTests.cs**
- `WalkAncestorsAsync` with maxDepth 5 on a 10-deep chain: returns 5 nodes
- `WalkAncestorsAsync` on a root (parent=0): returns just the root
- `WalkAncestorsAsync` with a cycle (defensive): terminates without crashing
- `WalkDescendantsAsync` with BFS fanout: visits all reachable in BFS order
- `WalkDescendantsAsync` respects `maxNodes`: returns exactly `maxNodes` and stops
- `WalkDescendantsAsync` respects `maxDepth`
- Children of a non-existent parent: returns empty
- Batched children fetch with 100 parents: one query, not 100

**WebApi/TraceEndpointsTests.cs**
- `GET /api/traces/{validTraceId}/tree`: 200 with tree
- `GET /api/traces/notHex/tree`: 400 ProblemDetails
- `GET /api/traces/{wrongLength}/tree`: 400
- `GET /api/traces/{unknown}/tree`: 404
- `GET /api/events/{eventId}/trace`: 200 with trace tree containing that event
- `GET /api/events/{eventId}/ancestors`: returns ancestor-only tree
- `GET /api/events/{eventId}/descendants`: returns descendant-only tree
- `maxEvents` query parameter: clamped to [1, 5000]; out-of-range returns 400 or clamps
- `maxDepth` query parameter: clamped appropriately

### 11.2 Backend Integration Tests

**CausalTreeRoundTripTests.cs**
- Push events with constructed trace_id and parent_event_id chains
- Query `/api/traces/{traceId}/tree`
- Assert: returned tree matches the constructed shape
- Build a bundle from the same data
- Open in offline viewer
- Query the same endpoint against the bundle
- Assert: results identical

**CrossIntervalTraceTests.cs**
- Push first 5 events of a trace into interval A
- Rotate (simulated clock)
- Push remaining 5 events into interval B
- Query `/api/traces/{traceId}/tree`
- Assert: all 10 events returned; edges across the rotation boundary intact

### 11.3 Frontend Unit Tests (Vitest)

**causalTreeLayout.spec.ts**
- Empty tree: returns layout with no nodes
- Single root: layer 0, x at canvas center
- Linear chain of 5: 5 layers, each with one node, all at same x
- Branching: 1 root, 3 children → root at layer 0 center; children at layer 1 evenly spaced
- DAG with convergence: the converging child appears once
- Cycle defense: doesn't infinite-loop

**causalTreeRenderer.spec.ts** (against canvas mocks)
- 5 nodes laid out: 5 `arc` calls drawn
- Selected node: outer ring drawn
- Notable node: corner square drawn
- Error severity: inner red dot drawn
- Latency labels drawn at edge midpoints

**causalTreeHitTest.spec.ts**
- Click exactly on node center: returns that node
- Click inside node radius but off-center: returns that node
- Click between nodes: returns null
- 1000 nodes spread across canvas: hit-test in < 10 ms

**useCausalTreeQuery.spec.ts**
- Open trace: fires `getTraceTree`
- Open event: fires `getTraceByEvent`
- Open ancestors: fires `getEventAncestors`
- Open descendants: fires `getEventDescendants`
- Switching from one request to another: previous request cancelled
- AbortError ignored

**useCausalTreeUrl.spec.ts**
- URL `/v/trace/abc` opens trace mode
- URL `/v/causal/def?mode=ancestors&maxDepth=10` opens ancestors with depth 10
- Selecting a node updates `?select=...`
- Cycling through URL changes maps to correct store actions

### 11.4 E2E Tests (Playwright)

```typescript
test('open causal tree from timeline', async ({ page }) => {
  await page.goto('http://localhost:5300/v/timeline/test-session');
  await page.waitForSelector('.timeline-canvas');
  await page.locator('.timeline-canvas').click({ position: { x: 500, y: 200 } });
  await page.waitForSelector('.event-inspector');
  await page.locator('.event-inspector__pivot-trace').click();
  // Should now be on causal tree view
  await expect(page).toHaveURL(/\/v\/causal\//);
  await page.waitForSelector('.causal-tree-canvas canvas');
  await expect(page.locator('.trace-summary')).toBeVisible();
});

test('cross-view pivot back to timeline', async ({ page }) => {
  await page.goto('http://localhost:5300/v/causal/known-event-id');
  await page.waitForSelector('.causal-tree-canvas canvas');
  await page.locator('.event-inspector__pivot-timeline').click();
  await expect(page).toHaveURL(/\/v\/timeline\//);
});

test('paste trace ID via search', async ({ page }) => {
  await page.goto('http://localhost:5300/v/causal/known-event-id');
  await page.waitForSelector('.causal-tree-canvas canvas');
  // Switch search kind to 'trace'
  await page.locator('.trace-search__kind').selectOption('trace');
  await page.locator('.trace-search__input').fill('a3f2b4c8d9e0f1a2');
  await page.locator('.trace-search__btn').click();
  await expect(page).toHaveURL(/\/v\/trace\/a3f2b4c8d9e0f1a2/);
});
```

### 11.5 Performance Tests

- Backend: tree query for a 1000-event trace returns in < 200 ms
- Backend: descendants walk depth 30, 1000 nodes returns in < 500 ms
- Frontend: layout of 500-node tree completes in < 50 ms
- Frontend: render of 500-node tree completes in < 50 ms
- E2E: timeline → tree pivot completes in < 1 second (cold cache)

---

## 12. Phase 6 Risks and Mitigations

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Layered DAG layout produces unreadable results for real traces | High | Medium | Test with realistic traces from FakeNode scenarios on day 1. If single-pass barycentric is insufficient, implement multi-sweep + dummy nodes. |
| Descendant walk against pre-Phase-6 intervals (no index) is too slow | Medium | Medium | Document the slowdown; retention naturally evicts old intervals within hours. If user impact is real, run the optional one-time migration. |
| Trace truncation surprises engineers ("where's the rest of the chain?") | Medium | Medium | The summary panel makes truncation explicit. Inspector offers "Open ancestors-only" or "Open descendants-only" pivots for focused walks. |
| Latency labels overlap on dense edges | Medium | Low | Render only labels with latency > some threshold; offer a "show all latencies" toggle. |
| Cycle in parent_event_id propagates somehow (should never happen) | Low | Medium | TraceWalker has `visited` set defense; tested with synthetic cycle data; doesn't crash. |
| Inspector pivots have wrong session ID (trace spans multiple sessions) | Low | Low | The chosen session is documented (the session of the first event). For multi-session traces, this is rare and acceptable. |
| Pan/zoom interferes with click event ordering | Medium | Low | Use distinct click vs drag detection: a "click" is a pointer up with < 5px movement since pointer down. |
| Backend tree query returns lots of payload data, slow to serialize | Medium | Medium | If profile shows JSON serialization > 50 ms for typical responses, add `includePayload=false` parameter; inspector loads payload on demand. |
| `EventInspector` is currently used by Timeline (Phase 5); Phase 6 adds new props | Low | Low | Use optional props with sensible defaults; Phase 5 callers don't change. |

---

## 13. Definition of Done for Phase 6

### Build & Run

- [ ] `Tracer.WebApi` builds with new TraceEndpoints registered
- [ ] Frontend builds with the new view, components, composables
- [ ] OpenAPI document includes `/api/traces/*` and `/api/events/{id}/{ancestors|descendants|trace}` endpoints
- [ ] TypeScript client regenerates cleanly

### Schema

- [ ] `parent_event_id` index created on all new intervals (Agent + Observer + bundle)
- [ ] Pre-Phase-6 intervals queryable without index (degraded performance accepted)
- [ ] Index creation is idempotent (CREATE INDEX IF NOT EXISTS)

### Trace Walking

- [ ] `GetTraceTreeAsync` returns correct tree for known input
- [ ] Roots and leaves identified correctly
- [ ] Convergent DAGs (two parents → one child) handled — no duplicate nodes
- [ ] Truncation at `maxEvents` works; `Truncated` flag and `TotalEventsAvailable` populated
- [ ] Latency on every edge correct to ms precision
- [ ] Cross-interval traces return complete results
- [ ] Cycle defense: walker terminates on synthetic cycle data
- [ ] BFS descendants walk visits nodes in depth order

### API Endpoints

- [ ] `GET /api/traces/{traceId}/tree`: returns 200 with tree
- [ ] `GET /api/events/{id}/trace`: returns 200 with the event's full trace
- [ ] `GET /api/events/{id}/ancestors`: returns ancestor chain only
- [ ] `GET /api/events/{id}/descendants`: returns descendant subtree only
- [ ] All endpoints validate hex inputs; return 400 ProblemDetails on invalid
- [ ] Endpoints return 404 for unknown IDs
- [ ] `maxEvents`, `maxDepth`, `maxNodes` parameters clamped to safe ranges

### Frontend: Causal Tree View

- [ ] CausalTreeView renders for a known trace
- [ ] Pan via horizontal/vertical drag
- [ ] Zoom via wheel, cursor-anchored
- [ ] Click on node: inspector opens with that event's details
- [ ] Inspector shows payload, severity, notable status
- [ ] Latency labels on every edge, formatted (μs / ms / s as appropriate)
- [ ] Per-publisher node coloring consistent with timeline
- [ ] Severity indicator (inner dot, color-coded)
- [ ] Notable corner marker

### Frontend: Trace Summary Panel

- [ ] Shows trace ID, event count, span, root/leaf counts, participating nodes
- [ ] Truncation notice appears when `summary.truncated`
- [ ] Node chips color-keyed consistently

### Frontend: Cross-View Navigation

- [ ] Timeline inspector's "Show causal tree" pivot is enabled and works
- [ ] CausalTreeView inspector's "Show in timeline" pivot works with correct centering
- [ ] CausalTreeView inspector's "Show in scenario" pivot works
- [ ] Pivot buttons are hidden when not applicable (e.g., trace_id = 0)

### Frontend: URL State

- [ ] `/v/trace/{id}` loads trace by trace ID
- [ ] `/v/causal/{id}` loads trace by event ID
- [ ] `?select=...` selects that event on load
- [ ] `?mode=ancestors&maxDepth=10` opens ancestor-only view
- [ ] `?mode=descendants` opens descendant-only view
- [ ] Selection updates URL after debounce
- [ ] URL preserves across reload

### Frontend: Search Input

- [ ] Paste event ID + click Open: navigates to `/v/causal/{id}`
- [ ] Paste trace ID + toggle to trace + Open: navigates to `/v/trace/{id}`
- [ ] Invalid input (non-hex, wrong length): error displayed inline
- [ ] Clears on successful submit

### Testing

- [ ] All Phase 1-5 tests pass
- [ ] Phase 6 backend unit tests pass (target: 30+ tests)
- [ ] Phase 6 backend integration tests pass: round-trip parity, cross-interval traces
- [ ] Phase 6 frontend unit tests pass: layout, renderer, hit-test, query, URL
- [ ] At least one Playwright E2E test passes

### Performance

- [ ] Trace tree query for 1000-event trace: < 200 ms p95
- [ ] Descendants walk depth 30, 1000 nodes: < 500 ms p95
- [ ] Frontend layout of 500 nodes: < 50 ms
- [ ] Frontend render of 500 nodes: < 50 ms
- [ ] Cross-view pivot (Timeline → Causal Tree): < 1 second cold cache

### Documentation

- [ ] `docs/causal-tree.md` explains the view for engineers (what trace_id and parent_event_id mean operationally)
- [ ] `docs/api-traces.md` documents the new endpoints
- [ ] Architecture §7.3 propagation rules referenced from in-product help when relevant
- [ ] CHANGELOG entry

---

## 14. Handoff to Phase 7

What Phase 7 inherits from Phase 6:

- **The `EventInspector` pivot pattern** — Phase 7 enables "Show entity history" which was stubbed in Phase 6
- **Per-node color consistency** — Phase 7's EntityHistoryView reuses the palette from `buildNodeColorMap`
- **The shareable URL pattern** — Phase 7 adds `/v/entity/{entityId}` following Phase 5/6 conventions
- **The schema index pattern** — if Phase 7 needs an `entity_id` lookup at high frequency, the optimization story is clear
- **The DAG / tree rendering machinery** — not directly reused (entity history is fundamentally time-series, not graph-structured), but the canvas + layout + render + hit-test pattern repeats

What Phase 7 must address that Phase 6 deferred:

- **Entity history view**: time-series of an entity's slow state, the events that touched it, and on-demand fast-state drill-down
- **Slow state rendering**: a vertical-stacked time-series style (different from timeline's swimlanes)
- **Fast state Parquet reading**: queries to the bundle's `fast_state/{topic}/{entity}/samples.parquet` files
- **Entity discovery API**: `GET /api/entities` to list entities for a session (for browsing)

What's now possible after Phase 6:

The complete diagnostic workflow:

1. Engineer opens TimelineView (Phase 5) for a session that "looks weird"
2. Filters and zooms to find an anomalous event — say, a weapons-fire event with no obvious cause
3. Clicks the event; inspector opens
4. "Show causal tree" → CausalTreeView opens centered on this event
5. The tree walks upward to roots: engineer sees the originating sensor event 2 nodes away, with 47 ms of latency to the response that should have arrived
6. The latency stands out — usually 5 ms, here 47 ms. The engineer has the network-delay hypothesis instantly.
7. "Show in scenario" → engineer goes back to confirm context: was this during a phase transition? During a known overload?
8. URL of the causal tree gets shared with the network team for further analysis

Six views interconnect: scenario, timeline, causal tree, plus the bundle/session browser. Phases 7+ add entity history and replication-latency analysis, but by Phase 6, **the trace_id machinery has paid off**. Every event is contextualized in its causal chain. Every chain is one click from its participants on the timeline. The engineer's questions ("what caused this? what did it cause?") have visual answers.
