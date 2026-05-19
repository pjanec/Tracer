# Tracer Phase 5 — Detailed Design
## Engineer Timeline View, Canvas Rendering, Live Multi-Interval Queries

*Companion to `tracer_architecture_v1.md`, `tracer_phase1_design.md` through `tracer_phase4_design.md`*
*Phase 5 of the build sequence (architecture §18)*
*C# / .NET 8 backend · Vue 3 / TypeScript frontend · May 2026*

*Phase 5 is when engineers begin using Tracer for real diagnostic work. It delivers the core engineer-facing view: a multi-node timeline with pan, zoom, filter, and event inspection — backed by aggregated server-side queries that scale to 100M-event sessions and live SSE updates that keep the view current.*

*This is the most data-intensive view in the system. Done right, it makes "what happened across all nodes at 14:23:17?" a sub-second question. Done wrong, it makes the engineer wait for their tooling instead of their cluster. Phase 5's success criteria are framed entirely around latency: every interaction has a budget.*

*Phase 5 also delivers the missing piece from Phase 3's deliberate simplification: the Observer can now query across the active interval AND recently-completed intervals. The `MultiIntervalReader` machinery built in Phase 4 is finally wired into live mode.*

---

## 1. Phase 5 Scope and Goals

### 1.1 What Phase 5 Delivers

- **`TimelineView.vue`** — the engineer's primary view. Multi-node swimlane timeline with Canvas2D rendering, pan/zoom, density-aware aggregation, click-to-inspect.
- **`TimelineCanvas.vue`** — the canvas renderer with spatial hit-testing.
- **`EventInspector.vue`** — side panel showing full event payload, payload pretty-print, cross-view navigation pivots.
- **`FilterPanel.vue`** — filter UI: topic, entity, trace, player, severity, time range, node.
- **`/api/events`** — the generalized event-query endpoint. Returns either raw events (when count is small) or aggregated buckets (when count is large). Phase 3's `/api/events/{eventId}` lookup remains.
- **`/api/events/aggregate`** — explicit aggregation endpoint with bucket sizes.
- **`/api/live/events`** — extended SSE endpoint streaming filtered events (not just notables, as in Phase 3).
- **Cross-interval querying on the Observer** — extends `ReadOnlyConnectionPool` and query services to span the active interval plus N recent completed intervals.
- **Bundle library** in the frontend — `BundlesView.vue` listing built bundles, with the action to open one.
- **Shareable URLs** — every timeline state (range, filters, selection) is serializable to and restorable from the URL.
- **Auto-follow live mode** — timeline keeps the latest events visible as they arrive, with a pin-to-pause affordance.
- **Cross-view navigation hooks** — clicking an event offers "Show full trace" (filters timeline to this trace_id) and "Show in scenario" (returns to scenario view). Other pivots (causal tree, entity history) are stubbed for Phase 6+ targets.

### 1.2 What Phase 5 Does NOT Deliver

- No causal tree view (Phase 6)
- No entity history view (Phase 7)
- No replication latency analysis (Phase 9 — and prerequisites including DDS adapter)
- No SQL console (Phase 10)
- No annotations / bookmarks / saved views (Phase 8)
- No triggers (Phase 8)
- **No payload search across event bodies** — filters operate on top-level columns only. Full-text search of JSON payloads is deferred.
- **No multi-session timeline** — a timeline shows one session at a time (or one bundle).
- **No fast-state on timeline** — fast state is too dense to plot here (architecture §16.2). It's reserved for entity history view.
- No theming or dark/light toggle — Phase 8.

### 1.3 Success Criteria

Performance targets from architecture §17, applied to Phase 5:

1. **Open session → timeline render**: < 500 ms on a 1M-event session
2. **Pan/zoom interaction**: < 100 ms response (renderer must not block on data fetch; show stale data + spinner pattern)
3. **Apply a filter**: < 300 ms p95
4. **Click event → inspector populated**: < 100 ms
5. **Live event arrival → marker on screen**: < 100 ms (SSE)
6. **Session-overview zoom on 100M-event session**: < 1 second

Functional criteria:

7. The timeline correctly displays events from multiple intervals (cross-interval rendering works end to end).
8. Filters compose: filtering by trace AND severity simultaneously returns only events matching both.
9. The same URL on two different machines opens the same view (assuming both can access the same session/bundle).
10. Auto-follow keeps the latest events centered; clicking on the timeline pins the view and stops auto-following.
11. The frontend works against both the live Observer and an offline bundle viewer with no code changes (the same Vue build serves both).
12. All Phase 1, 2, 3, 4 tests still pass.

### 1.4 Estimated Duration

Three to four calendar weeks. The work splits:
- Week 1: Backend cross-interval query extension; `/api/events` aggregation endpoint
- Week 2: Canvas renderer, swimlane layout, hit testing
- Week 3: Filters, event inspector, URL state, SSE wiring
- Week 4: Polish, performance tuning, accept-into-real-use feedback loop

The fourth week is critical: this is the phase where the system meets real engineers for the first time. Reserve time for UX iteration on their feedback. Performance bugs surface here.

---

## 2. Project Layout Additions

Building on Phase 4:

```
tracer/
  src/
    Tracer.Core/                                  (unchanged)
    Tracer.Storage.DuckDB/                        (unchanged)
    Tracer.Storage.DuckDB.MultiInterval/          (additions; see §3)
      LiveMultiIntervalReader.cs                  NEW — observer-side live variant
      IntervalSetTracker.cs                       NEW — keeps the active set current
    Tracer.Observer/                              (additions; see §3.4)
    Tracer.WebApi/                                (additions)
      Endpoints/
        EventEndpoints.cs                         EXTENDED — adds list & aggregate endpoints
      Queries/
        EventQueryService.cs                      NEW — list + aggregate event queries
        EventAggregationService.cs                NEW — bucket aggregation
      Streaming/
        LiveEventBroadcaster.cs                   EXTENDED — broadcasts all events with filter
        SseFilter.cs                              EXTENDED — richer filter set
      Contracts/Dto/
        EventListDto.cs
        EventAggregateBucketDto.cs
        EventFilterDto.cs
  tracer-viewer/
    src/
      views/
        TimelineView.vue                          NEW — engineer view
        BundlesView.vue                           NEW — bundle library
      components/
        TimelineCanvas.vue                        NEW — canvas renderer wrapper
        TimelineAxis.vue                          NEW — x-axis ticks/labels
        TimelineToolbar.vue                       NEW — zoom presets, follow toggle
        Swimlane.vue                              NEW — per-node lane chrome (labels, color key)
        EventInspector.vue                        NEW — selected event detail
        FilterPanel.vue                           NEW — filter UI
        FilterChip.vue                            NEW — single filter pill
        DensityIndicator.vue                      NEW — "showing N of M events" badge
      composables/
        useTimelineQuery.ts                       NEW — drives the data fetch
        useTimelineUrl.ts                         NEW — URL ↔ state binding
        useTimelineLiveStream.ts                  NEW — SSE for live mode
        useTimelineSelection.ts                   NEW — selection state across views
        useCanvasRenderer.ts                      NEW — canvas mount + draw loop
        useResizeObserver.ts                      NEW — generic resize hook
      rendering/
        timelineRenderer.ts                       NEW — pure draw logic (testable)
        timelineLayout.ts                         NEW — coordinate math
        timelineHitTest.ts                        NEW — spatial index + lookup
        timelineAggregator.ts                     NEW — client-side aggregation for live updates
        colorScheme.ts                            EXTENDED — per-node + per-severity palettes
      stores/
        timelineStore.ts                          NEW — viewport, filters, selection
        bundleStore.ts                            NEW — list of known bundles
      types/
        timeline.ts                               NEW — viewport, marker, bucket types
        filter.ts                                 NEW — filter expression types
  tests/
    Tracer.Tests.Unit/
      WebApi/
        EventQueryServiceTests.cs
        EventAggregationServiceTests.cs
        EventEndpointsListTests.cs
        EventEndpointsAggregateTests.cs
      MultiInterval/
        LiveMultiIntervalReaderTests.cs
        IntervalSetTrackerTests.cs
    Tracer.Tests.Integration/
      LiveMultiIntervalQueryTests.cs              live observer queries across intervals
      TimelineRoundTripTests.cs                   bundle/live timeline parity
  tracer-viewer/tests/
    unit/
      timelineRenderer.spec.ts                    pure-draw tests against canvas mocks
      timelineLayout.spec.ts
      timelineHitTest.spec.ts
      useTimelineQuery.spec.ts
      useTimelineUrl.spec.ts
    e2e/
      timeline-view.spec.ts                       Playwright end-to-end
```

### 2.1 Dependency Graph (unchanged for backend; frontend adds testable modules)

No new NuGet packages. Frontend keeps the same dependencies as Phase 4, no new npm packages either — Canvas2D is built into browsers.

---

## 3. Live Multi-Interval Querying

Phase 3 deliberately restricted Observer queries to the active interval. Phase 4 built `MultiIntervalReader` for the aggregator. Phase 5 wires multi-interval querying into the live Observer so the timeline can span the active interval plus the most recent N completed intervals — exposing more history than is possible with single-interval queries.

### 3.1 Why This Is Harder Than the Bundle Case

The aggregator (Phase 4 §4) attaches a fixed set of read-only DuckDB files: once, at the start of the run, and never changes during execution. The Observer is different:

- **Intervals rotate continuously**: every hour (by default), a new interval becomes active. Old ones complete and become read-only files.
- **The active interval is being written to**: queries must read it concurrently with the writer.
- **Retention deletes old intervals**: a file that's currently attached for query might be deleted by the retention manager if not handled carefully.
- **Multiple queries run concurrently**: HTTP requests overlap.

Phase 3's `ReadOnlyConnectionPool` handles the writer-rotation case (it refreshes connections at rotation). Phase 5 extends that pattern to a **set** of databases that grows and shrinks as intervals rotate and retention runs.

### 3.2 IntervalSetTracker

The component that tracks the set of intervals currently eligible for query.

```csharp
namespace Tracer.Storage.DuckDB.MultiInterval;

/// <summary>
/// Tracks the set of intervals that should be available for live querying:
/// the active interval plus the most recent N completed intervals.
/// Notifies subscribers when the set changes.
/// </summary>
public sealed class IntervalSetTracker
{
    private readonly IntervalRotator _rotator;
    private readonly int _completedIntervalsToKeep;
    private readonly object _lock = new();
    private readonly List<IntervalReference> _currentSet = new();
    private readonly ILogger<IntervalSetTracker> _logger;

    public event Func<IntervalSetSnapshot, CancellationToken, Task>? SetChanged;

    public IntervalSetTracker(
        IntervalRotator rotator,
        int completedIntervalsToKeep,
        ILogger<IntervalSetTracker> logger)
    {
        _rotator = rotator;
        _completedIntervalsToKeep = completedIntervalsToKeep;
        _logger = logger;
    }

    /// <summary>
    /// Initialize the set with the active interval plus existing completed intervals
    /// (up to the cap). Called by ObserverHostedService after startup recovery.
    /// </summary>
    public async Task InitializeAsync(CancellationToken ct)
    {
        var active = _rotator.CurrentDirectory
            ?? throw new InvalidOperationException(
                "IntervalSetTracker.InitializeAsync called before active interval was opened");
        
        var completed = _rotator.ListCompletedIntervals()
            .OrderByDescending(d => d.Timestamp)
            .Take(_completedIntervalsToKeep)
            .ToList();
        
        lock (_lock)
        {
            _currentSet.Clear();
            foreach (var c in completed)
                _currentSet.Add(new IntervalReference(c, IntervalRole.Completed));
            _currentSet.Add(new IntervalReference(active, IntervalRole.Active));
        }
        
        await NotifyAsync(ct);
    }

    /// <summary>Called by ObserverHostedService after a rotation completes.</summary>
    public async Task OnIntervalRotatedAsync(CancellationToken ct)
    {
        var newActive = _rotator.CurrentDirectory
            ?? throw new InvalidOperationException("OnIntervalRotatedAsync with no active interval");
        
        lock (_lock)
        {
            // Demote the previously-active interval to Completed
            var idx = _currentSet.FindIndex(r => r.Role == IntervalRole.Active);
            if (idx >= 0)
            {
                var previousActive = _currentSet[idx];
                _currentSet[idx] = previousActive with { Role = IntervalRole.Completed };
            }
            
            // Add the new active
            _currentSet.Add(new IntervalReference(newActive, IntervalRole.Active));
            
            // Evict oldest completed intervals beyond the cap
            EvictOldestCompletedBeyondCap_Locked();
        }
        
        await NotifyAsync(ct);
    }

    /// <summary>Called by RetentionManager after deleting interval directories.</summary>
    public async Task OnIntervalEvictedAsync(IntervalDirectory evicted, CancellationToken ct)
    {
        bool removed;
        lock (_lock)
        {
            removed = _currentSet.RemoveAll(r => r.Directory.Timestamp == evicted.Timestamp) > 0;
        }
        if (removed) await NotifyAsync(ct);
    }

    public IntervalSetSnapshot CurrentSnapshot()
    {
        lock (_lock)
        {
            return new IntervalSetSnapshot(_currentSet.ToList());
        }
    }

    private void EvictOldestCompletedBeyondCap_Locked()
    {
        var completed = _currentSet
            .Where(r => r.Role == IntervalRole.Completed)
            .OrderByDescending(r => r.Directory.Timestamp)
            .ToList();
        for (int i = _completedIntervalsToKeep; i < completed.Count; i++)
            _currentSet.Remove(completed[i]);
    }

    private async Task NotifyAsync(CancellationToken ct)
    {
        var snap = CurrentSnapshot();
        var handlers = SetChanged;
        if (handlers is null) return;
        foreach (var handler in handlers.GetInvocationList().Cast<Func<IntervalSetSnapshot, CancellationToken, Task>>())
        {
            try { await handler(snap, ct); }
            catch (Exception ex) { _logger.LogError(ex, "IntervalSetTracker subscriber threw"); }
        }
    }
}

public sealed record IntervalReference(IntervalDirectory Directory, IntervalRole Role);

public enum IntervalRole { Active, Completed }

public sealed record IntervalSetSnapshot(IReadOnlyList<IntervalReference> Intervals)
{
    public IntervalReference? Active => Intervals.FirstOrDefault(r => r.Role == IntervalRole.Active);
    public IEnumerable<IntervalReference> Completed => Intervals.Where(r => r.Role == IntervalRole.Completed);
}
```

**Two distinct change events**: rotation (a new active appears) versus retention eviction (an old completed disappears). Both reshape the set but for different reasons. Keeping them separate makes the tracker's role clear: it's the single source of truth about "what intervals exist for query right now".

**Thread safety**: every external method takes the lock for snapshot building. Notification happens outside the lock to avoid holding it through subscriber callbacks (which may be slow — they tend to involve DuckDB ATTACH/DETACH).

### 3.3 LiveMultiIntervalReader

The pool extension. Where Phase 3's `ReadOnlyConnectionPool` manages connections to one DB file, `LiveMultiIntervalReader` manages connections that have N databases attached, refreshing the attach set when the IntervalSetTracker emits changes.

```csharp
namespace Tracer.Storage.DuckDB.MultiInterval;

/// <summary>
/// A connection pool whose connections have multiple DuckDB databases attached.
/// Re-attaches when the IntervalSetTracker reports changes.
/// Used by the Observer in Phase 5+ for queries that span active + completed intervals.
/// </summary>
public sealed class LiveMultiIntervalReader : IAsyncDisposable
{
    private readonly IntervalSetTracker _tracker;
    private readonly ILogger<LiveMultiIntervalReader> _logger;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly int _poolSize;

    private Channel<PooledMultiIntervalConnection>? _connections;
    private IntervalSetSnapshot? _currentSnapshot;
    private bool _disposed;

    public LiveMultiIntervalReader(
        IntervalSetTracker tracker,
        ILogger<LiveMultiIntervalReader> logger,
        int poolSize = 8)
    {
        _tracker = tracker;
        _logger = logger;
        _poolSize = poolSize;
    }

    public async Task InitializeAsync(CancellationToken ct)
    {
        // Subscribe to set changes BEFORE building the initial pool so we don't miss a change
        _tracker.SetChanged += OnSetChangedAsync;
        await RebuildAsync(_tracker.CurrentSnapshot(), ct);
    }

    private async Task OnSetChangedAsync(IntervalSetSnapshot snap, CancellationToken ct)
    {
        await RebuildAsync(snap, ct);
    }

    private async Task RebuildAsync(IntervalSetSnapshot snapshot, CancellationToken ct)
    {
        await _refreshLock.WaitAsync(ct);
        try
        {
            // Drop old pool; in-flight queries continue against their borrowed connections
            // (which are valid until disposed)
            var old = _connections;
            _connections = Channel.CreateBounded<PooledMultiIntervalConnection>(_poolSize);
            _currentSnapshot = snapshot;
            
            if (old is not null)
            {
                old.Writer.TryComplete();
                while (old.Reader.TryRead(out var conn))
                {
                    try { await conn.DisposeUnderlyingAsync(); } catch { }
                }
            }
            
            // Build new connections, each with the current set attached
            for (int i = 0; i < _poolSize; i++)
            {
                var conn = await BuildAttachedConnectionAsync(snapshot, ct);
                await _connections.Writer.WriteAsync(conn, ct);
            }
            
            _logger.LogInformation(
                "LiveMultiIntervalReader rebuilt with {Count} intervals attached",
                snapshot.Intervals.Count);
        }
        finally { _refreshLock.Release(); }
    }

    private async Task<PooledMultiIntervalConnection> BuildAttachedConnectionAsync(
        IntervalSetSnapshot snapshot, CancellationToken ct)
    {
        var conn = new DuckDBConnection("Data Source=:memory:");
        await conn.OpenAsync(ct);
        var attachments = new AttachedDatabaseManager(conn);
        
        var aliases = new List<AttachedIntervalAlias>();
        foreach (var ivref in snapshot.Intervals)
        {
            var eventsPath = ivref.Directory.EventsDbPath;
            var slowStatePath = ivref.Directory.SlowStateDbPath;
            // Attach events DB
            var alias = await attachments.AttachAsync(
                eventsPath,
                $"iv_{ivref.Directory.Timestamp.Value}",
                ct);
            aliases.Add(new AttachedIntervalAlias(
                ivref.Directory.Timestamp.Value,
                alias,
                ivref.Role,
                ivref.Directory.StartUtc,
                ivref.Directory.EndUtc));
        }
        
        return new PooledMultiIntervalConnection(conn, attachments, aliases, this);
    }

    public async Task<PooledMultiIntervalConnection> AcquireAsync(CancellationToken ct)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(LiveMultiIntervalReader));
        var pool = _connections ?? throw new InvalidOperationException(
            "LiveMultiIntervalReader not initialized");
        return await pool.Reader.ReadAsync(ct);
    }

    internal async ValueTask ReturnAsync(PooledMultiIntervalConnection conn)
    {
        if (_disposed) { await conn.DisposeUnderlyingAsync(); return; }
        // If the connection was issued from a pool that's been replaced (rotation/retention happened
        // during this query), dispose rather than return
        if (conn.IssuingSnapshot != _currentSnapshot)
        {
            await conn.DisposeUnderlyingAsync();
            return;
        }
        await _connections!.Writer.WriteAsync(conn);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _tracker.SetChanged -= OnSetChangedAsync;
        var pool = _connections;
        if (pool is not null)
        {
            pool.Writer.TryComplete();
            while (pool.Reader.TryRead(out var conn))
                try { await conn.DisposeUnderlyingAsync(); } catch { }
        }
        _refreshLock.Dispose();
    }
}

public sealed class PooledMultiIntervalConnection : IAsyncDisposable
{
    public DuckDBConnection Connection { get; }
    public IReadOnlyList<AttachedIntervalAlias> Intervals { get; }
    internal IntervalSetSnapshot? IssuingSnapshot { get; }
    
    private readonly AttachedDatabaseManager _attachments;
    private readonly LiveMultiIntervalReader _pool;
    private bool _returned;

    internal PooledMultiIntervalConnection(
        DuckDBConnection conn,
        AttachedDatabaseManager attachments,
        IReadOnlyList<AttachedIntervalAlias> intervals,
        LiveMultiIntervalReader pool)
    {
        Connection = conn;
        _attachments = attachments;
        Intervals = intervals;
        _pool = pool;
        // Capture snapshot at creation; pool compares on return
        IssuingSnapshot = null;  // set by caller after construction in RebuildAsync
    }

    public async ValueTask DisposeAsync()
    {
        if (_returned) return;
        _returned = true;
        await _pool.ReturnAsync(this);
    }

    internal async ValueTask DisposeUnderlyingAsync()
    {
        await _attachments.DisposeAsync();
        await Connection.DisposeAsync();
    }
}

public sealed record AttachedIntervalAlias(
    string IntervalTimestamp,
    string Alias,
    IntervalRole Role,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc);
```

**Important property**: queries that started before a rotation continue against their borrowed connection. Their connection holds attachments to the previous set, including any intervals that may have just been demoted or evicted. DuckDB's read-only attached file remains queryable until the connection closes — even if retention deletes the file on disk later. On Windows this works because of the share-delete file open semantics DuckDB uses by default. On Linux (out of scope for Phase 5, but worth noting) the same property holds for different reasons.

**Retention coordination is therefore important**: the retention manager must NOT delete an interval directory while LiveMultiIntervalReader still has it attached. The flow:

1. Retention picks an interval to evict
2. IntervalSetTracker is notified (`OnIntervalEvictedAsync`)
3. Tracker removes from current set, notifies subscribers
4. LiveMultiIntervalReader rebuilds; new connections don't attach the doomed interval
5. **Retention now waits** for in-flight queries against the old pool to finish (their connections dispose on return-to-pool path)
6. Retention deletes the directory

For Phase 5 simplicity, step 5 is implemented as a fixed delay (e.g., 30 seconds) before deletion. A future refinement could use reference counting. The delay is acceptable because retention is not time-critical.

### 3.4 Observer Wiring

In `ObserverHostBuilder` (Phase 3 §3.4), additions:

```csharp
// Multi-interval tracker
builder.Services.AddSingleton<IntervalSetTracker>(sp =>
    new IntervalSetTracker(
        sp.GetRequiredService<IntervalRotator>(),
        completedIntervalsToKeep: sp.GetRequiredService<ObserverConfig>().LiveQueryWindow.CompletedIntervalsToInclude,
        sp.GetRequiredService<ILogger<IntervalSetTracker>>()));

builder.Services.AddSingleton<LiveMultiIntervalReader>();

// Query services now take the multi-interval reader instead of the single-pool variant
// (or both — see §4.1 for the migration strategy)
```

`ObserverConfig` gains:

```csharp
public sealed class ObserverConfig
{
    // ... existing fields ...
    
    public LiveQueryWindowConfig LiveQueryWindow { get; set; } = new();
}

public sealed class LiveQueryWindowConfig
{
    /// <summary>
    /// Number of completed intervals to include in live queries beyond the active one.
    /// Higher = more history visible without opening a bundle; more memory and file handles used.
    /// </summary>
    public int CompletedIntervalsToInclude { get; set; } = 3;
}
```

In `ObserverHostedService.ExecuteAsync` (Phase 3 §3.11), after the rotator opens the current interval:

```csharp
// 2. Open the current interval
await _rotator.OpenCurrentAsync(stoppingToken);

// 2a. Initialize multi-interval tracker now that there's an active interval
await _tracker.InitializeAsync(stoppingToken);

// 3. Initialize the multi-interval reader pool
await _multiReader.InitializeAsync(stoppingToken);
```

And in `RotationLoopAsync`:

```csharp
await _rotator.RotateAsync(ManifestFinalizationReason.ScheduledRotation, ct);

// Tracker observes the rotator's new state and updates the set
await _tracker.OnIntervalRotatedAsync(ct);

// (LiveMultiIntervalReader has subscribed to tracker.SetChanged; rebuilds automatically.)
```

In `RetentionManager` (Phase 2 §6.10), after deleting an interval directory:

```csharp
await _tracker.OnIntervalEvictedAsync(deletedIntervalDir, ct);
```

### 3.5 Phase 3's Single-Pool Coexistence

Phase 3's `ReadOnlyConnectionPool` is single-interval, used by the Scenario View's `ScenarioQueryService`. Phase 5 introduces `LiveMultiIntervalReader` for multi-interval queries. We have two options:

**Option A: Keep both.** The Scenario View continues to use the single-interval pool (it's fine for scenario-level queries within the active interval). The Timeline View uses the multi-interval reader. Two pools coexist.

**Option B: Migrate all queries to multi-interval.** Replace single-interval usage entirely. Slight overhead for scenario queries (UNION ALL over a single attachment is degenerate but real) in exchange for one less abstraction.

**Phase 5 chooses Option B.** Reasons:
- One concept to maintain
- The Scenario View benefits from cross-interval session listing once a session straddles a rotation
- Performance overhead of UNION ALL with one attached source is negligible
- Phase 4's offline viewer already uses MultiIntervalReader in degenerate form (one bundle DB attached); Option B brings the Observer to the same pattern

The migration: `SessionQueryService`, `ScenarioQueryService`, `TopologyQueryService`, `EventLookupService` all change their dependency from `ReadOnlyConnectionPool` to `LiveMultiIntervalReader`. Their SQL changes from `SELECT ... FROM events WHERE ...` to use `MultiIntervalReader.BuildEventsUnionSql(...)`. Phase 3 tests for these services need updates to match the new query shape.

`ReadOnlyConnectionPool` is removed from the Observer's DI. It remains in the codebase only for the offline viewer's bundle scenario where it's similarly redundant — but Phase 5 doesn't refactor the offline viewer; that's a tidy-up for Phase 6.

---

## 4. The Event Query API

The timeline drives all data fetching through a small set of endpoints. The contract: small queries return raw events; large queries return aggregated buckets; the frontend chooses bucket size based on the time range and visible-pixel budget.

### 4.1 Endpoints

```
GET  /api/events                          list events matching filter (small results only)
GET  /api/events/aggregate                aggregate events into time-bucketed counts
GET  /api/live/events                     SSE stream of new events matching a filter
GET  /api/events/{eventId}                single event detail (Phase 3, unchanged)
```

The first two are new; the live SSE is extended (Phase 3 only streamed notables); the lookup is unchanged.

### 4.2 GET /api/events — List Endpoint

Query string parameters:

| Parameter | Type | Required | Meaning |
|---|---|---|---|
| `sessionId` | string | yes | Constrains the query to this session's time range |
| `from` | ISO 8601 UTC | no | Lower bound (inclusive). Defaults to session start. |
| `to` | ISO 8601 UTC | no | Upper bound (exclusive). Defaults to session end (or "now" for active sessions). |
| `topic` | string | no | Repeatable. Multiple topics → OR. |
| `node` | string | no | Repeatable. Multiple nodes → OR. |
| `traceId` | string | no | Hex-encoded 64-bit. Filters to that one trace. |
| `entityId` | string | no | Repeatable. |
| `playerId` | string | no | Repeatable. |
| `severity` | string | no | One of `info`, `warning`, `error`. Repeatable. |
| `notablesOnly` | bool | no | If true, only events with non-null `notable_label`. |
| `limit` | int | no | Hard cap on rows returned. Default 5000, max 5000. |
| `orderBy` | string | no | `publish_wallclock` (default) or `publish_wallclock_desc`. |

Response (`Content-Type: application/json`):

```json
{
  "events": [
    {
      "eventId": "A3F2B4C8D9E0F1A2",
      "traceId": "B4C5D6E7F8A9B0C1",
      "parentEventId": null,
      "publishWallclock": "2026-05-19T14:23:17.143Z",
      "receiveWallclock": "2026-05-19T14:23:17.146Z",
      "publisherNode": "blue-veh-01",
      "subscriberNode": "blue-veh-01",
      "topic": "weapons.fire",
      "sequenceNumber": 1247831,
      "entityId": "vehicle:blue:17",
      "owningPlayerId": "player-12",
      "scenarioPhase": "engagement",
      "severity": "info",
      "notableLabel": null,
      "payloadJson": "{...}"
    }
  ],
  "totalMatching": 4127,
  "returned": 4127,
  "truncated": false
}
```

`totalMatching` is the true count; `returned` may be smaller if the limit was hit. `truncated = (returned < totalMatching)`. The frontend uses this to decide whether to switch to aggregate mode on the next query.

**SQL shape**:

```sql
-- via MultiIntervalReader.BuildEventsUnionSql with whereClause
WITH unioned AS (
    SELECT 'iv_20260519T140000Z' AS __source, * FROM iv_20260519T140000Z.events
        WHERE publish_wallclock >= $from AND publish_wallclock < $to
    UNION ALL
    SELECT 'iv_20260519T150000Z' AS __source, * FROM iv_20260519T150000Z.events
        WHERE publish_wallclock >= $from AND publish_wallclock < $to
)
SELECT * FROM unioned
WHERE
    ($topic_list IS NULL OR topic IN $topic_list)
    AND ($node_list IS NULL OR publisher_node IN $node_list)
    AND ($trace_id IS NULL OR trace_id = $trace_id)
    -- ... etc
ORDER BY publish_wallclock
LIMIT $limit;
```

Pushing the time-range filter into the per-interval SELECT (not the outer WHERE) lets DuckDB use each interval's index on `publish_wallclock`. The optimizer often does this anyway, but being explicit removes uncertainty.

**Count query** (for `totalMatching`):

```sql
-- Same structure, but with COUNT(*). Run as a separate query.
```

Running two queries (one for rows, one for count) is acceptable at Phase 5's scale. A `COUNT(*) OVER ()` window function could fold them, but tests show it adds overhead for small result sets.

### 4.3 GET /api/events/aggregate — Aggregation Endpoint

For zoom levels where row counts exceed the timeline's ability to render, the backend buckets events into fixed time-width windows and returns per-bucket counts.

Query string parameters:

| Parameter | Type | Required | Meaning |
|---|---|---|---|
| `sessionId` | string | yes | as above |
| `from`, `to` | ISO 8601 | yes | exact range (no defaults — aggregate needs precise bucketing) |
| `bucketDuration` | string | yes | One of: `100ms`, `1s`, `5s`, `30s`, `1m`, `5m`, `30m`, `1h` |
| `groupBy` | string | no | `node` (default), `topic`, `severity`, or `none` |
| All filter params from §4.2 | | | applied before aggregation |

Response:

```json
{
  "bucketDuration": "5s",
  "buckets": [
    {
      "bucketStartUtc": "2026-05-19T14:23:15.000Z",
      "groups": [
        { "groupKey": "blue-cmd-01", "count": 142 },
        { "groupKey": "blue-veh-01", "count": 1187 },
        { "groupKey": "red-cmd-01",  "count": 88 }
      ],
      "total": 1417
    },
    {
      "bucketStartUtc": "2026-05-19T14:23:20.000Z",
      "groups": [...],
      "total": 1389
    }
  ]
}
```

If `groupBy=none`, each bucket has a single anonymous group with `groupKey: null` and `count: total`.

**SQL shape** (for `groupBy=node`, `bucketDuration=5s`):

```sql
WITH unioned AS (...),  -- same as list query
filtered AS (
    SELECT * FROM unioned WHERE ...  -- filter predicates
)
SELECT
    time_bucket(INTERVAL '5 seconds', publish_wallclock) AS bucket_start,
    publisher_node AS group_key,
    COUNT(*) AS cnt
FROM filtered
GROUP BY bucket_start, group_key
ORDER BY bucket_start, group_key;
```

DuckDB's `time_bucket` function does the bucketing efficiently using the index on `publish_wallclock`. For a 1-hour range with 5s buckets across 5 nodes, the result has at most 720 × 5 = 3,600 rows — well within JSON serialization budget.

**Bucket duration choice** is the frontend's job (see §6.5). The backend trusts the requested bucket and returns it.

### 4.4 Filter Composition Rules

All filters compose with AND. Within a single filter that accepts multiple values (e.g., `topic=foo&topic=bar`), values are OR'd. So `topic=foo&topic=bar&severity=warning` means `(topic=foo OR topic=bar) AND severity=warning`.

The frontend's filter UI mirrors this: each filter chip is one logical filter; multi-select within a chip is OR; multiple chips compose with AND.

Filters that operate on top-level columns (`topic`, `publisher_node`, `trace_id`, `entity_id`, `owning_player_id`, `severity`, `notable_label IS NOT NULL`) are pushed into the SQL WHERE. Filters that would need JSON payload search are **not supported in Phase 5** — they'd defeat the column-pruning optimizer wins. A future SQL console (Phase 10) can express these as the user's explicit choice.

### 4.5 EventQueryService

```csharp
namespace Tracer.WebApi.Queries;

public sealed class EventQueryService
{
    private readonly LiveMultiIntervalReader _reader;
    private readonly ILogger<EventQueryService> _logger;

    public EventQueryService(LiveMultiIntervalReader reader, ILogger<EventQueryService> logger)
    {
        _reader = reader;
        _logger = logger;
    }

    public async Task<EventListResult> ListAsync(EventQuery query, CancellationToken ct)
    {
        await using var conn = await _reader.AcquireAsync(ct);
        
        // Build the union SQL with time-range pushdown
        var whereClause = "WHERE publish_wallclock >= $from AND publish_wallclock < $to";
        var unionSql = conn.BuildEventsUnionSql(whereClause: whereClause);
        
        // Build outer filter predicates
        var (filterSql, parameters) = QueryPredicateBuilder.Build(query);
        
        var listSql = $"""
            WITH unioned AS ({unionSql})
            SELECT * FROM unioned
            {filterSql}
            ORDER BY publish_wallclock {(query.OrderDescending ? "DESC" : "ASC")}
            LIMIT $limit;
            """;
        
        var countSql = $"""
            WITH unioned AS ({unionSql})
            SELECT COUNT(*) FROM unioned
            {filterSql};
            """;
        
        // Execute both queries
        var events = new List<EventRecord>();
        await using (var cmd = conn.Connection.CreateCommand())
        {
            cmd.CommandText = listSql;
            QueryPredicateBuilder.BindParameters(cmd, query, parameters);
            cmd.Parameters.Add(new DuckDBParameter("from", query.From.ToDateTimeOffset()));
            cmd.Parameters.Add(new DuckDBParameter("to",   query.To.ToDateTimeOffset()));
            cmd.Parameters.Add(new DuckDBParameter("limit", query.Limit));
            
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                events.Add(EventRecordMapper.FromReader(reader));
        }
        
        long totalMatching;
        await using (var cmd = conn.Connection.CreateCommand())
        {
            cmd.CommandText = countSql;
            QueryPredicateBuilder.BindParameters(cmd, query, parameters);
            cmd.Parameters.Add(new DuckDBParameter("from", query.From.ToDateTimeOffset()));
            cmd.Parameters.Add(new DuckDBParameter("to",   query.To.ToDateTimeOffset()));
            totalMatching = (long)(await cmd.ExecuteScalarAsync(ct))!;
        }
        
        return new EventListResult
        {
            Events = events,
            TotalMatching = totalMatching,
            Returned = events.Count,
            Truncated = events.Count < totalMatching
        };
    }
}

public sealed record EventQuery
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
    public required IReadOnlyList<EventRecord> Events { get; init; }
    public required long TotalMatching { get; init; }
    public required int Returned { get; init; }
    public required bool Truncated { get; init; }
}
```

### 4.6 EventAggregationService

```csharp
namespace Tracer.WebApi.Queries;

public sealed class EventAggregationService
{
    private readonly LiveMultiIntervalReader _reader;
    private readonly ILogger<EventAggregationService> _logger;

    public EventAggregationService(LiveMultiIntervalReader reader, ILogger<EventAggregationService> logger)
    {
        _reader = reader;
        _logger = logger;
    }

    public async Task<AggregateResult> AggregateAsync(AggregateQuery query, CancellationToken ct)
    {
        ValidateBucket(query.BucketDuration);
        var bucketSql = ToDuckDbInterval(query.BucketDuration);
        var groupByExpr = query.GroupBy switch
        {
            AggregateGroupBy.Node     => "publisher_node",
            AggregateGroupBy.Topic    => "topic",
            AggregateGroupBy.Severity => "severity",
            AggregateGroupBy.None     => "NULL",  // single group per bucket
            _ => throw new ArgumentException("Invalid GroupBy")
        };
        
        await using var conn = await _reader.AcquireAsync(ct);
        var whereClause = "WHERE publish_wallclock >= $from AND publish_wallclock < $to";
        var unionSql = conn.BuildEventsUnionSql(whereClause: whereClause);
        var (filterSql, parameters) = QueryPredicateBuilder.Build(query);
        
        var sql = $"""
            WITH unioned AS ({unionSql}),
            filtered AS (SELECT * FROM unioned {filterSql})
            SELECT
                time_bucket(INTERVAL '{bucketSql}', publish_wallclock) AS bucket_start,
                {groupByExpr} AS group_key,
                COUNT(*) AS cnt
            FROM filtered
            GROUP BY bucket_start, group_key
            ORDER BY bucket_start, group_key;
            """;
        
        await using var cmd = conn.Connection.CreateCommand();
        cmd.CommandText = sql;
        QueryPredicateBuilder.BindParameters(cmd, query, parameters);
        cmd.Parameters.Add(new DuckDBParameter("from", query.From.ToDateTimeOffset()));
        cmd.Parameters.Add(new DuckDBParameter("to",   query.To.ToDateTimeOffset()));
        
        // Read into bucket structure
        var bucketsByStart = new SortedDictionary<DateTimeOffset, List<AggregateGroup>>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var bucketStart = (DateTimeOffset)reader.GetDateTime(0);
            var groupKey = reader.IsDBNull(1) ? null : reader.GetString(1);
            var count = reader.GetInt64(2);
            
            if (!bucketsByStart.TryGetValue(bucketStart, out var groups))
                bucketsByStart[bucketStart] = groups = new();
            groups.Add(new AggregateGroup(groupKey, count));
        }
        
        var buckets = bucketsByStart.Select(kvp => new AggregateBucket(
            kvp.Key,
            kvp.Value,
            kvp.Value.Sum(g => g.Count))).ToList();
        
        return new AggregateResult { BucketDuration = query.BucketDuration, Buckets = buckets };
    }

    private static void ValidateBucket(string bucket)
    {
        var allowed = new[] { "100ms", "1s", "5s", "30s", "1m", "5m", "30m", "1h" };
        if (!allowed.Contains(bucket))
            throw new ArgumentException(
                $"bucketDuration must be one of {string.Join(", ", allowed)}");
    }

    private static string ToDuckDbInterval(string bucket) => bucket switch
    {
        "100ms" => "100 milliseconds",
        "1s"    => "1 second",
        "5s"    => "5 seconds",
        "30s"   => "30 seconds",
        "1m"    => "1 minute",
        "5m"    => "5 minutes",
        "30m"   => "30 minutes",
        "1h"    => "1 hour",
        _ => throw new ArgumentOutOfRangeException()
    };
}

public sealed record AggregateQuery
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

public enum AggregateGroupBy { Node, Topic, Severity, None }

public sealed record AggregateResult
{
    public required string BucketDuration { get; init; }
    public required IReadOnlyList<AggregateBucket> Buckets { get; init; }
}

public sealed record AggregateBucket(DateTimeOffset BucketStartUtc, IReadOnlyList<AggregateGroup> Groups, long Total);
public sealed record AggregateGroup(string? GroupKey, long Count);
```

### 4.7 SSE for Live Events (Extended from Phase 3)

Phase 3's `/api/live/notables` streamed only notable events. Phase 5 adds `/api/live/events` which streams all events matching a filter.

The endpoint shape is similar; the differences:

- Filter parameters mirror the list endpoint's full set
- The broadcaster's `Publish(EventRecord)` (Phase 3 §5.1) is reused — same hot path
- `SseFilter` is extended to evaluate the full filter expression server-side per event

```csharp
namespace Tracer.WebApi.Streaming;

public sealed record SseFilter
{
    public string? SessionId { get; init; }
    public IReadOnlySet<string>? Topics { get; init; }
    public IReadOnlySet<string>? Nodes { get; init; }
    public string? TraceId { get; init; }
    public IReadOnlySet<string>? EntityIds { get; init; }
    public IReadOnlySet<string>? PlayerIds { get; init; }
    public IReadOnlySet<string>? Severities { get; init; }
    public bool NotablesOnly { get; init; }

    public bool Matches(EventRecord ev)
    {
        if (NotablesOnly && string.IsNullOrEmpty(ev.NotableLabel)) return false;
        if (Topics is not null && !Topics.Contains(ev.Topic)) return false;
        if (Nodes is not null && !Nodes.Contains(ev.PublisherNode)) return false;
        if (TraceId is not null && ev.TraceId.ToString("X16") != TraceId) return false;
        if (EntityIds is not null && (ev.EntityId is null || !EntityIds.Contains(ev.EntityId))) return false;
        if (PlayerIds is not null && (ev.OwningPlayerId is null || !PlayerIds.Contains(ev.OwningPlayerId))) return false;
        if (Severities is not null && (ev.Severity is null || !Severities.Contains(ev.Severity))) return false;
        // SessionId filter: Phase 5 simplification — broadcast all events to all session subscribers
        // (frontend filters by time range). Server-side session-to-event correlation deferred.
        return true;
    }
}
```

**Hot-path performance**: every captured event goes through every connected SSE client's `Matches`. With ~10 clients and ~1000 events/sec, that's 10,000 `Matches` calls per second — trivial. The HashSet lookups are O(1). String comparisons are short-circuited.

### 4.8 Endpoint Implementations

`EventEndpoints` is extended:

```csharp
namespace Tracer.WebApi.Endpoints;

public static class EventEndpoints
{
    public static void Map(WebApplication app)
    {
        // Phase 3
        app.MapGet("/api/events/{eventId}", HandleGetByIdAsync).WithOpenApi();
        // Phase 5
        app.MapGet("/api/events", HandleListAsync).WithOpenApi();
        app.MapGet("/api/events/aggregate", HandleAggregateAsync).WithOpenApi();
    }

    public static async Task<Results<Ok<EventListDto>, ProblemHttpResult>> HandleListAsync(
        [FromQuery] string sessionId,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] string[]? topic,
        [FromQuery] string[]? node,
        [FromQuery] string? traceId,
        [FromQuery] string[]? entityId,
        [FromQuery] string[]? playerId,
        [FromQuery] string[]? severity,
        [FromQuery] bool notablesOnly = false,
        [FromQuery] int limit = 5000,
        [FromQuery] string? orderBy = null,
        [FromServices] EventQueryService events = default!,
        [FromServices] SessionQueryService sessions = default!,
        CancellationToken ct = default)
    {
        if (limit < 1 || limit > 5000)
            return TypedResults.Problem(new ProblemDetails
            {
                Title = "limit out of range",
                Detail = "limit must be 1..5000",
                Status = StatusCodes.Status400BadRequest
            });
        
        var session = await sessions.GetAsync(sessionId, ct);
        if (session is null)
            return TypedResults.Problem(new ProblemDetails
            {
                Title = "Session not found",
                Status = StatusCodes.Status404NotFound
            });
        
        var query = new EventQuery
        {
            SessionId = sessionId,
            From = from is not null
                ? WallclockTime.FromDateTimeOffset(from.Value)
                : WallclockTime.FromDateTimeOffset(session.StartUtc),
            To = to is not null
                ? WallclockTime.FromDateTimeOffset(to.Value)
                : WallclockTime.FromDateTimeOffset(session.EndUtc ?? DateTimeOffset.UtcNow),
            Topics = topic,
            Nodes = node,
            TraceId = traceId,
            EntityIds = entityId,
            PlayerIds = playerId,
            Severities = severity,
            NotablesOnly = notablesOnly,
            Limit = limit,
            OrderDescending = orderBy == "publish_wallclock_desc"
        };
        
        var result = await events.ListAsync(query, ct);
        return TypedResults.Ok(EventListDtoMapper.Map(result));
    }

    public static async Task<Results<Ok<EventAggregateDto>, ProblemHttpResult>> HandleAggregateAsync(
        [FromQuery] string sessionId,
        [FromQuery] DateTimeOffset from,
        [FromQuery] DateTimeOffset to,
        [FromQuery] string bucketDuration,
        [FromQuery] string? groupBy,
        [FromQuery] string[]? topic,
        [FromQuery] string[]? node,
        [FromQuery] string? traceId,
        [FromQuery] string[]? entityId,
        [FromQuery] string[]? playerId,
        [FromQuery] string[]? severity,
        [FromQuery] bool notablesOnly = false,
        [FromServices] EventAggregationService aggregation = default!,
        CancellationToken ct = default)
    {
        var groupByEnum = (groupBy ?? "node").ToLowerInvariant() switch
        {
            "node"     => AggregateGroupBy.Node,
            "topic"    => AggregateGroupBy.Topic,
            "severity" => AggregateGroupBy.Severity,
            "none"     => AggregateGroupBy.None,
            _ => AggregateGroupBy.Node
        };
        
        try
        {
            var query = new AggregateQuery
            {
                SessionId = sessionId,
                From = WallclockTime.FromDateTimeOffset(from),
                To   = WallclockTime.FromDateTimeOffset(to),
                BucketDuration = bucketDuration,
                GroupBy = groupByEnum,
                Topics = topic, Nodes = node, TraceId = traceId,
                EntityIds = entityId, PlayerIds = playerId,
                Severities = severity, NotablesOnly = notablesOnly
            };
            var result = await aggregation.AggregateAsync(query, ct);
            return TypedResults.Ok(EventAggregateDtoMapper.Map(result));
        }
        catch (ArgumentException ex)
        {
            return TypedResults.Problem(new ProblemDetails
            {
                Title = "Invalid argument",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }
    }
}
```

---

## 5. The Timeline View — Frontend Architecture

### 5.1 Overall Layout

```
+---------------------------------------------------------------+
| AppHeader: session label, live indicator, bundle/live mode    |
+---------------------------------------------------------------+
| TimelineToolbar: zoom presets, follow toggle, density badge   |
+----------+------------------------------------+----------------+
|          |                                    |                |
|          |                                    |                |
|  Filter  |        Timeline Canvas             |   Event        |
|  Panel   |  (multi-node swimlane)             |  Inspector     |
|          |                                    |   (optional)   |
| (~280px) |   (flex; main content)             |   (~400px)     |
|          |                                    |                |
|          +------------------------------------+                |
|          | TimelineAxis (wallclock ticks)     |                |
+----------+------------------------------------+----------------+
```

- **FilterPanel**: left rail, fixed width. Lists active filters as chips with add/remove. Expandable sections for each filter type (topic, node, trace, etc.).
- **TimelineCanvas**: main content. The Canvas2D rendering surface. Pan with horizontal drag; zoom with wheel.
- **TimelineAxis**: thin band below the canvas, time labels.
- **TimelineToolbar**: above the canvas, controls zoom presets ("5m", "1h", "Full session"), Follow toggle (auto-scroll to live edge), density indicator ("showing 47 of 1.2M events" or "buckets of 5s").
- **EventInspector**: right rail when an event is selected. Hidden otherwise.

The grid uses CSS Grid for adaptive layout. On narrower screens, the inspector becomes a modal overlay; the filter panel collapses to a button. Phase 5 doesn't optimize for mobile.

### 5.2 Component Responsibilities

```typescript
// TimelineView.vue — top-level page
//  - Reads URL parameters via useTimelineUrl
//  - Drives the timeline store
//  - Renders the layout shell
//  - Hosts FilterPanel, TimelineCanvas, EventInspector

// TimelineCanvas.vue — the canvas itself
//  - Mounts a <canvas> element
//  - Calls into rendering/timelineRenderer.ts on every viewport change
//  - Forwards pointer events (pan, zoom, click) to handlers
//  - Builds the hit-test index after each render

// FilterPanel.vue
//  - Reads filter state from store
//  - Lets the user add/remove filters; on change, store.applyFilter() triggers re-fetch

// EventInspector.vue
//  - Reads selectionStore.selectedEventId
//  - Fetches /api/events/{id} on selection change
//  - Renders payload JSON pretty-printed
//  - Shows pivot buttons: Filter to trace, Show in scenario, etc.

// TimelineToolbar.vue
//  - Zoom presets and the density badge
//  - Follow-live toggle (only enabled when current viewport's end is near "now")

// TimelineAxis.vue
//  - Wallclock tick labels (formatted by zoom level: hours / minutes / seconds / ms)
//  - Renders to an SVG (it's low-density chrome; SVG is fine here)
```

### 5.3 timelineStore (Pinia)

```typescript
// src/stores/timelineStore.ts
import { defineStore } from 'pinia';
import type { TimeRange, TimelineFilter, EventListDto, EventAggregateDto } from '@/types/timeline';

export const useTimelineStore = defineStore('timeline', {
  state: () => ({
    sessionId: null as string | null,
    viewport: {
      from: new Date(),
      to: new Date(),
      followLive: false,
    },
    filter: { } as TimelineFilter,
    queryResult: null as EventListDto | EventAggregateDto | null,
    queryMode: 'list' as 'list' | 'aggregate',
    loading: false,
    error: null as string | null,
    selectedEventId: null as string | null,
  }),
  actions: {
    setSession(sessionId: string) { this.sessionId = sessionId; },
    setViewport(range: TimeRange, follow = false) {
      this.viewport = { from: range.from, to: range.to, followLive: follow };
    },
    applyFilter(filter: TimelineFilter) {
      this.filter = filter;
    },
    setQueryResult(result: EventListDto | EventAggregateDto, mode: 'list' | 'aggregate') {
      this.queryResult = result;
      this.queryMode = mode;
    },
    selectEvent(eventId: string | null) {
      this.selectedEventId = eventId;
    },
    panBy(milliseconds: number) {
      this.viewport = {
        from: new Date(this.viewport.from.getTime() + milliseconds),
        to:   new Date(this.viewport.to.getTime()   + milliseconds),
        followLive: false,  // pan disables follow
      };
    },
    zoomBy(factor: number, centerMs: number) {
      const fromMs = this.viewport.from.getTime();
      const toMs   = this.viewport.to.getTime();
      const newSpan = (toMs - fromMs) * factor;
      this.viewport = {
        from: new Date(centerMs - newSpan / 2),
        to:   new Date(centerMs + newSpan / 2),
        followLive: false,
      };
    },
  },
});
```

The store is the single source of truth for viewport, filter, and selection. Components read reactively; mutations trigger re-fetch via `useTimelineQuery` (§5.4).

### 5.4 useTimelineQuery — the data-fetching driver

This composable watches the store's viewport+filter and fetches the right query (list vs aggregate). It's the orchestration brain of the view.

```typescript
// src/composables/useTimelineQuery.ts
import { watch, ref } from 'vue';
import { useTimelineStore } from '@/stores/timelineStore';
import { useApi } from '@/api/useApi';
import { chooseBucketDuration } from '@/rendering/timelineLayout';

export function useTimelineQuery() {
  const store = useTimelineStore();
  const api = useApi();
  let abortCtrl: AbortController | null = null;

  watch(
    () => ({
      sessionId: store.sessionId,
      from: store.viewport.from.getTime(),
      to:   store.viewport.to.getTime(),
      filter: JSON.stringify(store.filter),  // simple change detection
    }),
    async (curr, prev) => {
      if (!curr.sessionId) return;
      
      // Cancel any in-flight query
      abortCtrl?.abort();
      abortCtrl = new AbortController();
      
      store.loading = true;
      store.error = null;
      
      try {
        // Decide list vs aggregate based on visible-time-span
        const spanMs = curr.to - curr.from;
        const bucket = chooseBucketDuration(spanMs);
        
        if (bucket === 'raw') {
          const result = await api.listEvents({
            sessionId: curr.sessionId,
            from: new Date(curr.from),
            to:   new Date(curr.to),
            ...store.filter,
            limit: 5000,
          }, { signal: abortCtrl.signal });
          store.setQueryResult(result, 'list');
        } else {
          const result = await api.aggregateEvents({
            sessionId: curr.sessionId,
            from: new Date(curr.from),
            to:   new Date(curr.to),
            bucketDuration: bucket,
            groupBy: 'node',
            ...store.filter,
          }, { signal: abortCtrl.signal });
          store.setQueryResult(result, 'aggregate');
        }
      } catch (err: any) {
        if (err.name === 'AbortError') return;  // expected on viewport change
        store.error = err.message ?? 'Query failed';
      } finally {
        store.loading = false;
      }
    },
    { immediate: true, deep: false }
  );
}
```

**Cancellation matters**: pan/zoom interactions generate rapid viewport changes. Without cancellation, a slow query for the previous viewport could overwrite the result of a faster query for the new viewport. `AbortController` keeps the latest query winning.

**Single-flight vs queued**: we cancel-and-restart rather than queue. The user only ever sees the result of their most recent interaction; intermediate states are wasted work.

### 5.5 Bucket choice (timelineLayout.chooseBucketDuration)

```typescript
// src/rendering/timelineLayout.ts

/**
 * Choose the bucket duration based on visible time-span.
 * Returns 'raw' when the span is small enough that raw events fit in the row budget.
 */
export function chooseBucketDuration(spanMs: number): string {
  const fourHours = 4 * 60 * 60 * 1000;
  const oneHour   = 1 * 60 * 60 * 1000;
  const thirtyMin = 30 * 60 * 1000;
  const fiveMin   = 5 * 60 * 1000;
  const oneMin    = 1 * 60 * 1000;
  const oneSec    = 1000;
  
  if (spanMs >= fourHours) return '5m';
  if (spanMs >= oneHour)   return '30s';
  if (spanMs >= thirtyMin) return '5s';
  if (spanMs >= fiveMin)   return '1s';
  if (spanMs >= oneMin)    return '100ms';
  // Below 1 minute: raw events. Even a busy session won't exceed the 5000-row cap here.
  return 'raw';
}
```

These thresholds derive from the visible pixel budget. At 1200px wide, "5m buckets across 4 hours" is 48 buckets — 25px per bucket — drawable. "raw across 4 hours" at 1000 events/sec is 14M events — far over budget.

Phase 5 ships with these thresholds. They're tuneable in `timelineLayout.ts`; feedback in real use is expected to refine them.

### 5.6 timelineRenderer — the Canvas drawing module

```typescript
// src/rendering/timelineRenderer.ts

import type { EventListDto, EventAggregateDto } from '@/types/timeline';

export interface TimelineRenderInput {
  // Viewport
  fromMs: number;
  toMs:   number;
  
  // Canvas dimensions
  widthPx:  number;
  heightPx: number;
  
  // Swimlane layout
  nodes: string[];                // ordered top-to-bottom
  swimlaneHeightPx: number;
  
  // Data
  mode: 'list' | 'aggregate';
  list?: EventListDto;
  aggregate?: EventAggregateDto;
  
  // Highlight (e.g., selected event)
  selectedEventId?: string | null;
  
  // Color provider
  nodeColors: Map<string, string>;
  severityColors: { info: string; warning: string; error: string };
}

export interface TimelineRenderOutput {
  // Spatial index for hit-testing; built during render
  hitIndex: HitIndex;
}

export function render(ctx: CanvasRenderingContext2D, input: TimelineRenderInput): TimelineRenderOutput {
  clearCanvas(ctx, input);
  drawSwimlaneBackgrounds(ctx, input);
  drawGridlines(ctx, input);
  
  const hitIndex = new HitIndex(input.widthPx, input.heightPx);
  
  if (input.mode === 'list' && input.list) {
    drawRawEvents(ctx, input, input.list, hitIndex);
  } else if (input.mode === 'aggregate' && input.aggregate) {
    drawAggregateBuckets(ctx, input, input.aggregate, hitIndex);
  }
  
  drawSelectionHighlight(ctx, input);
  return { hitIndex };
}

function drawRawEvents(
  ctx: CanvasRenderingContext2D,
  input: TimelineRenderInput,
  list: EventListDto,
  hitIndex: HitIndex
) {
  const xScale = input.widthPx / (input.toMs - input.fromMs);
  const nodeRowY = new Map<string, number>();
  input.nodes.forEach((n, i) => nodeRowY.set(n, i * input.swimlaneHeightPx + input.swimlaneHeightPx / 2));
  
  for (const ev of list.events) {
    const tMs = new Date(ev.publishWallclock).getTime();
    const x = (tMs - input.fromMs) * xScale;
    if (x < 0 || x > input.widthPx) continue;  // outside viewport (shouldn't happen but defensive)
    
    const y = nodeRowY.get(ev.publisherNode);
    if (y === undefined) continue;
    
    const color = ev.severity === 'error'   ? input.severityColors.error
               : ev.severity === 'warning' ? input.severityColors.warning
               : input.nodeColors.get(ev.publisherNode) ?? '#888';
    
    // Marker: 3px radius circle, or 5px square if notable
    ctx.fillStyle = color;
    if (ev.notableLabel) {
      ctx.fillRect(x - 2.5, y - 2.5, 5, 5);
      hitIndex.add({ x, y, w: 5, h: 5, eventId: ev.eventId });
    } else {
      ctx.beginPath();
      ctx.arc(x, y, 3, 0, Math.PI * 2);
      ctx.fill();
      hitIndex.add({ x, y, w: 6, h: 6, eventId: ev.eventId });
    }
  }
}

function drawAggregateBuckets(
  ctx: CanvasRenderingContext2D,
  input: TimelineRenderInput,
  agg: EventAggregateDto,
  hitIndex: HitIndex
) {
  const xScale = input.widthPx / (input.toMs - input.fromMs);
  const nodeIdx = new Map<string, number>();
  input.nodes.forEach((n, i) => nodeIdx.set(n, i));
  
  // Compute max bucket total for height normalization (per-node bars sized relative)
  let maxBucketTotal = 0;
  for (const b of agg.buckets) {
    if (b.total > maxBucketTotal) maxBucketTotal = b.total;
  }
  if (maxBucketTotal === 0) return;
  
  const bucketMs = parseBucketDurationMs(agg.bucketDuration);
  const barWidthPx = Math.max(2, bucketMs * xScale - 1);  // 1px gap between buckets
  
  for (const b of agg.buckets) {
    const bucketStartMs = new Date(b.bucketStartUtc).getTime();
    const x = (bucketStartMs - input.fromMs) * xScale;
    
    for (const g of b.groups) {
      if (!g.groupKey) continue;
      const rowI = nodeIdx.get(g.groupKey);
      if (rowI === undefined) continue;
      
      const swimlaneTop = rowI * input.swimlaneHeightPx;
      // Bar height scaled to fraction of max
      const heightFrac = g.count / maxBucketTotal;
      const barH = heightFrac * (input.swimlaneHeightPx - 6);  // 3px padding top/bottom
      const barY = swimlaneTop + input.swimlaneHeightPx - 3 - barH;
      
      ctx.fillStyle = input.nodeColors.get(g.groupKey) ?? '#888';
      ctx.fillRect(x, barY, barWidthPx, barH);
      hitIndex.addBucket({
        x, y: swimlaneTop, w: barWidthPx, h: input.swimlaneHeightPx,
        bucketStartUtc: b.bucketStartUtc,
        nodeId: g.groupKey,
        count: g.count
      });
    }
  }
}

function clearCanvas(ctx: CanvasRenderingContext2D, input: TimelineRenderInput) {
  ctx.clearRect(0, 0, input.widthPx, input.heightPx);
}

function drawSwimlaneBackgrounds(ctx: CanvasRenderingContext2D, input: TimelineRenderInput) {
  input.nodes.forEach((n, i) => {
    if (i % 2 === 1) {
      ctx.fillStyle = 'rgba(255, 255, 255, 0.02)';
      ctx.fillRect(0, i * input.swimlaneHeightPx, input.widthPx, input.swimlaneHeightPx);
    }
  });
}

function drawGridlines(ctx: CanvasRenderingContext2D, input: TimelineRenderInput) { /* vertical lines */ }
function drawSelectionHighlight(ctx: CanvasRenderingContext2D, input: TimelineRenderInput) { /* highlight selected */ }

function parseBucketDurationMs(s: string): number {
  const m = s.match(/^(\d+)(ms|s|m|h)$/);
  if (!m) return 1000;
  const v = parseInt(m[1]);
  return m[2] === 'ms' ? v
       : m[2] === 's'  ? v * 1000
       : m[2] === 'm'  ? v * 60 * 1000
       : v * 60 * 60 * 1000;
}
```

The renderer is **pure**: it takes input, calls into the 2D context. It builds a hit-index as it draws so hover/click can find what's under the pointer fast. The renderer is **testable**: with a mock `CanvasRenderingContext2D` (jsdom + canvas-mock), unit tests can verify the right marker count is drawn at the right coordinates for a given input.

### 5.7 HitIndex

```typescript
// src/rendering/timelineHitTest.ts

interface MarkerHitEntry {
  x: number; y: number; w: number; h: number;
  eventId: string;
}

interface BucketHitEntry {
  x: number; y: number; w: number; h: number;
  bucketStartUtc: string;
  nodeId: string;
  count: number;
}

/**
 * Simple uniform-grid spatial index for hit-testing.
 * Tuned to canvas dimensions; 64x16 cells works at 1200x600 (about 19px cell width).
 */
export class HitIndex {
  private readonly cols = 64;
  private readonly rows = 16;
  private readonly cellW: number;
  private readonly cellH: number;
  private readonly markers: MarkerHitEntry[][];
  private readonly buckets: BucketHitEntry[][];
  
  constructor(widthPx: number, heightPx: number) {
    this.cellW = widthPx / this.cols;
    this.cellH = heightPx / this.rows;
    this.markers = Array.from({ length: this.cols * this.rows }, () => []);
    this.buckets = Array.from({ length: this.cols * this.rows }, () => []);
  }
  
  add(entry: MarkerHitEntry) {
    for (const idx of this.cellsFor(entry.x, entry.y, entry.w, entry.h)) {
      this.markers[idx].push(entry);
    }
  }
  
  addBucket(entry: BucketHitEntry) {
    for (const idx of this.cellsFor(entry.x, entry.y, entry.w, entry.h)) {
      this.buckets[idx].push(entry);
    }
  }
  
  findMarkerAt(x: number, y: number): MarkerHitEntry | null {
    const idx = this.cellIndex(x, y);
    if (idx < 0) return null;
    // Within the cell, pick the closest marker to (x, y)
    let best: MarkerHitEntry | null = null;
    let bestDist = Infinity;
    for (const m of this.markers[idx]) {
      const dx = m.x - x;
      const dy = m.y - y;
      const d = dx * dx + dy * dy;
      if (d < bestDist && d < (m.w / 2) * (m.w / 2) + (m.h / 2) * (m.h / 2)) {
        bestDist = d;
        best = m;
      }
    }
    return best;
  }
  
  findBucketAt(x: number, y: number): BucketHitEntry | null {
    const idx = this.cellIndex(x, y);
    if (idx < 0) return null;
    for (const b of this.buckets[idx]) {
      if (x >= b.x && x <= b.x + b.w && y >= b.y && y <= b.y + b.h) return b;
    }
    return null;
  }
  
  private cellIndex(x: number, y: number): number {
    const c = Math.floor(x / this.cellW);
    const r = Math.floor(y / this.cellH);
    if (c < 0 || c >= this.cols || r < 0 || r >= this.rows) return -1;
    return r * this.cols + c;
  }
  
  private *cellsFor(x: number, y: number, w: number, h: number): IterableIterator<number> {
    const c0 = Math.max(0, Math.floor(x / this.cellW));
    const c1 = Math.min(this.cols - 1, Math.floor((x + w) / this.cellW));
    const r0 = Math.max(0, Math.floor(y / this.cellH));
    const r1 = Math.min(this.rows - 1, Math.floor((y + h) / this.cellH));
    for (let r = r0; r <= r1; r++)
      for (let c = c0; c <= c1; c++)
        yield r * this.cols + c;
  }
}
```

A uniform grid is enough — markers are small, distributed, and we're not querying ranges, just points. KD-trees or interval trees would be over-engineering at the scale Phase 5 needs.

### 5.8 useCanvasRenderer

```typescript
// src/composables/useCanvasRenderer.ts
import { ref, watchEffect, onMounted, onBeforeUnmount } from 'vue';
import { useTimelineStore } from '@/stores/timelineStore';
import { render, type TimelineRenderInput } from '@/rendering/timelineRenderer';
import type { HitIndex } from '@/rendering/timelineHitTest';

export function useCanvasRenderer(canvasRef: Ref<HTMLCanvasElement | null>) {
  const store = useTimelineStore();
  const hitIndex = ref<HitIndex | null>(null);
  
  // Trigger render on viewport, query result, or canvas resize
  watchEffect(() => {
    const canvas = canvasRef.value;
    if (!canvas) return;
    const ctx = canvas.getContext('2d');
    if (!ctx) return;
    if (!store.queryResult) {
      ctx.clearRect(0, 0, canvas.width, canvas.height);
      return;
    }
    
    // DPI-correct sizing
    const dpr = window.devicePixelRatio || 1;
    const cssWidth  = canvas.clientWidth;
    const cssHeight = canvas.clientHeight;
    if (canvas.width !== cssWidth * dpr) canvas.width = cssWidth * dpr;
    if (canvas.height !== cssHeight * dpr) canvas.height = cssHeight * dpr;
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    
    const input: TimelineRenderInput = {
      fromMs: store.viewport.from.getTime(),
      toMs:   store.viewport.to.getTime(),
      widthPx:  cssWidth,
      heightPx: cssHeight,
      nodes: extractNodes(),  // computed from store
      swimlaneHeightPx: cssHeight / Math.max(1, extractNodes().length),
      mode: store.queryMode,
      list: store.queryMode === 'list' ? store.queryResult : undefined,
      aggregate: store.queryMode === 'aggregate' ? store.queryResult : undefined,
      selectedEventId: store.selectedEventId,
      nodeColors: buildNodeColorMap(extractNodes()),
      severityColors: { info: '#5b9dff', warning: '#e8b048', error: '#e85c5c' },
    };
    
    const result = render(ctx, input);
    hitIndex.value = result.hitIndex;
  });
  
  return { hitIndex };
}
```

DPI-correct sizing is essential. On high-resolution displays (most modern laptops), failing to scale by `devicePixelRatio` produces blurry markers.

`watchEffect` re-runs whenever any of its read reactive deps change — viewport, query result, store filter. That's the right granularity: every meaningful change re-renders.

---

## 6. Pan, Zoom, Hover, Click

### 6.1 Pan

Horizontal drag on the canvas pans the viewport. The handler converts pixel-deltas to time-deltas using the current viewport scale.

```typescript
// inside TimelineCanvas.vue setup
import { useTimelineStore } from '@/stores/timelineStore';

const store = useTimelineStore();
let dragging = false;
let lastX = 0;

function onPointerDown(e: PointerEvent) {
  dragging = true;
  lastX = e.clientX;
  (e.target as Element).setPointerCapture(e.pointerId);
}

function onPointerMove(e: PointerEvent) {
  if (!dragging) {
    handleHover(e);
    return;
  }
  const dx = e.clientX - lastX;
  lastX = e.clientX;
  const canvasWidth = (e.target as HTMLCanvasElement).clientWidth;
  const spanMs = store.viewport.to.getTime() - store.viewport.from.getTime();
  const dtMs = -(dx / canvasWidth) * spanMs;  // drag right = pan time backward
  store.panBy(dtMs);
}

function onPointerUp(e: PointerEvent) {
  dragging = false;
  (e.target as Element).releasePointerCapture(e.pointerId);
}
```

Panning fires viewport changes which `useTimelineQuery` debounces (next subsection).

### 6.2 Debouncing Query Fetches

Pan and zoom generate rapid viewport updates. Without throttling, we'd fire 60+ HTTP queries per second. We use a 100ms debounce: viewport changes update the store immediately (so the canvas re-renders with the cached data shifted), but the actual data fetch waits.

```typescript
// extension to useTimelineQuery
import { debounce } from '@/utils/debounce';

const fetchDebounced = debounce(async () => {
  // ... the fetch logic from §5.4 ...
}, 100);

watch(/* viewport+filter */, fetchDebounced, { immediate: true });
```

**Critical UX detail**: during pan/zoom the canvas keeps drawing the **stale** data positioned for the new viewport. Markers slide horizontally smoothly. The new data arrives 100ms+ after the gesture stops; at that point the canvas re-renders with the right markers. Without this, the canvas would flicker empty during drags.

### 6.3 Zoom

Mouse wheel zooms in/out, centered on the cursor.

```typescript
function onWheel(e: WheelEvent) {
  e.preventDefault();
  
  const canvas = e.target as HTMLCanvasElement;
  const rect = canvas.getBoundingClientRect();
  const cursorX = e.clientX - rect.left;
  const cursorFraction = cursorX / rect.width;
  
  const fromMs = store.viewport.from.getTime();
  const toMs   = store.viewport.to.getTime();
  const spanMs = toMs - fromMs;
  const centerMs = fromMs + spanMs * cursorFraction;
  
  // Wheel-up zooms in (factor < 1); wheel-down zooms out.
  // Multiplier per wheel tick. deltaY > 0 means scroll down (zoom out).
  const factor = e.deltaY > 0 ? 1.25 : 0.8;
  store.zoomBy(factor, centerMs);
}
```

Two clamps: minimum span = 100ms (don't zoom below visible-marker granularity), maximum span = full session range.

### 6.4 Hover and Tooltip

On pointermove without drag, look up the marker under the pointer.

```typescript
const hoveredMarker = ref<{ eventId: string; x: number; y: number } | null>(null);

function handleHover(e: PointerEvent) {
  const canvas = e.target as HTMLCanvasElement;
  const rect = canvas.getBoundingClientRect();
  const x = e.clientX - rect.left;
  const y = e.clientY - rect.top;
  
  if (store.queryMode === 'list') {
    const hit = hitIndex.value?.findMarkerAt(x, y);
    hoveredMarker.value = hit ? { eventId: hit.eventId, x: e.clientX, y: e.clientY } : null;
  } else {
    const bucket = hitIndex.value?.findBucketAt(x, y);
    // Bucket tooltip shows "Node X, 1417 events at 14:23:15..14:23:20"
    hoveredBucket.value = bucket ?? null;
  }
}
```

The tooltip is a floating `<div>` positioned at the cursor; reads from the hovered marker. For raw-event hover, we want event-level detail; for bucket hover, we want count + time range. Showing the same level of detail in both modes is misleading.

**Tooltip latency**: hover events fire at ~60 Hz. We do not fetch event details on hover — too expensive. The tooltip shows what's already known from the loaded query result. Full payload comes on click.

### 6.5 Click → Selection → Inspector

```typescript
function onClick(e: PointerEvent) {
  const canvas = e.target as HTMLCanvasElement;
  const rect = canvas.getBoundingClientRect();
  const x = e.clientX - rect.left;
  const y = e.clientY - rect.top;
  
  if (store.queryMode === 'list') {
    const hit = hitIndex.value?.findMarkerAt(x, y);
    if (hit) {
      store.selectEvent(hit.eventId);
    } else {
      store.selectEvent(null);  // click outside marker clears selection
    }
  } else {
    // Click on a bucket: zoom into that bucket
    const bucket = hitIndex.value?.findBucketAt(x, y);
    if (bucket) {
      const bucketMs = parseBucketDurationMs(store.queryResult.bucketDuration);
      const start = new Date(bucket.bucketStartUtc).getTime();
      store.setViewport({ from: new Date(start), to: new Date(start + bucketMs) });
    }
  }
}
```

**Aggregate-mode click zooms into the bucket**: this is the drill-down gesture. Click a bar showing "1417 events in this 5-second bucket on blue-veh-01", and the timeline zooms to that 5-second window — likely at raw-event resolution, since 1417 events in 5 seconds fits the 5000-row budget.

The selected `eventId` lives in `timelineStore.selectedEventId`. `EventInspector.vue` watches it and fetches details:

```typescript
// inside EventInspector.vue setup
import { watch, ref } from 'vue';
import { useTimelineStore } from '@/stores/timelineStore';
import { useApi } from '@/api/useApi';
import type { EventDto } from '@/types/timeline';

const store = useTimelineStore();
const api = useApi();
const event = ref<EventDto | null>(null);
const loading = ref(false);

watch(() => store.selectedEventId, async (id) => {
  if (!id) { event.value = null; return; }
  loading.value = true;
  try {
    event.value = await api.getEvent(id);
  } finally {
    loading.value = false;
  }
});
```

The inspector renders the payload as pretty-printed JSON with syntax highlighting. Phase 5 uses a small inline highlighter; bringing in a full library (highlight.js, prism) is unnecessary at this scale.

Pivot buttons in the inspector:
- **"Filter to this trace"** → adds `traceId` to filter, re-fetches
- **"Show in scenario"** → navigates to `/scenario/{sessionId}` with the event time pinned
- **"Show causal tree"** → disabled in Phase 5 (Phase 6 will enable this)
- **"Show entity history"** → disabled (Phase 7)
- **"Copy event ID"** → copies `eventId` to clipboard

---

## 7. URL State and Sharing

Every meaningful timeline state is reflected in the URL so that sharing a URL transports a colleague to the exact same view.

### 7.1 URL Format

```
/v/timeline/{sessionId}?from=2026-05-19T14:00:00Z&to=2026-05-19T14:30:00Z&topic=weapons.fire&node=blue-veh-01&trace=B4C5D6E7F8A9B0C1&select=A3F2B4C8D9E0F1A2&follow=false
```

Components:
- `/v/timeline/{sessionId}` — base path
- `from`, `to` — ISO 8601 timestamps; the viewport
- `topic`, `node`, `entity`, `player`, `severity` — repeatable filter params
- `trace` — single trace ID filter
- `notable` — `true` for notables-only mode
- `select` — selected event ID
- `follow` — `true` for auto-follow live mode

### 7.2 useTimelineUrl

```typescript
// src/composables/useTimelineUrl.ts
import { watch } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { useTimelineStore } from '@/stores/timelineStore';

export function useTimelineUrl() {
  const route = useRoute();
  const router = useRouter();
  const store = useTimelineStore();
  
  // URL → Store (on route change)
  watch(() => route.query, (q) => {
    if (q.from) store.viewport.from = new Date(q.from as string);
    if (q.to)   store.viewport.to   = new Date(q.to as string);
    store.viewport.followLive = q.follow === 'true';
    
    store.filter = {
      topics: toArray(q.topic),
      nodes:  toArray(q.node),
      entities: toArray(q.entity),
      players: toArray(q.player),
      severities: toArray(q.severity),
      traceId: q.trace as string | undefined,
      notablesOnly: q.notable === 'true',
    };
    
    store.selectedEventId = (q.select as string) ?? null;
  }, { immediate: true });
  
  // Store → URL (on state change, debounced)
  const writeUrl = debounce(() => {
    const q: Record<string, string | string[]> = {
      from: store.viewport.from.toISOString(),
      to:   store.viewport.to.toISOString(),
    };
    if (store.viewport.followLive) q.follow = 'true';
    if (store.filter.topics?.length)   q.topic    = store.filter.topics;
    if (store.filter.nodes?.length)    q.node     = store.filter.nodes;
    if (store.filter.entities?.length) q.entity   = store.filter.entities;
    if (store.filter.players?.length)  q.player   = store.filter.players;
    if (store.filter.severities?.length) q.severity = store.filter.severities;
    if (store.filter.traceId)          q.trace    = store.filter.traceId;
    if (store.filter.notablesOnly)     q.notable  = 'true';
    if (store.selectedEventId)         q.select   = store.selectedEventId;
    
    router.replace({ query: q });
  }, 250);
  
  watch(
    () => [
      store.viewport.from.getTime(),
      store.viewport.to.getTime(),
      store.viewport.followLive,
      JSON.stringify(store.filter),
      store.selectedEventId,
    ],
    writeUrl
  );
}

function toArray(v: any): string[] | undefined {
  if (v === undefined) return undefined;
  return Array.isArray(v) ? v : [v];
}
```

**Bidirectional binding** with debouncing: URL changes update the store immediately; store changes update the URL after a brief settle to avoid history churn during pan/zoom.

`router.replace` (not `push`) so that pan/zoom doesn't pollute browser history. Browser back/forward jumps to discrete states the user explicitly navigated to (loading a session, clicking a pivot button), not every intermediate viewport.

### 7.3 Pivots Update the URL

When the user clicks "Filter to this trace" in the inspector, the action is:

```typescript
function filterToTrace() {
  if (!event.value) return;
  store.filter = { ...store.filter, traceId: event.value.traceId };
  // The URL updates via useTimelineUrl's watcher on filter changes
}
```

The URL now includes `?trace=...`. The user can copy the URL and paste it; opening it on another machine reproduces the filtered view.

---

## 8. Live Mode and Auto-Follow

### 8.1 Live Streaming Wiring

When the session is active (i.e., the active interval contains events after the viewport's `to`), the view subscribes to `/api/live/events` with the current filter. New events arrive incrementally.

```typescript
// src/composables/useTimelineLiveStream.ts
import { onMounted, onUnmounted, watch } from 'vue';
import { fetchEventSource } from '@microsoft/fetch-event-source';
import { useTimelineStore } from '@/stores/timelineStore';
import type { EventDto } from '@/types/timeline';

export function useTimelineLiveStream(sessionId: Ref<string | null>) {
  const store = useTimelineStore();
  let abortCtrl: AbortController | null = null;

  watch([sessionId, () => JSON.stringify(store.filter)], () => {
    abortCtrl?.abort();
    if (!sessionId.value) return;
    
    abortCtrl = new AbortController();
    const url = buildLiveUrl(sessionId.value, store.filter);
    
    fetchEventSource(url, {
      signal: abortCtrl.signal,
      openWhenHidden: true,
      onmessage(ev) {
        if (!ev.data) return;
        try {
          const dto = JSON.parse(ev.data) as EventDto;
          store.appendLiveEvent(dto);
        } catch (err) {
          console.error('SSE parse error:', err);
        }
      },
      onerror() {
        // Allow fetchEventSource's built-in reconnect
      }
    });
  }, { immediate: true });
  
  onUnmounted(() => abortCtrl?.abort());
}
```

### 8.2 Appending Live Events

In `timelineStore`:

```typescript
actions: {
  appendLiveEvent(ev: EventDto) {
    if (this.queryMode !== 'list') return;  // aggregate mode: live updates trigger re-query
    if (!this.queryResult) return;
    const list = this.queryResult as EventListDto;
    list.events.push(ev);
    list.totalMatching += 1;
    list.returned += 1;
    
    if (this.viewport.followLive) {
      // Slide the viewport to keep the new event visible
      const evTime = new Date(ev.publishWallclock).getTime();
      const toMs = this.viewport.to.getTime();
      if (evTime > toMs) {
        const span = toMs - this.viewport.from.getTime();
        this.viewport = {
          from: new Date(evTime - span + 5000),  // 5s headroom
          to:   new Date(evTime + 5000),
          followLive: true,
        };
      }
    }
  }
}
```

**Live updates in aggregate mode**: rather than insert into buckets (complex; bucket boundaries don't always align with arrival times), aggregate mode re-fetches periodically (every 5 seconds) when in live-edge view. This is cheap because aggregate queries return small results.

```typescript
// inside useTimelineQuery extension
let aggregateLiveTimer: ReturnType<typeof setInterval> | null = null;

watch(() => store.queryMode, (mode) => {
  if (aggregateLiveTimer) { clearInterval(aggregateLiveTimer); aggregateLiveTimer = null; }
  if (mode === 'aggregate' && store.viewport.followLive) {
    aggregateLiveTimer = setInterval(fetchDebounced, 5000);
  }
});
```

### 8.3 Auto-Follow UX

The Follow toggle in `TimelineToolbar`:

```vue
<button
  class="toolbar__follow"
  :class="{ 'toolbar__follow--active': store.viewport.followLive }"
  :disabled="!isLiveSession"
  @click="toggleFollow"
  title="Auto-follow the live edge as events arrive"
>
  {{ store.viewport.followLive ? 'Following live' : 'Follow live' }}
</button>
```

`isLiveSession` is computed: the session has no `endUtc`, or the viewport's `to` is within 30 seconds of "now".

**Pan disables follow**: any user-initiated viewport change clears `followLive`. This is the right pattern: if the user is investigating, they don't want the view yanked back to the present.

**Re-enabling follow** is one click; the new viewport snaps to the live edge with the previous span preserved.

### 8.4 Live SSE Endpoint

```csharp
namespace Tracer.WebApi.Endpoints;

public static class LiveEventStreamEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/live/events", HandleAsync);
    }

    public static async Task HandleAsync(
        HttpContext ctx,
        [FromQuery] string sessionId,
        [FromQuery] string[]? topic,
        [FromQuery] string[]? node,
        [FromQuery] string? traceId,
        [FromQuery] string[]? entityId,
        [FromQuery] string[]? playerId,
        [FromQuery] string[]? severity,
        [FromQuery] bool notablesOnly,
        [FromServices] SseConnectionManager mgr,
        [FromServices] ObserverConfig config,
        CancellationToken ct)
    {
        ctx.Response.Headers["Content-Type"] = "text/event-stream";
        ctx.Response.Headers["Cache-Control"] = "no-cache";
        ctx.Response.Headers["X-Accel-Buffering"] = "no";
        await ctx.Response.Body.FlushAsync(ct);
        
        var conn = new SseConnection(config.LiveStreaming.PerClientBufferSize)
        {
            Filter = new SseFilter
            {
                SessionId = sessionId,
                Topics    = topic?.ToHashSet(),
                Nodes     = node?.ToHashSet(),
                TraceId   = traceId,
                EntityIds = entityId?.ToHashSet(),
                PlayerIds = playerId?.ToHashSet(),
                Severities = severity?.ToHashSet(),
                NotablesOnly = notablesOnly,
            }
        };
        
        if (!await mgr.TryRegisterAsync(conn, ct))
        {
            ctx.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }
        
        // Same drain loop as Phase 3 /api/live/notables; only the filter differs
        // ... heartbeats, write loop, deregister-on-disconnect ...
    }
}
```

This endpoint shares all the plumbing from Phase 3 §5.3. The Phase 5 difference: the filter is richer, so more events match for engineer-facing streams than the notables-only stream.

---

## 9. Bundle Library

Phase 4 added `GET /api/bundles`; Phase 5 surfaces it in the frontend.

### 9.1 BundlesView.vue

A separate tab on the SessionBrowserView, or its own page at `/bundles`. Shows a list of available bundles with their labels, time ranges, and sizes. Clicking a bundle in **live mode** (the user is connected to an observer) opens a downloadable link or "Open in offline viewer" instruction. Clicking a bundle in **offline-viewer mode** is a no-op (the viewer already has one bundle open; switching is done via Open Bundle UI from Phase 4).

```vue
<!-- src/views/BundlesView.vue -->
<script setup lang="ts">
import { onMounted, ref } from 'vue';
import { useApi } from '@/api/useApi';
import { useBundleMode } from '@/composables/useBundleMode';
import type { BundleListEntryDto } from '@/api/tracerApiClient';

const api = useApi();
const { isLive } = useBundleMode();
const bundles = ref<BundleListEntryDto[]>([]);
const loading = ref(false);
const error = ref<string | null>(null);

async function load() {
  loading.value = true;
  error.value = null;
  try {
    const result = await api.listBundles();
    bundles.value = result.bundles;
  } catch (err: any) {
    error.value = err.message ?? 'Failed to list bundles';
  } finally {
    loading.value = false;
  }
}

function downloadUrl(b: BundleListEntryDto): string {
  return `/api/bundles/${encodeURIComponent(b.bundleId)}/download`;
}

onMounted(load);
</script>

<template>
  <div class="bundles">
    <h1>Bundles</h1>
    <p v-if="!isLive" class="bundles__hint">
      You're viewing a bundle. To open a different bundle, return to the Open Bundle screen.
    </p>
    <div v-else-if="bundles.length === 0" class="bundles__empty">
      No bundles built yet.
    </div>
    <ul v-else class="bundles__list">
      <li v-for="b in bundles" :key="b.bundleId" class="bundles__item">
        <div class="bundles__meta">
          <div class="bundles__label">{{ b.label ?? `Bundle ${b.bundleId.slice(0,8)}` }}</div>
          <div class="bundles__details">
            {{ formatRange(b.timeRange) }} · {{ formatBytes(b.sizeBytes) }}
          </div>
        </div>
        <a :href="downloadUrl(b)" class="bundles__download">Download</a>
      </li>
    </ul>
  </div>
</template>
```

### 9.2 "Build a Bundle" Action

From any session in the Session Browser, an action lets the user trigger a bundle build:

```vue
<!-- inside SessionCard.vue, additions -->
<button class="session-card__build" @click.stop="buildBundle">
  Build bundle
</button>

<script setup lang="ts">
import { useApi } from '@/api/useApi';
const api = useApi();
const buildState = ref<'idle' | 'queued' | 'inprogress' | 'completed' | 'failed'>('idle');
const buildBundleId = ref<string | null>(null);

async function buildBundle() {
  buildState.value = 'queued';
  const { bundleId } = await api.buildBundle({
    sessionId: props.session.sessionId,
    fastStateScope: 'None',  // Phase 5 default; UI for selection comes Phase 7
  });
  buildBundleId.value = bundleId;
  pollStatus();
}

async function pollStatus() {
  if (!buildBundleId.value) return;
  const interval = setInterval(async () => {
    const status = await api.getBundleStatus(buildBundleId.value!);
    buildState.value = status.state.toLowerCase() as any;
    if (status.state === 'Completed' || status.state === 'Failed') {
      clearInterval(interval);
    }
  }, 1500);
}
</script>
```

The card shows the build progress inline ("Building bundle… 42 of 73 intervals processed…") and a download link on completion.

---

## 10. Performance Tuning Plan

Performance is the central concern of Phase 5. Targets from §1.3 are aspirational defaults; this section is the plan to hit them.

### 10.1 Profiling Setup

Day 1: instrument the relevant code paths with timing measurements. .NET's `Stopwatch` for backend; `performance.now()` for frontend.

Log structured entries (Phase 2 §22 convention):

```csharp
using var _ = Activity.StartActivity("EventQueryService.ListAsync");
var sw = Stopwatch.StartNew();
// ... query work ...
_logger.LogInformation(
    "Query complete: {DurationMs}ms, {TotalMatching} matching, {Returned} returned",
    sw.ElapsedMilliseconds, result.TotalMatching, result.Returned);
```

Acceptance threshold: 95th percentile latency for each operation must hit its target. We log and chart; we don't ship until hits.

### 10.2 Common Hot Spots and Mitigations

| Hot spot | Symptom | Mitigation |
|---|---|---|
| Cold-start query on first viewport | First render takes 2s+ | Pre-warm the multi-interval reader pool on Observer startup; query a tiny placeholder query during initialization to JIT the SQL path |
| Per-query ATTACH overhead | Aggregate queries slow at high attachment counts | Pool size 8 with pre-attached connections; never re-attach per query |
| JSON serialization of 5000 events | Backend takes 100+ ms just to write the response | Use `System.Text.Json` source generators for `EventListDto`; avoid reflection in the hot path |
| Frontend canvas re-render on every event | Live mode causes 60fps redraws under load | Batch SSE arrivals into 60ms windows; render at most once per window |
| Hit-index rebuild on every render | Slow even for 5000 events | The hit-index IS the render output; no separate rebuild |
| DPI-aware canvas blur | Markers fuzzy on high-DPI screens | Multiply `canvas.width` by `devicePixelRatio`; use `ctx.setTransform` |
| Hover lag in dense renders | Tooltip latency | Throttle hover lookups to 30 Hz; uniform-grid index keeps lookups O(1) average |

### 10.3 Query Plan Verification

For each query type, verify DuckDB's plan uses the expected indexes:

```sql
EXPLAIN ANALYZE
SELECT * FROM iv_20260519T140000Z.events
WHERE publish_wallclock >= '2026-05-19T14:00:00Z'
  AND publish_wallclock <  '2026-05-19T14:05:00Z';
```

Expected plan: index range scan on `publish_wallclock`. If DuckDB chooses a full scan, investigate why (statistics, index definition).

Run this check in a test in `Tracer.Tests.Integration` so regressions are caught.

### 10.4 Aggregate-Query Budget

For aggregate queries:
- A 1-hour viewport at 5s buckets = 720 buckets × ~5 nodes = ~3,600 result rows
- A 4-hour viewport at 5m buckets = 48 buckets × ~5 nodes = ~240 result rows
- A full 8-hour session at 5m buckets = 96 × 5 = ~480 result rows

All small. The cost is in the GROUP BY scan over the underlying events. For a session with 100M events:
- Full session at 5m buckets: scans all 100M rows; expected < 1s with DuckDB on a reasonable machine
- This is the 100M-event session target from §1.3 success criteria

If this misses target, consider:
- A periodic background job that pre-computes aggregate rollups into a separate table
- Phase 5 doesn't ship with rollups; if profiling shows them necessary, add as a Phase 5.5 deliverable

### 10.5 Live SSE Throughput

Worst case from architecture §17: 1000 events/sec sustained, 5000 events/sec burst. With 10 concurrent SSE clients each subscribed with a filter that matches everything:

- 1000 events/sec × 10 clients = 10,000 broadcaster fanouts/sec
- Each fanout: filter evaluation (O(1) HashSet lookups), enqueue to bounded channel

Estimated cost: < 1 ms per event for 10 clients. Well within budget.

If filters become substantially more complex (e.g., regex matching), revisit.

---

## 11. Test Plan for Phase 5

### 11.1 Backend Unit Tests

**MultiInterval/IntervalSetTrackerTests.cs**
- `InitializeAsync` with no completed intervals: snapshot has only the active interval
- `InitializeAsync` with 5 completed but cap=3: snapshot has 3 newest + active
- `OnIntervalRotatedAsync`: previously-active is demoted to Completed; new active appears
- `OnIntervalEvictedAsync`: snapshot no longer contains the evicted interval
- `SetChanged` event fires after every meaningful change
- `SetChanged` event does NOT fire if the eviction targeted an interval not in the current set

**MultiInterval/LiveMultiIntervalReaderTests.cs**
- `InitializeAsync` builds N connections, each with the current set attached
- `AcquireAsync` returns a connection from the pool
- After `OnIntervalRotatedAsync` fires the SetChanged event, new connections have the new set
- Connections issued from the old pool dispose-rather-than-return after a refresh
- Concurrent acquire+rebuild doesn't crash or leak handles

**WebApi/EventQueryServiceTests.cs**
- `ListAsync` with empty filter returns events in time order
- Time-range pushdown: only events in [from, to) returned
- Topic filter: only matching topics
- Multi-topic filter: OR within the filter
- Multiple filter types compose with AND
- `TotalMatching` correct even when `Truncated`
- Filter on `trace_id` returns only that trace's events
- `OrderDescending` returns newest first
- Empty result set: returns empty list with `TotalMatching=0`

**WebApi/EventAggregationServiceTests.cs**
- 1-hour viewport at 5s buckets: returns expected bucket count
- Empty range: returns empty `buckets` list
- `groupBy=none`: each bucket has one group with `groupKey=null`
- `groupBy=node`: groups by `publisher_node`
- Filter applied before aggregation: only matching events counted
- Bucket totals = sum of group counts within bucket
- Invalid bucket duration: throws `ArgumentException`

**WebApi/EventEndpointsListTests.cs**
- `GET /api/events?sessionId=X` with no filter: 200 with event list
- `limit=0` or `limit=5001`: 400 ProblemDetails
- Unknown sessionId: 404 ProblemDetails
- Multiple `topic` query parameters: handled correctly

**WebApi/EventEndpointsAggregateTests.cs**
- Missing required `bucketDuration`: 400
- Invalid `bucketDuration`: 400
- Valid request: 200 with aggregate DTO

### 11.2 Backend Integration Tests

**LiveMultiIntervalQueryTests.cs**
- Start observer fixture, push events into 3 sequential intervals
- Query `/api/events` for full session range
- Assert: events from all 3 intervals appear in the result
- Assert: events are correctly ordered across interval boundaries

**LiveMultiIntervalRotationTests.cs**
- Push events into the active interval
- Rotate (simulated clock)
- Push more events into the new active interval
- Query for the full session
- Assert: events from both intervals returned

**LiveMultiIntervalEvictionTests.cs**
- Configure tracker with `CompletedIntervalsToInclude=1`
- Push events into 3 sequential intervals
- After the 4th interval, the 1st should be evicted from queryability
- Query the full session
- Assert: only events from intervals 2, 3, and the active interval are returned

**TimelineRoundTripTests.cs**
- Capture events live
- Build a bundle
- Open bundle in offline viewer
- Run identical timeline queries against both
- Assert: results are bitwise identical (modulo client-side server-time fields)

### 11.3 Frontend Unit Tests (Vitest)

```typescript
// tests/unit/timelineRenderer.spec.ts
import { render } from '@/rendering/timelineRenderer';
import { createMockContext } from './canvasMocks';

describe('timelineRenderer', () => {
  it('draws one marker per event in list mode', () => {
    const ctx = createMockContext();
    const result = render(ctx, {
      fromMs: 1000, toMs: 11000,
      widthPx: 1000, heightPx: 100,
      nodes: ['n1'], swimlaneHeightPx: 100,
      mode: 'list',
      list: { events: makeFakeEvents(5, 'n1'), totalMatching: 5, returned: 5, truncated: false },
      nodeColors: new Map([['n1', '#fff']]),
      severityColors: { info: '#5b9dff', warning: '#e8b048', error: '#e85c5c' }
    });
    expect(ctx.calls.filter(c => c.method === 'arc')).toHaveLength(5);
    expect(result.hitIndex.findMarkerAt(...)).not.toBeNull();
  });
  
  it('draws bars per (bucket, group) in aggregate mode', () => { ... });
  it('handles empty events list cleanly', () => { ... });
  it('skips events outside the viewport defensively', () => { ... });
});
```

**timelineLayout.spec.ts**
- `chooseBucketDuration` returns 'raw' for sub-1-minute spans
- Returns '5s' for the 5-30 minute range
- Returns '5m' for spans over 4 hours
- Boundary tests at each threshold

**timelineHitTest.spec.ts**
- Single marker added; find at exact coordinates returns it
- Find at coordinates inside the marker radius returns it
- Find outside any marker returns null
- 1000 markers: find takes < 1 ms
- Two markers in the same cell: closer one wins

**useTimelineQuery.spec.ts**
- Viewport change fires a query
- Rapid viewport changes (< 100 ms apart): only the last one fires
- Mode switch (list→aggregate) at zoom threshold
- Query error sets `store.error`
- AbortError doesn't surface as error

**useTimelineUrl.spec.ts**
- URL params apply to store on mount
- Store changes update URL (debounced)
- Multiple filter values URL-encoded as repeated params
- Pivots correctly add filters to URL

### 11.4 E2E Tests (Playwright)

```typescript
test('timeline navigation', async ({ page }) => {
  await page.goto('http://localhost:5300/v/timeline/test-session-id');
  
  // Wait for canvas to render
  await page.waitForSelector('.timeline-canvas');
  
  // Pan: drag horizontally
  const canvas = page.locator('.timeline-canvas');
  await canvas.dragTo(canvas, {
    sourcePosition: { x: 600, y: 300 },
    targetPosition: { x: 400, y: 300 },
  });
  // URL should have updated from/to
  await expect(page).toHaveURL(/from=.*to=/);
  
  // Click a marker (approximate position)
  await canvas.click({ position: { x: 500, y: 200 } });
  // Inspector should appear
  await expect(page.locator('.event-inspector')).toBeVisible();
});

test('filter to trace pivots', async ({ page }) => {
  await page.goto('http://localhost:5300/v/timeline/test-session-id');
  await page.waitForSelector('.timeline-canvas');
  await page.locator('.timeline-canvas').click({ position: { x: 500, y: 200 } });
  await page.locator('.event-inspector__pivot-trace').click();
  await expect(page).toHaveURL(/trace=/);
  await expect(page.locator('.filter-chip')).toContainText('trace:');
});
```

### 11.5 Performance Tests (gated on canary builds)

- Backend: 100M-event aggregate query completes in < 1 second (synthetic data)
- Backend: 1M-event list query with filters returns in < 300 ms
- Frontend: render() with 5000 markers completes in < 50 ms
- E2E: open session → see first markers within 2 seconds (cold cache)

These are run on a dedicated perf-test build, not on every PR. Regressions there block the next release.

---

## 12. Phase 5 Risks and Mitigations

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Canvas2D rendering hits performance ceiling at very high marker density | Medium | High | The 5000-marker budget should keep this safe. If it doesn't, consider OffscreenCanvas + Worker for the renderer (architectural addition, Phase 5.5). |
| DuckDB `time_bucket` performance on very large session queries | Low | Medium | Test against 100M-event synthetic data on day 1. If insufficient, fall back to manual `floor((publish_wallclock - $from) / $bucket_size) * $bucket_size` which sometimes plans better. |
| LiveMultiIntervalReader file-handle exhaustion | Low | Medium | Pool size × attached intervals = ~32 handles per Observer process under default config. Well under any OS limit. Document the math. |
| Retention races with in-flight queries | Medium | High | The 30-second delay before deletion (§3.3) covers normal cases. If queries truly take longer than 30s, the delete fails (file is open); retention retries on next pass. |
| URL state grows long with many filters | Low | Low | At 5 simultaneous filters with ~10 values each, the URL is well under any browser limit. Document the limit. |
| SSE reconnect after observer restart confuses live mode | Medium | Medium | On reconnect, the frontend re-fetches the initial event list. The "live edge" may briefly snap backward as the observer restarts its recovery. Document. |
| Engineer disagrees with the UX choices once they actually use it | High | Low | This is Phase 5's defining moment. Reserve week 4 for UX iteration. No design choice in this document is sacred under real-engineer feedback. |
| Cross-interval queries surface schema-version mismatches between intervals | Low | High | Phase 1 §4.3 specifies the schema is stable; Phase 5 verifies via integration test that mixed-vintage interval reads return correctly. If schema is migrated in the future, design a separate compatibility layer. |
| Frontend bundle size grows due to new components and rendering code | Low | Low | Track bundle size; if it exceeds 2 MB gzipped, split the timeline into a lazy-loaded route chunk. |
| Color accessibility (red/green nodes) on colorblind users | Medium | Low | Use shape + position differentiation, not color alone. Severities use distinct shapes; per-node colors are decorative. |

---

## 13. Definition of Done for Phase 5

### Build & Run

- [ ] All new backend assemblies build clean with `TreatWarningsAsErrors=true`
- [ ] Frontend builds with no TypeScript errors and no eslint warnings
- [ ] `tracer-observer.exe` runs with live multi-interval reader enabled
- [ ] `tracer-viewer.exe` (offline) opens a bundle and the Timeline View renders
- [ ] OpenAPI document updated; TypeScript client regenerates with new endpoints

### Live Multi-Interval

- [ ] Observer attaches the active interval plus N completed intervals on startup
- [ ] On rotation, the previously-active interval is added to the queryable set; new active appears
- [ ] On retention eviction, the evicted interval is removed from the queryable set
- [ ] Queries across multiple intervals return correct, ordered results
- [ ] In-flight queries during rotation continue to succeed against their issued connections
- [ ] Retention waits before deleting interval directories whose data may still be referenced

### Event Query API

- [ ] `GET /api/events` returns event list with correct schema
- [ ] `GET /api/events` respects all filter parameters
- [ ] `GET /api/events?limit=5000` returns full result; `truncated=false` when count ≤ limit
- [ ] `GET /api/events?limit=10` with > 10 matching: `truncated=true`, `totalMatching > returned`
- [ ] `GET /api/events/aggregate` returns buckets with correct durations and grouping
- [ ] `GET /api/events/aggregate` validates `bucketDuration` value
- [ ] `GET /api/live/events` streams events matching the SSE filter

### Frontend: Timeline View

- [ ] Timeline View renders for a session with multiple nodes (multi-node swimlane display)
- [ ] Pan via horizontal drag updates viewport; data re-fetches after debounce
- [ ] Zoom via wheel updates viewport; bucket size adjusts at thresholds
- [ ] Aggregate-to-list transition is smooth (no flicker on bucket-mode switch)
- [ ] Click on raw-event marker opens inspector with full payload
- [ ] Click on aggregate bucket zooms into that time window
- [ ] Hover shows tooltip without lag
- [ ] All visible markers are hit-testable

### Frontend: Filters

- [ ] All filter types from §4.2 are exposed in the FilterPanel UI
- [ ] Adding a filter immediately triggers a refetch
- [ ] Removing a filter removes its constraint
- [ ] Filters compose with AND (cross-filter) and OR (within-filter)
- [ ] Filter state persists in the URL

### Frontend: URL

- [ ] Loading a URL with `?from=&to=&topic=...` reproduces the exact view
- [ ] Pan/zoom updates URL after a debounce
- [ ] Filter changes update URL immediately
- [ ] Browser back/forward navigates between meaningful states (not every pan tick)

### Frontend: Live Mode

- [ ] When the session is active, SSE subscription delivers new events to the timeline
- [ ] Auto-follow keeps the live edge centered
- [ ] User pan disables auto-follow
- [ ] Follow toggle re-enables and snaps to live edge
- [ ] Live mode works with filters: only filtered events stream

### Frontend: Bundle Library

- [ ] BundlesView lists bundles from `GET /api/bundles`
- [ ] Download link works (streams via `/api/bundles/{id}/download`)
- [ ] "Build bundle" action on a session card triggers `POST /api/bundles/build` and shows progress

### Testing

- [ ] All Phase 1-4 tests pass
- [ ] Phase 5 backend unit tests pass (target: 40+ tests)
- [ ] Phase 5 integration tests pass: rotation, eviction, round-trip parity
- [ ] Phase 5 frontend unit tests pass: renderer, layout, hit-test, query orchestration, URL state
- [ ] At least one Playwright E2E test passes locally

### Performance

- [ ] Initial timeline render on 1M-event session: < 500 ms
- [ ] Pan/zoom interaction: < 100 ms response
- [ ] Filter apply: < 300 ms p95
- [ ] Event click → inspector populated: < 100 ms
- [ ] SSE event → marker visible: < 100 ms
- [ ] 100M-event session-overview aggregate: < 1 second

### Documentation

- [ ] `docs/timeline-view.md` explains the UX for engineers (how to pan, zoom, filter)
- [ ] `docs/api-events.md` documents the new query endpoints
- [ ] README updated with Phase 5 capabilities
- [ ] CHANGELOG entry

---

## 14. Handoff to Phase 6

What Phase 6 inherits from Phase 5:

- **`/api/events`** — Phase 6's causal tree view uses this endpoint with `traceId` filter to list events on a trace
- **`EventInspector`** — Phase 6 enables the "Show causal tree" pivot (disabled in Phase 5)
- **The shareable URL pattern** — Phase 6 adds `/v/causal/{eventId}` following the same conventions
- **`LiveMultiIntervalReader`** — Phase 6's parent/child resolution may need to span intervals if events on the same trace are split across rotations
- **Canvas rendering machinery** — Phase 6 reuses for tree rendering, though tree layout is structurally different from the swimlane timeline

What Phase 6 must address that Phase 5 deferred:

- **Parent/child event resolution at scale**: Phase 6's tree walking needs an indexed lookup of "find children of event X". Add an index on `parent_event_id` if profiling shows the need (Phase 1 schema permits it).
- **Tree layout algorithm**: layered DAG layout for events with multiple ancestors (convergent traces) is non-trivial. Phase 6 §X will design it.
- **Cross-view navigation**: clicking a node in the causal tree should pivot back to the timeline at that event's time. The selectionStore introduced in Phase 5 carries this state.

What's now possible after Phase 5:

The complete engineer workflow:
1. Engineer notices a problem during a live session (or after reviewing a bundle)
2. Opens TimelineView, narrows time range to the suspected window
3. Filters by node or topic to isolate the relevant chatter
4. Spots an anomalous event, clicks it
5. Inspector shows payload; engineer copies trace ID
6. "Filter to this trace" pivots; timeline shows all events on that trace
7. Engineer reads the trace across nodes, understands the causal chain
8. Shares the URL with a teammate, who sees the same view

This is the engineer's daily-driver capability. Phase 6 adds causal tree visualization; Phase 7 adds entity-history drill-down. But by Phase 5, Tracer is genuinely useful for diagnostic work — not a demo, a tool.
