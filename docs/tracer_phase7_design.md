# Tracer Phase 7 — Detailed Design
## Entity History View, Slow State Time Series, Fast State Drill-Down

*Companion to `tracer_architecture_v1.md` and `tracer_phase1_design.md` through `tracer_phase6_design.md`*
*Phase 7 of the build sequence (architecture §18)*
*C# / .NET 8 backend · Vue 3 / TypeScript frontend · May 2026*

*Phase 7 brings the entity-centric perspective. Where Phase 5's timeline shows "what happened on each node over time" and Phase 6's causal tree shows "what caused what across all nodes", Phase 7 asks the third fundamental question: "what happened to this specific entity over its lifetime?"*

*This is the phase that validates the separated-storage decision from architecture §4: events and slow state live in DuckDB on the hot path; fast state lives in Parquet, queried only on demand. Phase 7 is the first view to actually exercise both paths, and the first time fast-state Parquet files get queried by user-facing code.*

*Entity history also closes the cross-view navigation loop. From timeline, click an event → see causal tree (Phase 6). From causal tree, click a node → see entity history (Phase 7). From entity history, click an event → back to timeline. The three views weave together into a real diagnostic workflow.*

---

## 1. Phase 7 Scope and Goals

### 1.1 What Phase 7 Delivers

- **`EntityHistoryView.vue`** — the new view, entity-centric, time-series layout
- **Lifecycle ribbon** — spawn, ownership changes, destruction events as a single horizontal band
- **Slow-state time series** — one row per slow-state topic the entity emits; values plotted over time
- **Event strip** — events that touched the entity (`entity_id` field matches) as markers along the timeline
- **Fast-state drill-down panel** — on-demand chart for a fast-state topic + axis selection (e.g., position.x over time)
- **`/api/entities`** endpoint — list entities with optional filters (player, topic, time range)
- **`/api/entities/{entityId}/events`** — events touching this entity in a time range
- **`/api/entities/{entityId}/slow-state`** — slow-state samples for this entity, grouped by topic
- **`/api/entities/{entityId}/fast-state`** — fast-state samples (Parquet query) with topic + columns + time range filter
- **Per-entity Parquet reader on the backend** — query fast-state Parquet files on demand
- **Shareable URL** — `/v/entity/{entityId}?...`
- **Cross-view pivots** — "Show in timeline focused on this entity", "Show causal tree of this event"

### 1.2 What Phase 7 Does NOT Deliver

- **No replication latency view** (Phase 9)
- **No trigger evaluation log** (Phase 8)
- **No annotations or saved views** (Phase 8)
- **No SQL console** (Phase 10)
- **No multi-entity comparison view** — Phase 7 shows one entity at a time; "Compare with entity X" pivot is stubbed for Phase 10+
- **No DDS adapter** — still uses mock data
- **No live SSE on entity history** — entity history is fundamentally retrospective. Phase 7 always shows a snapshot up to a chosen time. Live update of an entity-history view isn't a workflow we've heard requested; skip.
- **No fast state in live mode for the engineer's primary workflow** — engineers typically drill into fast state during retrospective bundle analysis, not during a live run. The endpoint works in live mode; the UI works in live mode; we just don't optimize for it.

### 1.3 Success Criteria

1. **Open an entity history from timeline or causal tree**: clicking an event with non-null `entity_id` in any view opens `EntityHistoryView` for that entity. Loads in < 500 ms.
2. **Lifecycle visible**: spawn, ownership transitions, and destruction events are visually distinct on the lifecycle ribbon.
3. **Slow state plots**: each slow-state topic the entity touched gets its own time-series row. Numeric fields plot as lines; enum/string fields plot as stepped color bands.
4. **Event strip**: every event with this entity's `entity_id` shows as a marker positioned by time. Click → inspector.
5. **Fast state drill-down works**: user picks a topic (from the entity's known topics) and a numeric column from its payload schema; chart appears within 1 second for typical 30-min entity histories.
6. **Cross-view pivots**: from any event marker, jump to timeline focused on it; from a slow-state event, jump to the trace that caused it.
7. **Shareable URL**: `/v/entity/{entityId}?session={sessionId}&from=...&to=...&fastStateTopic=...&fastStateColumn=...` reproduces the view exactly.
8. **Entity discovery**: from the Session Browser, an "Entities" tab lists known entities for a session; user can pick directly.
9. **Performance**:
   - Entity-events query: < 200 ms for a 30-min entity history with ~5000 events
   - Slow-state query: < 100 ms (data is small)
   - Fast-state Parquet query: < 1 second for a 30-min window of one entity at typical sample rates
10. **All Phase 1-6 tests pass.**

### 1.4 Estimated Duration

Two to three calendar weeks for one developer. Distribution:
- Week 1: backend — entity discovery, entity-events query, slow-state query, fast-state Parquet reader
- Week 2: frontend — view layout, lifecycle ribbon, slow-state time series renderer, event strip
- Week 3: fast-state drill-down UI, cross-view pivots, URL state, performance pass

---

## 2. Project Layout Additions

Building on Phase 6:

```
tracer/
  src/
    Tracer.Core/                                  (unchanged)
    Tracer.Storage.DuckDB/                        (additions to schema for slow-state indexing)
      Schema/
        SchemaV1.cs                               EXTENDED — adds slow_state entity index
    Tracer.Storage.Parquet/                       NEW assembly
      Tracer.Storage.Parquet.csproj
      ParquetReader.cs                            on-demand reader for fast-state files
      ParquetSchemaInspector.cs                   reads columns/types from a Parquet file
      ParquetQueryBuilder.cs                      time + column projection SQL
    Tracer.WebApi/
      Endpoints/
        EntityEndpoints.cs                        NEW
      Queries/
        EntityDiscoveryService.cs                 NEW
        EntityEventsService.cs                    NEW
        EntitySlowStateService.cs                 NEW
        EntityFastStateService.cs                 NEW — queries Parquet
      Contracts/Dto/
        EntityListDto.cs
        EntitySummaryDto.cs
        EntityEventsDto.cs
        EntitySlowStateDto.cs
        EntityFastStateDto.cs
        FastStateTopicSchemaDto.cs
  tracer-viewer/
    src/
      views/
        EntityHistoryView.vue                     NEW
      components/
        EntityLifecycleRibbon.vue                 NEW — spawn/ownership/destruction band
        SlowStateChart.vue                        NEW — one slow-state topic per row
        EntityEventStrip.vue                      NEW — event markers on a timeline
        FastStateDrillDown.vue                    NEW — chart for a fast-state topic + column
        FastStateColumnPicker.vue                 NEW — picks topic + numeric column
        EntityPickerView.vue                      NEW — list and pick entity for a session
      composables/
        useEntityHistoryQuery.ts                  NEW — drives the multi-query fetch
        useEntityHistoryUrl.ts                    NEW
        useFastStateChart.ts                      NEW — on-demand fast-state plot
      rendering/
        slowStateChartRenderer.ts                 NEW
        eventStripRenderer.ts                     NEW
        fastStateChartRenderer.ts                 NEW
        timeSeriesAxis.ts                         NEW (shared helper for stacked time series)
      stores/
        entityHistoryStore.ts                     NEW
      types/
        entityHistory.ts                          NEW
  tests/
    Tracer.Tests.Unit/
      Parquet/
        ParquetReaderTests.cs
        ParquetSchemaInspectorTests.cs
      WebApi/
        EntityDiscoveryServiceTests.cs
        EntityEventsServiceTests.cs
        EntitySlowStateServiceTests.cs
        EntityFastStateServiceTests.cs
        EntityEndpointsTests.cs
    Tracer.Tests.Integration/
      EntityHistoryRoundTripTests.cs
      FastStateParquetRoundTripTests.cs
  tracer-viewer/tests/
    unit/
      slowStateChartRenderer.spec.ts
      eventStripRenderer.spec.ts
      fastStateChartRenderer.spec.ts
      useEntityHistoryQuery.spec.ts
    e2e/
      entity-history-view.spec.ts
```

### 2.1 Dependencies

The `Tracer.Storage.Parquet` assembly reuses DuckDB's Parquet reading (via `read_parquet()` SQL function); no new NuGet packages required. Phase 4 already added `Parquet.Net` for writing — Phase 7 keeps that capability available but uses DuckDB for reads (simpler, leverages query optimization).

---

## 3. Schema Extensions

### 3.1 Slow State entity_id Index

Phase 1's slow_state table has an `entity_id` column but no index on it. Phase 7 queries "all slow-state samples for entity X over time range T", which is exactly an entity_id + time-range lookup. Add the index:

```sql
CREATE INDEX IF NOT EXISTS idx_slow_state_entity_time
ON slow_state (entity_id, publish_wallclock)
WHERE entity_id IS NOT NULL;
```

Composite index on `(entity_id, publish_wallclock)` — DuckDB can use this for both equality on entity_id and range on publish_wallclock. The partial index excludes rows with no entity_id (e.g., global state samples).

In `Tracer.Storage.DuckDB.Schema.SchemaV1.CreateIndexes`, append to Phase 6's set:

```csharp
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
    
    -- Phase 7
    CREATE INDEX IF NOT EXISTS idx_slow_state_entity_time
        ON slow_state (entity_id, publish_wallclock) WHERE entity_id IS NOT NULL;
    """;
```

Same migration policy as Phase 6: new intervals/bundles get the index; pre-existing intervals run slow until evicted. Documented; not migrated.

### 3.2 Fast-State Parquet Files: No Schema Change

Fast-state Parquet files were defined in Phase 1 §4.4 and used by the Phase 2 writer and Phase 4 aggregator. Their schema is per-topic:

```
fast_state/{topic}/{entity}/samples.parquet:
  publish_wallclock  TIMESTAMP
  instance_key       VARCHAR        -- entity_id (redundant with directory, kept for filter pushdown)
  ... per-topic columns derived from the topic IDL ...
```

The columns beyond `publish_wallclock` and `instance_key` are topic-specific. Phase 7 queries these dynamically — the column set is part of the response (so the frontend can offer column-picker UI without prior knowledge).

No schema change here. Phase 7 introduces only the reader; the writer (Phase 2) is unchanged.

---

## 4. Backend: Entity Discovery and Queries

### 4.1 Entity Discovery

What entities exist for a session? Phase 7 needs to answer that without scanning the full event stream.

The query: `SELECT DISTINCT entity_id FROM events WHERE entity_id IS NOT NULL AND publish_wallclock BETWEEN session_start AND session_end`. The existing index on `entity_id` makes this efficient.

But "what's the entity's first-seen and last-seen?" requires aggregation:

```sql
SELECT
    entity_id,
    MIN(publish_wallclock) AS first_seen_utc,
    MAX(publish_wallclock) AS last_seen_utc,
    COUNT(*) AS event_count,
    ANY_VALUE(owning_player_id) AS sample_player_id,  -- any non-null player ID seen
    ARRAY_AGG(DISTINCT topic ORDER BY topic) AS topics
FROM events
WHERE entity_id IS NOT NULL
  AND publish_wallclock >= $sessionStart
  AND publish_wallclock <  $sessionEnd
GROUP BY entity_id
ORDER BY event_count DESC
LIMIT $limit;
```

Two design choices to highlight:

- **`ANY_VALUE(owning_player_id)` not `MIN`**: player attribution may change over an entity's lifetime (ownership transfer). For the discovery list we just want a representative, not a constraint. Phase 7's UI shows full ownership transitions in the lifecycle ribbon.
- **Topics list**: lets the UI offer a "filter by topic" in the entity picker without a separate roundtrip. Caps at the natural cardinality (entities typically touch 5-20 topics).

```csharp
namespace Tracer.WebApi.Queries;

public sealed class EntityDiscoveryService
{
    private readonly LiveMultiIntervalReader _reader;
    private readonly ILogger<EntityDiscoveryService> _logger;

    public EntityDiscoveryService(LiveMultiIntervalReader reader, ILogger<EntityDiscoveryService> logger)
    {
        _reader = reader;
        _logger = logger;
    }

    public async Task<IReadOnlyList<EntitySummary>> DiscoverAsync(
        string sessionId,
        WallclockTime sessionStart,
        WallclockTime sessionEnd,
        string? topicFilter,
        string? playerFilter,
        int limit,
        CancellationToken ct)
    {
        await using var conn = await _reader.AcquireAsync(ct);
        
        var whereClause = """
            WHERE entity_id IS NOT NULL
              AND publish_wallclock >= $sessionStart
              AND publish_wallclock <  $sessionEnd
            """;
        var unionSql = conn.BuildEventsUnionSql(whereClause: whereClause);
        
        // Additional filters in the outer query
        var extraFilters = new List<string>();
        if (topicFilter is not null) extraFilters.Add("topic = $topicFilter");
        if (playerFilter is not null) extraFilters.Add("owning_player_id = $playerFilter");
        var extraWhere = extraFilters.Count > 0 ? "WHERE " + string.Join(" AND ", extraFilters) : "";
        
        var sql = $"""
            WITH u AS ({unionSql}),
            filtered AS (SELECT * FROM u {extraWhere})
            SELECT
                entity_id,
                MIN(publish_wallclock) AS first_seen_utc,
                MAX(publish_wallclock) AS last_seen_utc,
                COUNT(*) AS event_count,
                ANY_VALUE(owning_player_id) AS sample_player_id,
                ARRAY_AGG(DISTINCT topic ORDER BY topic) AS topics
            FROM filtered
            GROUP BY entity_id
            ORDER BY event_count DESC
            LIMIT $limit;
            """;
        
        await using var cmd = conn.Connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new DuckDBParameter("sessionStart", sessionStart.ToDateTimeOffset()));
        cmd.Parameters.Add(new DuckDBParameter("sessionEnd",   sessionEnd.ToDateTimeOffset()));
        cmd.Parameters.Add(new DuckDBParameter("limit", limit));
        if (topicFilter is not null) cmd.Parameters.Add(new DuckDBParameter("topicFilter", topicFilter));
        if (playerFilter is not null) cmd.Parameters.Add(new DuckDBParameter("playerFilter", playerFilter));
        
        var entities = new List<EntitySummary>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            entities.Add(new EntitySummary
            {
                EntityId = reader.GetString(0),
                FirstSeenUtc = new DateTimeOffset(reader.GetDateTime(1), TimeSpan.Zero),
                LastSeenUtc = new DateTimeOffset(reader.GetDateTime(2), TimeSpan.Zero),
                EventCount = reader.GetInt64(3),
                SamplePlayerId = reader.IsDBNull(4) ? null : reader.GetString(4),
                Topics = ReadStringList(reader, 5),
            });
        }
        return entities;
    }

    private static IReadOnlyList<string> ReadStringList(DbDataReader reader, int columnOrdinal)
    {
        // DuckDB array reading specifics depend on DuckDB.NET version.
        // The pattern: reader.GetValue returns object[] or similar; cast and convert.
        var value = reader.GetValue(columnOrdinal);
        if (value is null or DBNull) return Array.Empty<string>();
        if (value is IEnumerable<object> objs) return objs.Cast<string>().ToList();
        // ... defensive handling ...
        return Array.Empty<string>();
    }
}

public sealed record EntitySummary
{
    public required string EntityId { get; init; }
    public required DateTimeOffset FirstSeenUtc { get; init; }
    public required DateTimeOffset LastSeenUtc { get; init; }
    public required long EventCount { get; init; }
    public string? SamplePlayerId { get; init; }
    public required IReadOnlyList<string> Topics { get; init; }
}
```

### 4.2 Entity Events Service

For a single entity over a time range, return all events with `entity_id = X`. Just a filtered event query:

```csharp
namespace Tracer.WebApi.Queries;

public sealed class EntityEventsService
{
    private readonly LiveMultiIntervalReader _reader;
    private readonly ILogger<EntityEventsService> _logger;

    public async Task<EntityEventsResult> GetEventsAsync(
        string entityId,
        WallclockTime from,
        WallclockTime to,
        int limit,
        CancellationToken ct)
    {
        await using var conn = await _reader.AcquireAsync(ct);
        
        var whereClause = """
            WHERE entity_id = $entityId
              AND publish_wallclock >= $from
              AND publish_wallclock <  $to
            """;
        var unionSql = conn.BuildEventsUnionSql(whereClause: whereClause);
        
        var sql = $"""
            WITH u AS ({unionSql})
            SELECT * FROM u
            ORDER BY publish_wallclock
            LIMIT $limit;
            """;
        
        await using var cmd = conn.Connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new DuckDBParameter("entityId", entityId));
        cmd.Parameters.Add(new DuckDBParameter("from", from.ToDateTimeOffset()));
        cmd.Parameters.Add(new DuckDBParameter("to",   to.ToDateTimeOffset()));
        cmd.Parameters.Add(new DuckDBParameter("limit", limit + 1));   // +1 for truncation detection
        
        var events = new List<EventRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            events.Add(EventRecordMapper.FromReader(reader));
        
        var truncated = events.Count > limit;
        if (truncated) events.RemoveAt(events.Count - 1);
        
        return new EntityEventsResult
        {
            EntityId = entityId,
            Events = events,
            Truncated = truncated
        };
    }
}

public sealed record EntityEventsResult
{
    public required string EntityId { get; init; }
    public required IReadOnlyList<EventRecord> Events { get; init; }
    public required bool Truncated { get; init; }
}
```

### 4.3 Entity Slow State Service

Slow state samples for an entity, grouped by topic. Each topic gets a list of (timestamp, payload) pairs.

```csharp
namespace Tracer.WebApi.Queries;

public sealed class EntitySlowStateService
{
    private readonly LiveMultiIntervalReader _reader;
    private readonly ILogger<EntitySlowStateService> _logger;

    public async Task<EntitySlowStateResult> GetAsync(
        string entityId,
        WallclockTime from,
        WallclockTime to,
        IReadOnlyList<string>? topicFilter,
        CancellationToken ct)
    {
        await using var conn = await _reader.AcquireAsync(ct);
        
        var whereClause = """
            WHERE entity_id = $entityId
              AND publish_wallclock >= $from
              AND publish_wallclock <  $to
            """;
        var unionSql = conn.BuildSlowStateUnionSql(whereClause: whereClause);
        
        var topicFilterClause = topicFilter is not null && topicFilter.Count > 0
            ? "WHERE topic IN (" + string.Join(",", topicFilter.Select((_, i) => $"$topic{i}")) + ")"
            : "";
        
        var sql = $"""
            WITH u AS ({unionSql}),
            filtered AS (SELECT * FROM u {topicFilterClause})
            SELECT
                topic,
                publish_wallclock,
                payload,
                trace_id
            FROM filtered
            ORDER BY topic, publish_wallclock;
            """;
        
        await using var cmd = conn.Connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new DuckDBParameter("entityId", entityId));
        cmd.Parameters.Add(new DuckDBParameter("from", from.ToDateTimeOffset()));
        cmd.Parameters.Add(new DuckDBParameter("to",   to.ToDateTimeOffset()));
        if (topicFilter is not null)
            for (int i = 0; i < topicFilter.Count; i++)
                cmd.Parameters.Add(new DuckDBParameter($"topic{i}", topicFilter[i]));
        
        var byTopic = new Dictionary<string, List<SlowStateSample>>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var topic = reader.GetString(0);
            if (!byTopic.TryGetValue(topic, out var list))
                byTopic[topic] = list = new();
            list.Add(new SlowStateSample(
                topic,
                new WallclockTime(reader.GetDateTime(1)),
                reader.GetString(2),  // payload JSON
                reader.IsDBNull(3) ? 0UL : (ulong)reader.GetInt64(3)));
        }
        
        return new EntitySlowStateResult
        {
            EntityId = entityId,
            ByTopic = byTopic.ToDictionary(kvp => kvp.Key, kvp => (IReadOnlyList<SlowStateSample>)kvp.Value)
        };
    }
}

public sealed record SlowStateSample(string Topic, WallclockTime PublishWallclock, string PayloadJson, ulong TraceId);

public sealed record EntitySlowStateResult
{
    public required string EntityId { get; init; }
    public required IReadOnlyDictionary<string, IReadOnlyList<SlowStateSample>> ByTopic { get; init; }
}
```

The `BuildSlowStateUnionSql` is the slow-state equivalent of Phase 5's `BuildEventsUnionSql` — same pattern, different table. It belongs alongside the existing builder in `MultiIntervalReader`:

```csharp
public string BuildSlowStateUnionSql(string whereClause = "", string orderByClause = "", int? limit = null)
{
    if (_attachments.Attachments.Count == 0)
        return "SELECT NULL WHERE FALSE";
    
    var sb = new StringBuilder();
    bool first = true;
    foreach (var alias in _attachments.Attachments.Keys)
    {
        if (!first) sb.AppendLine("UNION ALL");
        sb.AppendLine($"SELECT '{alias}' as __source_alias, * FROM {alias}.slow_state {whereClause}");
        first = false;
    }
    if (!string.IsNullOrEmpty(orderByClause)) sb.AppendLine(orderByClause);
    if (limit.HasValue) sb.AppendLine($"LIMIT {limit.Value}");
    return sb.ToString();
}
```

### 4.4 Fast State Parquet Reader

This is the new architectural piece. Fast state lives in Parquet files at `fast_state/{topic}/{entity}/samples.parquet` — either inside per-interval directories (live mode) or inside the bundle (offline). The reader uses DuckDB's `read_parquet()` function to query them.

```csharp
namespace Tracer.Storage.Parquet;

public sealed class ParquetReader
{
    private readonly ILogger<ParquetReader> _logger;

    public ParquetReader(ILogger<ParquetReader> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Inspect the columns and types of a Parquet file without reading data.
    /// Used to populate the column-picker UI.
    /// </summary>
    public async Task<ParquetSchema> InspectSchemaAsync(string parquetPath, CancellationToken ct)
    {
        await using var conn = new DuckDBConnection("Data Source=:memory:");
        await conn.OpenAsync(ct);
        
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DESCRIBE SELECT * FROM read_parquet('{EscapeSql(parquetPath)}')";
        
        var columns = new List<ParquetColumn>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var name = reader.GetString(0);
            var type = reader.GetString(1);
            columns.Add(new ParquetColumn(name, type, IsNumeric(type)));
        }
        
        return new ParquetSchema(parquetPath, columns);
    }
    
    /// <summary>
    /// Read a time range from a Parquet file, projecting specific columns.
    /// </summary>
    public async Task<ParquetTimeSeriesResult> ReadTimeSeriesAsync(
        string parquetPath,
        string entityId,
        IReadOnlyList<string> columns,
        WallclockTime from,
        WallclockTime to,
        int maxSamples,
        CancellationToken ct)
    {
        await using var conn = new DuckDBConnection("Data Source=:memory:");
        await conn.OpenAsync(ct);
        
        var safeColumns = columns.Select(SafeColumnIdentifier).ToList();
        var columnList = string.Join(", ", safeColumns);
        
        // Two queries: one for count, one for downsampled data.
        // For Phase 7 we use simple uniform downsampling: take every Nth sample
        // where N = totalSamples / maxSamples. Sophisticated approaches (LTTB, M4)
        // are deferred.
        
        // Count first
        long totalSamples;
        await using (var countCmd = conn.CreateCommand())
        {
            countCmd.CommandText = $"""
                SELECT COUNT(*)
                FROM read_parquet('{EscapeSql(parquetPath)}')
                WHERE instance_key = $entityId
                  AND publish_wallclock >= $from
                  AND publish_wallclock <  $to
                """;
            countCmd.Parameters.Add(new DuckDBParameter("entityId", entityId));
            countCmd.Parameters.Add(new DuckDBParameter("from", from.ToDateTimeOffset()));
            countCmd.Parameters.Add(new DuckDBParameter("to",   to.ToDateTimeOffset()));
            totalSamples = (long)(await countCmd.ExecuteScalarAsync(ct))!;
        }
        
        if (totalSamples == 0)
            return new ParquetTimeSeriesResult
            {
                Columns = columns,
                Samples = Array.Empty<ParquetSample>(),
                TotalSamples = 0,
                Downsampled = false
            };
        
        // Read with optional stride downsampling
        var downsampled = totalSamples > maxSamples;
        var stride = downsampled ? (totalSamples / maxSamples) : 1L;
        
        var dataSql = downsampled
            ? $"""
                WITH numbered AS (
                    SELECT
                        ROW_NUMBER() OVER (ORDER BY publish_wallclock) AS rn,
                        publish_wallclock,
                        {columnList}
                    FROM read_parquet('{EscapeSql(parquetPath)}')
                    WHERE instance_key = $entityId
                      AND publish_wallclock >= $from
                      AND publish_wallclock <  $to
                )
                SELECT publish_wallclock, {columnList}
                FROM numbered
                WHERE (rn - 1) % $stride = 0
                ORDER BY publish_wallclock;
                """
            : $"""
                SELECT publish_wallclock, {columnList}
                FROM read_parquet('{EscapeSql(parquetPath)}')
                WHERE instance_key = $entityId
                  AND publish_wallclock >= $from
                  AND publish_wallclock <  $to
                ORDER BY publish_wallclock;
                """;
        
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = dataSql;
        cmd.Parameters.Add(new DuckDBParameter("entityId", entityId));
        cmd.Parameters.Add(new DuckDBParameter("from", from.ToDateTimeOffset()));
        cmd.Parameters.Add(new DuckDBParameter("to",   to.ToDateTimeOffset()));
        if (downsampled) cmd.Parameters.Add(new DuckDBParameter("stride", stride));
        
        var samples = new List<ParquetSample>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var time = new WallclockTime(reader.GetDateTime(0));
            var values = new Dictionary<string, double?>();
            for (int i = 0; i < columns.Count; i++)
            {
                if (reader.IsDBNull(i + 1)) { values[columns[i]] = null; }
                else
                {
                    // Coerce all numeric DuckDB types to double for the response.
                    try { values[columns[i]] = Convert.ToDouble(reader.GetValue(i + 1)); }
                    catch { values[columns[i]] = null; }
                }
            }
            samples.Add(new ParquetSample(time, values));
        }
        
        return new ParquetTimeSeriesResult
        {
            Columns = columns,
            Samples = samples,
            TotalSamples = totalSamples,
            Downsampled = downsampled
        };
    }
    
    private static bool IsNumeric(string ducktype) =>
        ducktype switch
        {
            "TINYINT" or "SMALLINT" or "INTEGER" or "BIGINT" or "HUGEINT" or
            "UTINYINT" or "USMALLINT" or "UINTEGER" or "UBIGINT" or
            "FLOAT" or "DOUBLE" or "DECIMAL" => true,
            _ => false
        };
    
    private static string EscapeSql(string s) => s.Replace("'", "''");
    
    private static string SafeColumnIdentifier(string name)
    {
        // DuckDB column names: alphanumeric and underscore, or quoted with double quotes.
        // We accept the user-provided column name but wrap in quotes defensively.
        return $"\"{name.Replace("\"", "\"\"")}\"";
    }
}

public sealed record ParquetColumn(string Name, string DuckType, bool IsNumeric);
public sealed record ParquetSchema(string Path, IReadOnlyList<ParquetColumn> Columns);
public sealed record ParquetSample(WallclockTime PublishWallclock, IReadOnlyDictionary<string, double?> Values);
public sealed record ParquetTimeSeriesResult
{
    public required IReadOnlyList<string> Columns { get; init; }
    public required IReadOnlyList<ParquetSample> Samples { get; init; }
    public required long TotalSamples { get; init; }
    public required bool Downsampled { get; init; }
}
```

**Critical design choices**:

- **Per-call connection**: each Parquet query opens a fresh in-memory DuckDB connection. Rationale: Parquet reads aren't on the hot path (engineer interactions, < 1/sec), opening a connection takes ~10 ms, and not maintaining a pool here keeps the resource model simple.
- **Stride downsampling, not LTTB**: Phase 7 ships with the dumbest possible downsampling — every Nth sample. It loses spikes between samples but is fast, deterministic, and easy to reason about. Sophisticated algorithms (LTTB for visual fidelity, M4 for pixel-perfect) are deferred to Phase 10+ when entity drill-down becomes a daily workflow and the loss becomes noticeable.
- **Numeric coercion to double**: the response is JSON; all numeric DuckDB types collapse to JSON number. The frontend chart code works with `(timestamp, double)` pairs. String/categorical fast-state columns are not supported in Phase 7's chart — only numeric ones appear in the column picker.
- **`maxSamples` defaults to 5000**: same as Phase 5's row budget. Chart rendering performance is the constraint.

### 4.5 Locating Fast-State Files

In the **live Observer**, fast-state Parquet files are spread across interval directories: `{dataRoot}/intervals/{intervalTimestamp}/fast_state/{topic}/{entity}/samples.parquet`. Querying an entity over time means reading from multiple files.

DuckDB's `read_parquet` accepts globs and lists:

```sql
SELECT * FROM read_parquet(['file1.parquet', 'file2.parquet']);
SELECT * FROM read_parquet('{dataRoot}/intervals/*/fast_state/{topic}/{entity}/samples.parquet');
```

For Phase 7 we use the explicit-list form — it's predictable and lets us check existence before passing the path. A small helper:

```csharp
namespace Tracer.WebApi.Queries;

public sealed class FastStateFileLocator
{
    private readonly IntervalSetTracker _tracker;
    private readonly BundleOpenManager? _bundleOpenManager;

    public FastStateFileLocator(IntervalSetTracker tracker, BundleOpenManager? bundleOpenManager = null)
    {
        _tracker = tracker;
        _bundleOpenManager = bundleOpenManager;
    }

    public IReadOnlyList<string> LocateFiles(string topic, string entityId)
    {
        // Filename-safe encoding (same scheme as Phase 4 §3.1)
        var safeTopic = BundleNaming.SafeFileName(topic);
        var safeEntity = BundleNaming.SafeFileName(entityId);
        
        // Live Observer mode: search per-interval directories
        var snapshot = _tracker.CurrentSnapshot();
        var paths = new List<string>();
        foreach (var iv in snapshot.Intervals)
        {
            var candidate = Path.Combine(
                iv.Directory.RootPath,
                "fast_state", safeTopic, safeEntity, "samples.parquet");
            if (File.Exists(candidate))
                paths.Add(candidate);
        }
        
        // Offline-bundle mode (Phase 4 OfflineViewer):
        // BundleOpenManager has a single working directory with fast_state/...
        if (_bundleOpenManager?.Current is { } bundle)
        {
            var bundleCandidate = Path.Combine(
                bundle.WorkingDirectory,
                "fast_state", safeTopic, safeEntity, "samples.parquet");
            if (File.Exists(bundleCandidate))
                paths.Add(bundleCandidate);
        }
        
        return paths;
    }
}
```

**An important asymmetry**: in live mode, each interval has its own per-topic-per-entity Parquet file (the agent writes them per-interval). In bundle mode, the aggregator (Phase 4 §5.6) consolidates per-entity Parquets across intervals into one per-entity file in the bundle. So in live mode we may query 4+ files; in bundle mode we query 1. The reader handles both.

### 4.6 EntityFastStateService

```csharp
namespace Tracer.WebApi.Queries;

public sealed class EntityFastStateService
{
    private readonly ParquetReader _reader;
    private readonly FastStateFileLocator _locator;
    private readonly ILogger<EntityFastStateService> _logger;

    public EntityFastStateService(ParquetReader reader, FastStateFileLocator locator, ILogger<EntityFastStateService> logger)
    {
        _reader = reader;
        _locator = locator;
        _logger = logger;
    }

    /// <summary>Returns the column schema for a (topic, entity) without reading any data.</summary>
    public async Task<FastStateTopicSchema?> GetSchemaAsync(
        string entityId, string topic, CancellationToken ct)
    {
        var files = _locator.LocateFiles(topic, entityId);
        if (files.Count == 0) return null;
        
        // Inspect the first file; assume all files have the same schema (true for our writer).
        var schema = await _reader.InspectSchemaAsync(files[0], ct);
        return new FastStateTopicSchema
        {
            EntityId = entityId,
            Topic = topic,
            Columns = schema.Columns.Where(c => c.Name != "publish_wallclock" && c.Name != "instance_key").ToList()
        };
    }

    /// <summary>Returns the topics this entity has fast-state data for.</summary>
    public IReadOnlyList<string> GetAvailableTopics(string entityId)
    {
        // Walk the directory(ies) where fast-state is rooted, find {topic}/{entity-safe}/samples.parquet
        return _locator.GetAvailableTopicsForEntity(entityId);
    }

    public async Task<EntityFastStateResult> ReadAsync(
        string entityId, string topic, IReadOnlyList<string> columns,
        WallclockTime from, WallclockTime to, int maxSamples,
        CancellationToken ct)
    {
        var files = _locator.LocateFiles(topic, entityId);
        if (files.Count == 0)
            return new EntityFastStateResult
            {
                EntityId = entityId, Topic = topic, Columns = columns,
                Samples = Array.Empty<ParquetSample>(),
                TotalSamples = 0, Downsampled = false
            };
        
        // For multiple files (live mode with multiple intervals), pass all to DuckDB's read_parquet([...])
        // by composing a glob-equivalent list parameter.
        // For Phase 7 simplicity, we iterate per-file and merge. DuckDB can handle the array
        // directly but the count() + downsampling logic is per-file; merging happens after.
        
        // ... in practice: build a single read_parquet([f1, f2, ...]) call with stride-downsampling
        //                  applied to the combined logical view.
        
        // The implementation calls ParquetReader.ReadTimeSeriesAsync with the combined file list.
        // For clarity, ParquetReader exposes a variant that accepts a list of files; details omitted.
        
        var result = await _reader.ReadTimeSeriesAsync(
            files, entityId, columns, from, to, maxSamples, ct);
        
        return new EntityFastStateResult
        {
            EntityId = entityId,
            Topic = topic,
            Columns = result.Columns,
            Samples = result.Samples,
            TotalSamples = result.TotalSamples,
            Downsampled = result.Downsampled
        };
    }
}

public sealed record FastStateTopicSchema
{
    public required string EntityId { get; init; }
    public required string Topic { get; init; }
    public required IReadOnlyList<ParquetColumn> Columns { get; init; }
}

public sealed record EntityFastStateResult
{
    public required string EntityId { get; init; }
    public required string Topic { get; init; }
    public required IReadOnlyList<string> Columns { get; init; }
    public required IReadOnlyList<ParquetSample> Samples { get; init; }
    public required long TotalSamples { get; init; }
    public required bool Downsampled { get; init; }
}
```

The `ParquetReader.ReadTimeSeriesAsync` overload accepting multiple files is a small extension of §4.4: pass `read_parquet([list of paths])` to DuckDB instead of one path. The rest is identical.

---

## 5. Web API Endpoints

### 5.1 Endpoint Surface

```
GET  /api/entities                                          list entities for a session
GET  /api/entities/{entityId}/summary                       lifetime stats
GET  /api/entities/{entityId}/events                        events touching this entity
GET  /api/entities/{entityId}/slow-state                    slow state samples for this entity
GET  /api/entities/{entityId}/fast-state/topics             which fast-state topics exist
GET  /api/entities/{entityId}/fast-state/{topic}/schema     column schema of one topic
GET  /api/entities/{entityId}/fast-state/{topic}            time-series data
```

### 5.2 EntityEndpoints.cs

```csharp
namespace Tracer.WebApi.Endpoints;

public static class EntityEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/entities",                              HandleListAsync).WithOpenApi();
        app.MapGet("/api/entities/{entityId}/summary",           HandleSummaryAsync).WithOpenApi();
        app.MapGet("/api/entities/{entityId}/events",            HandleEventsAsync).WithOpenApi();
        app.MapGet("/api/entities/{entityId}/slow-state",        HandleSlowStateAsync).WithOpenApi();
        app.MapGet("/api/entities/{entityId}/fast-state/topics", HandleFastStateTopicsAsync).WithOpenApi();
        app.MapGet("/api/entities/{entityId}/fast-state/{topic}/schema", HandleFastStateSchemaAsync).WithOpenApi();
        app.MapGet("/api/entities/{entityId}/fast-state/{topic}", HandleFastStateAsync).WithOpenApi();
    }

    public static async Task<Results<Ok<EntityListDto>, ProblemHttpResult>> HandleListAsync(
        [FromQuery] string sessionId,
        [FromQuery] string? topic,
        [FromQuery] string? playerId,
        [FromQuery] int limit = 200,
        [FromServices] EntityDiscoveryService discovery = default!,
        [FromServices] SessionQueryService sessions = default!,
        CancellationToken ct = default)
    {
        var session = await sessions.GetAsync(sessionId, ct);
        if (session is null)
            return TypedResults.Problem(new ProblemDetails {
                Title = "Session not found", Status = 404 });
        
        var entities = await discovery.DiscoverAsync(
            sessionId,
            WallclockTime.FromDateTimeOffset(session.StartUtc),
            WallclockTime.FromDateTimeOffset(session.EndUtc ?? DateTimeOffset.UtcNow),
            topic, playerId,
            Math.Clamp(limit, 1, 5000),
            ct);
        
        return TypedResults.Ok(new EntityListDto
        {
            Entities = entities.Select(EntityDtoMapper.Map).ToList(),
            Count = entities.Count
        });
    }

    public static async Task<Results<Ok<EntitySummaryDto>, NotFound>> HandleSummaryAsync(
        string entityId,
        [FromQuery] string sessionId,
        [FromServices] EntityDiscoveryService discovery,
        [FromServices] SessionQueryService sessions,
        CancellationToken ct)
    {
        var session = await sessions.GetAsync(sessionId, ct);
        if (session is null) return TypedResults.NotFound();
        
        var entities = await discovery.DiscoverAsync(
            sessionId,
            WallclockTime.FromDateTimeOffset(session.StartUtc),
            WallclockTime.FromDateTimeOffset(session.EndUtc ?? DateTimeOffset.UtcNow),
            null, null, 5000, ct);
        var match = entities.FirstOrDefault(e => e.EntityId == entityId);
        return match is null ? TypedResults.NotFound() : TypedResults.Ok(EntityDtoMapper.Map(match));
    }

    public static async Task<Ok<EntityEventsDto>> HandleEventsAsync(
        string entityId,
        [FromQuery] DateTimeOffset from,
        [FromQuery] DateTimeOffset to,
        [FromQuery] int limit = 5000,
        [FromServices] EntityEventsService events = default!,
        CancellationToken ct = default)
    {
        var result = await events.GetEventsAsync(
            entityId,
            WallclockTime.FromDateTimeOffset(from),
            WallclockTime.FromDateTimeOffset(to),
            Math.Clamp(limit, 1, 5000), ct);
        return TypedResults.Ok(EntityEventsDtoMapper.Map(result));
    }

    public static async Task<Ok<EntitySlowStateDto>> HandleSlowStateAsync(
        string entityId,
        [FromQuery] DateTimeOffset from,
        [FromQuery] DateTimeOffset to,
        [FromQuery] string[]? topic,
        [FromServices] EntitySlowStateService slowState = default!,
        CancellationToken ct = default)
    {
        var result = await slowState.GetAsync(
            entityId,
            WallclockTime.FromDateTimeOffset(from),
            WallclockTime.FromDateTimeOffset(to),
            topic, ct);
        return TypedResults.Ok(EntitySlowStateDtoMapper.Map(result));
    }

    public static Task<Ok<IReadOnlyList<string>>> HandleFastStateTopicsAsync(
        string entityId,
        [FromServices] EntityFastStateService fastState)
    {
        var topics = fastState.GetAvailableTopics(entityId);
        return Task.FromResult(TypedResults.Ok(topics));
    }

    public static async Task<Results<Ok<FastStateTopicSchemaDto>, NotFound>> HandleFastStateSchemaAsync(
        string entityId,
        string topic,
        [FromServices] EntityFastStateService fastState,
        CancellationToken ct)
    {
        var schema = await fastState.GetSchemaAsync(entityId, topic, ct);
        return schema is null
            ? TypedResults.NotFound()
            : TypedResults.Ok(FastStateSchemaDtoMapper.Map(schema));
    }

    public static async Task<Results<Ok<EntityFastStateDto>, ProblemHttpResult>> HandleFastStateAsync(
        string entityId,
        string topic,
        [FromQuery] string[]? column,
        [FromQuery] DateTimeOffset from,
        [FromQuery] DateTimeOffset to,
        [FromQuery] int maxSamples = 5000,
        [FromServices] EntityFastStateService fastState = default!,
        CancellationToken ct = default)
    {
        if (column is null || column.Length == 0)
            return TypedResults.Problem(new ProblemDetails {
                Title = "Missing column", Detail = "At least one column required", Status = 400 });
        if (maxSamples < 10 || maxSamples > 10_000)
            return TypedResults.Problem(new ProblemDetails {
                Title = "maxSamples out of range", Detail = "10..10000", Status = 400 });
        
        var result = await fastState.ReadAsync(
            entityId, topic, column,
            WallclockTime.FromDateTimeOffset(from),
            WallclockTime.FromDateTimeOffset(to),
            maxSamples, ct);
        return TypedResults.Ok(EntityFastStateDtoMapper.Map(result));
    }
}
```

### 5.3 DTOs

```csharp
namespace Tracer.WebApi.Contracts.Dto;

public sealed record EntityListDto
{
    public required IReadOnlyList<EntitySummaryDto> Entities { get; init; }
    public required int Count { get; init; }
}

public sealed record EntitySummaryDto
{
    public required string EntityId { get; init; }
    public required DateTimeOffset FirstSeenUtc { get; init; }
    public required DateTimeOffset LastSeenUtc { get; init; }
    public required long EventCount { get; init; }
    public string? SamplePlayerId { get; init; }
    public required IReadOnlyList<string> Topics { get; init; }
}

public sealed record EntityEventsDto
{
    public required string EntityId { get; init; }
    public required IReadOnlyList<EventDto> Events { get; init; }
    public required bool Truncated { get; init; }
}

public sealed record EntitySlowStateDto
{
    public required string EntityId { get; init; }
    public required IReadOnlyDictionary<string, IReadOnlyList<SlowStateSampleDto>> ByTopic { get; init; }
}

public sealed record SlowStateSampleDto
{
    public required string Topic { get; init; }
    public required DateTimeOffset PublishWallclock { get; init; }
    public required string PayloadJson { get; init; }
    public string? TraceId { get; init; }                          // null if 0
}

public sealed record FastStateTopicSchemaDto
{
    public required string EntityId { get; init; }
    public required string Topic { get; init; }
    public required IReadOnlyList<FastStateColumnDto> Columns { get; init; }
}

public sealed record FastStateColumnDto
{
    public required string Name { get; init; }
    public required string DuckType { get; init; }
    public required bool IsNumeric { get; init; }
}

public sealed record EntityFastStateDto
{
    public required string EntityId { get; init; }
    public required string Topic { get; init; }
    public required IReadOnlyList<string> Columns { get; init; }
    public required IReadOnlyList<FastStateSampleDto> Samples { get; init; }
    public required long TotalSamples { get; init; }
    public required bool Downsampled { get; init; }
}

public sealed record FastStateSampleDto
{
    public required DateTimeOffset PublishWallclock { get; init; }
    public required IReadOnlyDictionary<string, double?> Values { get; init; }
}
```

### 5.4 Wiring

In `ObserverHostBuilder` and `OfflineViewerHostBuilder`:

```csharp
builder.Services.AddSingleton<ParquetReader>();
builder.Services.AddSingleton<FastStateFileLocator>();
builder.Services.AddSingleton<EntityDiscoveryService>();
builder.Services.AddSingleton<EntityEventsService>();
builder.Services.AddSingleton<EntitySlowStateService>();
builder.Services.AddSingleton<EntityFastStateService>();

// In ConfigureMiddleware:
EntityEndpoints.Map(app);
```

---

## 6. Frontend: View Layout

### 6.1 Overall Layout

```
+---------------------------------------------------------------+
| AppHeader                                                     |
+---------------------------------------------------------------+
| Entity Summary Strip: ID, lifespan, player, topics            |
+---------------------------------------------------------------+
|                                                               |
| Lifecycle Ribbon (spawn → ownership transitions → destroy)    |
|                                                               |
+---------------------------------------------------------------+
|                                                               |
| Slow State Time Series (one row per topic, stacked)           |
|                                                               |
+---------------------------------------------------------------+
|                                                               |
| Event Strip (markers along the same time axis)                |
|                                                               |
+---------------------------------------------------------------+
|                                                               |
| Fast State Drill-Down                                         |
|   ┌─Topic picker ──┐  ┌─Column picker ──┐                   |
|   │ position_state │  │ ☑ x ☐ y ☐ z      │                  |
|   └────────────────┘  └─────────────────┘                   |
|   ┌─────────────────────────────────────┐                   |
|   │     line chart with values          │                   |
|   └─────────────────────────────────────┘                   |
|                                                               |
+---------------------------------------------------------------+
```

All horizontal panels share the same time axis. Zooming or panning in one updates the others. The fast-state drill-down is collapsed by default — most entity exploration doesn't need it; engineers expand it on demand.

### 6.2 EntityHistoryView.vue

```vue
<!-- src/views/EntityHistoryView.vue -->
<script setup lang="ts">
import { computed, ref } from 'vue';
import { useEntityHistoryStore } from '@/stores/entityHistoryStore';
import { useEntityHistoryQuery } from '@/composables/useEntityHistoryQuery';
import { useEntityHistoryUrl } from '@/composables/useEntityHistoryUrl';
import EntitySummaryStrip from '@/components/EntitySummaryStrip.vue';
import EntityLifecycleRibbon from '@/components/EntityLifecycleRibbon.vue';
import SlowStateChart from '@/components/SlowStateChart.vue';
import EntityEventStrip from '@/components/EntityEventStrip.vue';
import FastStateDrillDown from '@/components/FastStateDrillDown.vue';
import LoadingSpinner from '@/components/LoadingSpinner.vue';
import ErrorMessage from '@/components/ErrorMessage.vue';

const store = useEntityHistoryStore();
useEntityHistoryUrl();
useEntityHistoryQuery();
</script>

<template>
  <div class="entity-history">
    <LoadingSpinner v-if="store.loading && !store.summary" />
    <ErrorMessage v-else-if="store.error" :message="store.error" @retry="store.retry" />
    <template v-else-if="store.summary">
      <EntitySummaryStrip :summary="store.summary" />
      <EntityLifecycleRibbon
        v-if="store.events"
        :events="store.events"
        :time-range="store.timeRange"
      />
      <SlowStateChart
        v-for="(samples, topic) in store.slowStateByTopic"
        :key="topic"
        :topic="topic"
        :samples="samples"
        :time-range="store.timeRange"
        @select-event="onSelectEvent"
      />
      <EntityEventStrip
        v-if="store.events"
        :events="store.events"
        :time-range="store.timeRange"
        :selected-event-id="store.selectedEventId"
        @select="store.selectEvent"
      />
      <FastStateDrillDown
        :entity-id="store.entityId"
        :time-range="store.timeRange"
        :available-topics="store.fastStateTopics"
      />
    </template>
  </div>
</template>

<style lang="scss">
.entity-history {
  max-width: 1600px;
  margin: 0 auto;
  padding: 1.5rem;
  display: flex;
  flex-direction: column;
  gap: 1rem;
}
</style>
```

### 6.3 entityHistoryStore

```typescript
// src/stores/entityHistoryStore.ts
import { defineStore } from 'pinia';
import type {
  EntitySummaryDto, EntityEventsDto, EntitySlowStateDto, EventDto
} from '@/types/entityHistory';

export const useEntityHistoryStore = defineStore('entityHistory', {
  state: () => ({
    entityId: null as string | null,
    sessionId: null as string | null,
    timeRange: { from: new Date(), to: new Date() },
    summary: null as EntitySummaryDto | null,
    events: null as EntityEventsDto | null,
    slowStateByTopic: {} as Record<string, SlowStateSampleDto[]>,
    fastStateTopics: [] as string[],
    selectedEventId: null as string | null,
    loading: false,
    error: null as string | null,
  }),
  actions: {
    setEntity(entityId: string, sessionId: string) {
      this.entityId = entityId;
      this.sessionId = sessionId;
      this.summary = null;
      this.events = null;
      this.slowStateByTopic = {};
      this.selectedEventId = null;
    },
    setTimeRange(from: Date, to: Date) {
      this.timeRange = { from, to };
    },
    setSummary(s: EntitySummaryDto) {
      this.summary = s;
      // Default time range to entity's lifespan
      if (!this.timeRange.from || !this.timeRange.to ||
          this.timeRange.from.getTime() === this.timeRange.to.getTime()) {
        this.timeRange = {
          from: new Date(s.firstSeenUtc),
          to: new Date(s.lastSeenUtc),
        };
      }
    },
    setEvents(e: EntityEventsDto) { this.events = e; },
    setSlowState(s: EntitySlowStateDto) {
      this.slowStateByTopic = { ...s.byTopic };
    },
    setFastStateTopics(topics: string[]) { this.fastStateTopics = topics; },
    selectEvent(eventId: string | null) { this.selectedEventId = eventId; },
    retry() {
      const e = this.entityId; const s = this.sessionId;
      this.entityId = null;
      this.entityId = e; this.sessionId = s;
    },
  },
});
```

### 6.4 useEntityHistoryQuery

The composable orchestrates four parallel fetches when the entity changes: summary, events, slow state, fast-state topics list. Each is independent; failures in one don't block the others.

```typescript
// src/composables/useEntityHistoryQuery.ts
import { watch } from 'vue';
import { useEntityHistoryStore } from '@/stores/entityHistoryStore';
import { useApi } from '@/api/useApi';

export function useEntityHistoryQuery() {
  const store = useEntityHistoryStore();
  const api = useApi();
  let abortCtrl: AbortController | null = null;

  watch(
    () => [store.entityId, store.sessionId],
    async ([entityId, sessionId]) => {
      if (!entityId || !sessionId) return;
      abortCtrl?.abort();
      abortCtrl = new AbortController();
      const signal = abortCtrl.signal;
      
      store.loading = true;
      store.error = null;
      
      try {
        const summary = await api.getEntitySummary(entityId, sessionId, { signal });
        store.setSummary(summary);
        
        const from = new Date(summary.firstSeenUtc);
        const to   = new Date(summary.lastSeenUtc);
        
        // Fetch in parallel
        const [events, slowState, fastStateTopics] = await Promise.all([
          api.getEntityEvents(entityId, from, to, { signal }),
          api.getEntitySlowState(entityId, from, to, undefined, { signal }),
          api.getEntityFastStateTopics(entityId, { signal }),
        ]);
        
        store.setEvents(events);
        store.setSlowState(slowState);
        store.setFastStateTopics(fastStateTopics);
      } catch (err: any) {
        if (err.name === 'AbortError') return;
        store.error = err.message ?? 'Failed to load entity history';
      } finally {
        store.loading = false;
      }
    },
    { immediate: true }
  );
}
```

**Sequential then parallel**: the summary must come first because the other queries need the time range it provides. Once summary lands, the three follow-up queries fire in parallel.

The summary's `firstSeenUtc..lastSeenUtc` is the default time range; the user can shrink or expand it via the URL or future zoom controls.

---

## 7. Lifecycle Ribbon

A single horizontal band at the top showing the entity's lifecycle: spawn (creation), ownership transitions, destruction. Distinct visual encoding from the regular event strip so the engineer can see "what shaped this entity" at a glance.

### 7.1 Identifying Lifecycle Events

Lifecycle events are domain-specific (the simulation defines them). Phase 7 looks at conventional topic names:

| Topic pattern | Event meaning |
|---|---|
| `*.spawn`, `*.created`, `*.spawned` | Spawn |
| `*.ownership_changed`, `*.owner_transferred` | Ownership transition |
| `*.destroyed`, `*.killed`, `*.removed` | Destruction |

The frontend filters the events list by topic to extract lifecycle events. The filter is a hardcoded set of suffixes; future Phase 8 work may make it configurable per integration.

```typescript
// src/rendering/lifecycleClassifier.ts

export type LifecycleKind = 'spawn' | 'ownership' | 'destruction' | null;

const SPAWN_SUFFIXES       = ['spawn', 'created', 'spawned'];
const OWNERSHIP_SUFFIXES   = ['ownership_changed', 'owner_transferred', 'owner_changed'];
const DESTRUCTION_SUFFIXES = ['destroyed', 'killed', 'removed', 'despawned'];

export function classifyLifecycleEvent(topic: string): LifecycleKind {
  const tail = topic.split('.').pop() ?? '';
  if (SPAWN_SUFFIXES.includes(tail)) return 'spawn';
  if (OWNERSHIP_SUFFIXES.includes(tail)) return 'ownership';
  if (DESTRUCTION_SUFFIXES.includes(tail)) return 'destruction';
  return null;
}
```

### 7.2 EntityLifecycleRibbon.vue

```vue
<!-- src/components/EntityLifecycleRibbon.vue -->
<script setup lang="ts">
import { computed } from 'vue';
import type { EntityEventsDto, EventDto } from '@/types/entityHistory';
import { classifyLifecycleEvent, type LifecycleKind } from '@/rendering/lifecycleClassifier';
import { formatTime } from '@/utils/time';

interface LifecycleEvent {
  kind: LifecycleKind;
  event: EventDto;
  xPct: number;  // 0..100 horizontal position
}

const props = defineProps<{
  events: EntityEventsDto;
  timeRange: { from: Date; to: Date };
}>();

const lifecycleEvents = computed<LifecycleEvent[]>(() => {
  const from = props.timeRange.from.getTime();
  const to   = props.timeRange.to.getTime();
  const span = to - from;
  if (span <= 0) return [];
  
  return props.events.events
    .map(ev => {
      const kind = classifyLifecycleEvent(ev.topic);
      if (!kind) return null;
      const t = new Date(ev.publishWallclock).getTime();
      const xPct = ((t - from) / span) * 100;
      return { kind, event: ev, xPct };
    })
    .filter((x): x is LifecycleEvent => x !== null);
});

const ownershipPeriods = computed(() => {
  // For each ownership transition, show a band from this transition's time to the next.
  // First period: from spawn (or from start) to first ownership change.
  const transitions = lifecycleEvents.value
    .filter(e => e.kind === 'ownership' || e.kind === 'spawn')
    .sort((a, b) => new Date(a.event.publishWallclock).getTime() - new Date(b.event.publishWallclock).getTime());
  
  const periods: Array<{ fromPct: number; toPct: number; ownerLabel: string }> = [];
  let lastT = 0;
  for (const t of transitions) {
    const owner = extractOwnerFromPayload(t.event);
    if (periods.length > 0) {
      periods[periods.length - 1].toPct = t.xPct;
    }
    periods.push({ fromPct: t.xPct, toPct: 100, ownerLabel: owner });
    lastT = t.xPct;
  }
  return periods;
});

function extractOwnerFromPayload(ev: EventDto): string {
  try {
    const payload = JSON.parse(ev.payloadJson);
    return payload.newOwnerId ?? payload.ownerId ?? ev.owningPlayerId ?? 'unknown';
  } catch {
    return ev.owningPlayerId ?? 'unknown';
  }
}
</script>

<template>
  <section class="lifecycle-ribbon">
    <div class="lifecycle-ribbon__header">
      <h3>Lifecycle</h3>
    </div>
    <div class="lifecycle-ribbon__track">
      <!-- Ownership-period bands -->
      <div
        v-for="(p, i) in ownershipPeriods"
        :key="i"
        class="lifecycle-ribbon__ownership-band"
        :style="{ left: `${p.fromPct}%`, width: `${Math.max(p.toPct - p.fromPct, 1)}%` }"
        :title="`Owner: ${p.ownerLabel}`"
      >
        <span class="lifecycle-ribbon__ownership-label">{{ p.ownerLabel }}</span>
      </div>
      
      <!-- Lifecycle event markers -->
      <div
        v-for="(le, i) in lifecycleEvents"
        :key="i"
        class="lifecycle-ribbon__marker"
        :class="`lifecycle-ribbon__marker--${le.kind}`"
        :style="{ left: `${le.xPct}%` }"
        :title="`${le.kind} at ${formatTime(le.event.publishWallclock)}`"
      />
    </div>
  </section>
</template>

<style lang="scss">
.lifecycle-ribbon {
  background: var(--c-bg-surface);
  border-radius: 8px;
  padding: 0.75rem;
  
  &__header {
    margin-bottom: 0.5rem;
    h3 { margin: 0; font-size: 0.875rem; color: var(--c-text-muted); text-transform: uppercase; letter-spacing: 0.05em; }
  }
  
  &__track {
    position: relative;
    height: 40px;
    background: var(--c-bg-subtle);
    border-radius: 4px;
  }
  
  &__ownership-band {
    position: absolute;
    top: 0; height: 100%;
    background: rgba(91, 157, 255, 0.15);
    border-left: 2px solid var(--c-accent);
    display: flex; align-items: center; padding-left: 0.5rem;
    overflow: hidden; pointer-events: auto;
  }
  
  &__ownership-label {
    font-size: 0.75rem;
    font-family: var(--font-mono);
    color: var(--c-text);
    white-space: nowrap;
    text-overflow: ellipsis;
    overflow: hidden;
  }
  
  &__marker {
    position: absolute;
    top: 50%; transform: translate(-50%, -50%);
    width: 12px; height: 12px;
    border-radius: 50%;
    z-index: 1;
    
    &--spawn       { background: var(--c-success); }
    &--ownership   { background: var(--c-accent); }
    &--destruction { background: var(--c-danger); }
  }
}
</style>
```

**Why a ribbon, not a separate view**: lifecycle is context. The engineer wants to see "when did ownership change" at a glance while looking at events on the entity. Putting it as a distinct panel above the data integrates the temporal context without crowding the main view.

**Pure-CSS rendering (no canvas)**: lifecycle events are sparse (typically < 10 per entity). DOM-positioned absolute elements are the right tool. Canvas is reserved for high-density data.

---

## 8. Slow State Chart

One row per slow-state topic the entity has touched. Each row is a thin time-series strip.

### 8.1 Visual Design

```
Topic: vehicle_health
  |  ___________
  | |    100    |__________________________
  | |__________| |    87.5                 |____
  |             |________________________| |  60
  |                                         |__________
  +---------------------------------------------------------> time

Topic: vehicle_phase
  |
  | [idle      ][approach     ][engage    ][withdraw  ]
  |
  +---------------------------------------------------------> time
```

- **Numeric fields**: line chart with one line per (topic, field). Stepped/last-value-held between samples to reflect "this is the state until the next sample changes it".
- **Enum / string fields**: color-band chart showing categorical state over time, like a Gantt chart.
- **Categorical detection**: at chart-render time, examine the values; if all values are strings or all numeric values are in a small set (< 10 distinct), treat as categorical.

### 8.2 The Renderer

```typescript
// src/rendering/slowStateChartRenderer.ts

import type { SlowStateSampleDto } from '@/types/entityHistory';

export interface SlowStateChartInput {
  topic: string;
  samples: SlowStateSampleDto[];
  timeRange: { from: Date; to: Date };
  widthPx: number;
  heightPx: number;
  field: string;                // payload field to plot
  fieldType: 'numeric' | 'categorical';
}

export interface SlowStateHitEntry {
  x: number; w: number;
  sample: SlowStateSampleDto;
}

export interface SlowStateChartResult {
  hits: SlowStateHitEntry[];
}

export function renderSlowStateChart(
  ctx: CanvasRenderingContext2D,
  input: SlowStateChartInput
): SlowStateChartResult {
  ctx.clearRect(0, 0, input.widthPx, input.heightPx);
  
  if (input.fieldType === 'numeric') {
    return renderNumericLine(ctx, input);
  } else {
    return renderCategoricalBands(ctx, input);
  }
}

function renderNumericLine(ctx: CanvasRenderingContext2D, input: SlowStateChartInput): SlowStateChartResult {
  const from = input.timeRange.from.getTime();
  const to   = input.timeRange.to.getTime();
  const xScale = input.widthPx / (to - from);
  
  // Extract values
  const points = input.samples
    .map(s => {
      const t = new Date(s.publishWallclock).getTime();
      let v: number | null = null;
      try {
        const payload = JSON.parse(s.payloadJson);
        const raw = payload[input.field];
        if (typeof raw === 'number') v = raw;
        else if (typeof raw === 'boolean') v = raw ? 1 : 0;
      } catch {}
      return { t, v, sample: s };
    })
    .filter(p => p.v !== null) as Array<{ t: number; v: number; sample: SlowStateSampleDto }>;
  
  if (points.length === 0) return { hits: [] };
  
  // Compute y-range
  const vs = points.map(p => p.v);
  const minV = Math.min(...vs);
  const maxV = Math.max(...vs);
  const range = Math.max(maxV - minV, 1e-9);
  const padding = 4;
  const yScale = (input.heightPx - padding * 2) / range;
  
  function yPx(v: number): number {
    return input.heightPx - padding - (v - minV) * yScale;
  }
  function xPx(t: number): number {
    return (t - from) * xScale;
  }
  
  // Stepped line (last-value-held)
  ctx.strokeStyle = 'var(--c-accent, #5b9dff)';
  ctx.lineWidth = 1.5;
  ctx.beginPath();
  for (let i = 0; i < points.length; i++) {
    const x = xPx(points[i].t);
    const y = yPx(points[i].v);
    if (i === 0) ctx.moveTo(x, y);
    else {
      ctx.lineTo(x, yPx(points[i - 1].v));  // hold previous value
      ctx.lineTo(x, y);
    }
  }
  // Extend final value to the right edge
  if (points.length > 0) {
    const last = points[points.length - 1];
    ctx.lineTo(input.widthPx, yPx(last.v));
  }
  ctx.stroke();
  
  // Hit entries (sample points only)
  const hits: SlowStateHitEntry[] = points.map((p, i) => {
    const nextT = i < points.length - 1 ? points[i + 1].t : to;
    return {
      x: xPx(p.t),
      w: xPx(nextT) - xPx(p.t),
      sample: p.sample,
    };
  });
  
  return { hits };
}

function renderCategoricalBands(ctx: CanvasRenderingContext2D, input: SlowStateChartInput): SlowStateChartResult {
  const from = input.timeRange.from.getTime();
  const to   = input.timeRange.to.getTime();
  const xScale = input.widthPx / (to - from);
  
  // Extract values; preserve ordering
  const points = input.samples
    .map(s => {
      const t = new Date(s.publishWallclock).getTime();
      let v: string | null = null;
      try {
        const payload = JSON.parse(s.payloadJson);
        const raw = payload[input.field];
        v = raw === null || raw === undefined ? null : String(raw);
      } catch {}
      return { t, v, sample: s };
    })
    .filter(p => p.v !== null) as Array<{ t: number; v: string; sample: SlowStateSampleDto }>;
  
  // Distinct values → color assignment
  const distinct = Array.from(new Set(points.map(p => p.v)));
  const colors = ['#5b9dff', '#4ec97a', '#e8b048', '#a673e8', '#5cdce8', '#e85c9d', '#a8c950'];
  const colorOf = new Map(distinct.map((v, i) => [v, colors[i % colors.length]]));
  
  const hits: SlowStateHitEntry[] = [];
  for (let i = 0; i < points.length; i++) {
    const t = points[i].t;
    const nextT = i < points.length - 1 ? points[i + 1].t : to;
    const x = (t - from) * xScale;
    const w = (nextT - t) * xScale;
    
    ctx.fillStyle = colorOf.get(points[i].v) ?? '#888';
    ctx.fillRect(x, 0, w, input.heightPx);
    
    // Label inside band if there's room
    ctx.fillStyle = 'rgba(255,255,255,0.95)';
    ctx.font = '11px var(--font-mono, monospace)';
    ctx.textBaseline = 'middle';
    const label = points[i].v;
    const metrics = ctx.measureText(label);
    if (metrics.width < w - 6) {
      ctx.fillText(label, x + 3, input.heightPx / 2);
    }
    
    hits.push({ x, w, sample: points[i].sample });
  }
  
  return { hits };
}
```

### 8.3 Field Selection

A slow-state event has a payload (JSON). Which field do we plot? Phase 7 chooses the first plottable field automatically:
- For numeric fields: prefer one named `value`, `state`, `level`, `health`, or the first numeric field
- For string fields: prefer one named `state`, `status`, `phase`, or the first string field

The user can click a field-picker dropdown on each chart row to switch. The picker shows all fields in the topic's payload along with the value distribution (e.g., "value: numeric, 100..0.5", "state: 4 distinct values").

```vue
<!-- src/components/SlowStateChart.vue -->
<script setup lang="ts">
import { ref, computed, watch, onMounted } from 'vue';
import type { SlowStateSampleDto } from '@/types/entityHistory';
import { renderSlowStateChart, type SlowStateHitEntry } from '@/rendering/slowStateChartRenderer';
import { useResizeObserver } from '@/composables/useResizeObserver';

const props = defineProps<{
  topic: string;
  samples: SlowStateSampleDto[];
  timeRange: { from: Date; to: Date };
}>();

const emit = defineEmits<{ selectEvent: [sample: SlowStateSampleDto] }>();

const canvasRef = ref<HTMLCanvasElement | null>(null);
const fieldChoices = computed(() => detectFields(props.samples));
const selectedField = ref<string>(fieldChoices.value[0]?.name ?? '');
const fieldType = computed(() => fieldChoices.value.find(f => f.name === selectedField.value)?.type ?? 'numeric');
let hits: SlowStateHitEntry[] = [];

function draw() {
  const canvas = canvasRef.value;
  if (!canvas) return;
  const ctx = canvas.getContext('2d');
  if (!ctx) return;
  
  const dpr = window.devicePixelRatio || 1;
  const cssW = canvas.clientWidth;
  const cssH = canvas.clientHeight;
  if (canvas.width !== cssW * dpr) canvas.width = cssW * dpr;
  if (canvas.height !== cssH * dpr) canvas.height = cssH * dpr;
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
  
  const result = renderSlowStateChart(ctx, {
    topic: props.topic,
    samples: props.samples,
    timeRange: props.timeRange,
    widthPx: cssW,
    heightPx: cssH,
    field: selectedField.value,
    fieldType: fieldType.value,
  });
  hits = result.hits;
}

watch([() => props.samples, () => props.timeRange, selectedField], draw);
useResizeObserver(canvasRef, draw);
onMounted(draw);

function onClick(e: PointerEvent) {
  const canvas = canvasRef.value!;
  const rect = canvas.getBoundingClientRect();
  const x = e.clientX - rect.left;
  for (const h of hits) {
    if (x >= h.x && x <= h.x + h.w) {
      emit('selectEvent', h.sample);
      return;
    }
  }
}

interface FieldChoice { name: string; type: 'numeric' | 'categorical'; }

function detectFields(samples: SlowStateSampleDto[]): FieldChoice[] {
  if (samples.length === 0) return [];
  // Inspect first ~20 samples for fields and types
  const fieldTypes = new Map<string, Set<string>>();
  for (const s of samples.slice(0, 20)) {
    try {
      const payload = JSON.parse(s.payloadJson);
      for (const [k, v] of Object.entries(payload)) {
        if (!fieldTypes.has(k)) fieldTypes.set(k, new Set());
        fieldTypes.get(k)!.add(typeof v);
      }
    } catch {}
  }
  
  const choices: FieldChoice[] = [];
  for (const [name, types] of fieldTypes) {
    if (types.has('number')) choices.push({ name, type: 'numeric' });
    else if (types.has('string') || types.has('boolean')) choices.push({ name, type: 'categorical' });
  }
  // Preferred field ordering
  const PREF_NUMERIC = ['value', 'level', 'health', 'count'];
  const PREF_CATEGORICAL = ['state', 'status', 'phase', 'kind'];
  choices.sort((a, b) => {
    const pa = a.type === 'numeric' ? PREF_NUMERIC.indexOf(a.name) : PREF_CATEGORICAL.indexOf(a.name);
    const pb = b.type === 'numeric' ? PREF_NUMERIC.indexOf(b.name) : PREF_CATEGORICAL.indexOf(b.name);
    if (pa !== -1 && pb === -1) return -1;
    if (pa === -1 && pb !== -1) return 1;
    if (pa !== -1 && pb !== -1) return pa - pb;
    return a.name.localeCompare(b.name);
  });
  return choices;
}
</script>

<template>
  <section class="slow-state-chart">
    <header class="slow-state-chart__header">
      <h4 class="slow-state-chart__topic">{{ topic }}</h4>
      <select v-model="selectedField" class="slow-state-chart__field">
        <option v-for="f in fieldChoices" :key="f.name" :value="f.name">
          {{ f.name }} ({{ f.type }})
        </option>
      </select>
    </header>
    <canvas
      ref="canvasRef"
      class="slow-state-chart__canvas"
      @click="onClick"
    />
  </section>
</template>

<style lang="scss">
.slow-state-chart {
  background: var(--c-bg-surface);
  border-radius: 8px;
  padding: 0.75rem;
  
  &__header {
    display: flex; align-items: center; gap: 0.75rem;
    margin-bottom: 0.5rem;
  }
  
  &__topic {
    margin: 0; flex: 1;
    font-family: var(--font-mono);
    font-size: 0.875rem;
    color: var(--c-text);
  }
  
  &__field {
    background: var(--c-bg-subtle);
    border: 1px solid transparent;
    color: var(--c-text);
    padding: 0.25rem 0.5rem;
    border-radius: 4px;
    font-size: 0.75rem;
  }
  
  &__canvas {
    width: 100%;
    height: 60px;
    display: block;
    cursor: pointer;
  }
}
</style>
```

---

## 9. Event Strip

A simple horizontal strip showing event markers from `EntityEventsDto`. The same time axis as the slow-state charts above; markers are visually similar to Phase 5's timeline.

```vue
<!-- src/components/EntityEventStrip.vue -->
<script setup lang="ts">
import { ref, computed, watch, onMounted } from 'vue';
import type { EntityEventsDto } from '@/types/entityHistory';
import { renderEventStrip } from '@/rendering/eventStripRenderer';
import { useResizeObserver } from '@/composables/useResizeObserver';
import { buildNodeColorMap } from '@/rendering/colorScheme';

const props = defineProps<{
  events: EntityEventsDto;
  timeRange: { from: Date; to: Date };
  selectedEventId: string | null;
}>();

const emit = defineEmits<{ select: [eventId: string | null] }>();

const canvasRef = ref<HTMLCanvasElement | null>(null);
const nodeColors = computed(() => {
  const nodes = Array.from(new Set(props.events.events.map(e => e.publisherNode)));
  return buildNodeColorMap(nodes);
});

function draw() {
  const canvas = canvasRef.value;
  if (!canvas) return;
  const ctx = canvas.getContext('2d');
  if (!ctx) return;
  
  const dpr = window.devicePixelRatio || 1;
  const cssW = canvas.clientWidth;
  const cssH = canvas.clientHeight;
  if (canvas.width !== cssW * dpr) canvas.width = cssW * dpr;
  if (canvas.height !== cssH * dpr) canvas.height = cssH * dpr;
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
  
  renderEventStrip(ctx, {
    events: props.events.events,
    timeRange: props.timeRange,
    widthPx: cssW,
    heightPx: cssH,
    selectedEventId: props.selectedEventId,
    nodeColors: nodeColors.value,
  });
}

watch([() => props.events, () => props.timeRange, () => props.selectedEventId], draw);
useResizeObserver(canvasRef, draw);
onMounted(draw);

function onClick(e: PointerEvent) {
  // Hit-test: find event whose x is closest to click x within threshold
  // (Same pattern as Phase 5; details elided. Emits select with eventId.)
}
</script>

<template>
  <section class="event-strip">
    <header class="event-strip__header">
      <h4>Events ({{ events.events.length }}<span v-if="events.truncated"> — truncated</span>)</h4>
    </header>
    <canvas
      ref="canvasRef"
      class="event-strip__canvas"
      @click="onClick"
    />
  </section>
</template>
```

The renderer is a small variant of Phase 5's timeline marker drawing: same node-color mapping, same severity treatment, but in a single horizontal lane (no swimlanes).

---

## 10. Fast State Drill-Down

The on-demand chart. Collapsed by default. When expanded, the user picks a topic and one or more numeric columns; the chart appears.

### 10.1 FastStateDrillDown.vue

```vue
<!-- src/components/FastStateDrillDown.vue -->
<script setup lang="ts">
import { ref, computed, watch } from 'vue';
import { useApi } from '@/api/useApi';
import type { FastStateTopicSchemaDto, EntityFastStateDto } from '@/types/entityHistory';
import FastStateColumnPicker from './FastStateColumnPicker.vue';
import FastStateChart from './FastStateChart.vue';

const props = defineProps<{
  entityId: string;
  timeRange: { from: Date; to: Date };
  availableTopics: string[];
}>();

const expanded = ref(false);
const api = useApi();

const selectedTopic = ref<string | null>(null);
const schema = ref<FastStateTopicSchemaDto | null>(null);
const selectedColumns = ref<string[]>([]);
const data = ref<EntityFastStateDto | null>(null);
const loading = ref(false);
const error = ref<string | null>(null);

watch(selectedTopic, async (topic) => {
  if (!topic) { schema.value = null; selectedColumns.value = []; return; }
  schema.value = await api.getEntityFastStateSchema(props.entityId, topic);
  // Default to the first numeric column
  const firstNumeric = schema.value?.columns.find(c => c.isNumeric);
  selectedColumns.value = firstNumeric ? [firstNumeric.name] : [];
});

watch([selectedTopic, selectedColumns, () => props.timeRange], async () => {
  if (!selectedTopic.value || selectedColumns.value.length === 0) {
    data.value = null;
    return;
  }
  loading.value = true;
  error.value = null;
  try {
    data.value = await api.getEntityFastState(
      props.entityId,
      selectedTopic.value,
      selectedColumns.value,
      props.timeRange.from,
      props.timeRange.to,
      5000);
  } catch (err: any) {
    error.value = err.message ?? 'Failed to load fast state';
  } finally {
    loading.value = false;
  }
});
</script>

<template>
  <section class="fast-state-drill-down">
    <button
      class="fast-state-drill-down__toggle"
      @click="expanded = !expanded"
    >
      <span>{{ expanded ? '▼' : '▶' }}</span>
      Fast state drill-down
      <span v-if="availableTopics.length === 0" class="fast-state-drill-down__hint">
        (no fast-state data for this entity)
      </span>
    </button>
    
    <div v-show="expanded && availableTopics.length > 0" class="fast-state-drill-down__body">
      <div class="fast-state-drill-down__controls">
        <label>
          Topic:
          <select v-model="selectedTopic">
            <option :value="null">— Choose a topic —</option>
            <option v-for="t in availableTopics" :key="t" :value="t">{{ t }}</option>
          </select>
        </label>
        <FastStateColumnPicker
          v-if="schema"
          :schema="schema"
          v-model:selected="selectedColumns"
        />
      </div>
      
      <div v-if="loading" class="fast-state-drill-down__loading">Loading…</div>
      <div v-else-if="error" class="fast-state-drill-down__error">{{ error }}</div>
      <FastStateChart
        v-else-if="data && data.samples.length > 0"
        :data="data"
        :time-range="timeRange"
      />
      <div v-else-if="selectedTopic && data" class="fast-state-drill-down__empty">
        No samples in the selected time range.
      </div>
      
      <div v-if="data?.downsampled" class="fast-state-drill-down__downsample-notice">
        Data downsampled: showing {{ data.samples.length }} of {{ data.totalSamples.toLocaleString() }} samples.
      </div>
    </div>
  </section>
</template>
```

### 10.2 FastStateColumnPicker.vue

```vue
<!-- src/components/FastStateColumnPicker.vue -->
<script setup lang="ts">
import { computed } from 'vue';
import type { FastStateTopicSchemaDto } from '@/types/entityHistory';

const props = defineProps<{
  schema: FastStateTopicSchemaDto;
  selected: string[];
}>();

const emit = defineEmits<{ 'update:selected': [columns: string[]] }>();

const numericColumns = computed(() => props.schema.columns.filter(c => c.isNumeric));

function toggle(name: string) {
  const isOn = props.selected.includes(name);
  emit('update:selected', isOn
    ? props.selected.filter(c => c !== name)
    : [...props.selected, name]);
}
</script>

<template>
  <div class="column-picker">
    <span class="column-picker__label">Columns:</span>
    <label
      v-for="c in numericColumns"
      :key="c.name"
      class="column-picker__chip"
      :class="{ 'column-picker__chip--on': selected.includes(c.name) }"
    >
      <input
        type="checkbox"
        :checked="selected.includes(c.name)"
        @change="toggle(c.name)"
      />
      {{ c.name }}
    </label>
    <span v-if="schema.columns.length > numericColumns.length" class="column-picker__hint">
      (non-numeric columns hidden)
    </span>
  </div>
</template>
```

### 10.3 FastStateChart.vue

A line chart with one line per selected column. Multiple Y axes are not supported in Phase 7 — all columns share a single Y scale. If the user selects columns with wildly different ranges (e.g., position.x in meters and velocity.norm in m/s), one will dwarf the other visually. Multi-axis is a Phase 8+ enhancement.

```typescript
// src/rendering/fastStateChartRenderer.ts

import type { EntityFastStateDto } from '@/types/entityHistory';

export interface FastStateChartInput {
  data: EntityFastStateDto;
  timeRange: { from: Date; to: Date };
  widthPx: number;
  heightPx: number;
  columnColors: Map<string, string>;
}

export function renderFastStateChart(ctx: CanvasRenderingContext2D, input: FastStateChartInput) {
  ctx.clearRect(0, 0, input.widthPx, input.heightPx);
  
  const from = input.timeRange.from.getTime();
  const to   = input.timeRange.to.getTime();
  const xScale = input.widthPx / (to - from);
  
  // Find global min/max across all selected columns
  let minV = +Infinity, maxV = -Infinity;
  for (const s of input.data.samples) {
    for (const c of input.data.columns) {
      const v = s.values[c];
      if (v !== null && v !== undefined && Number.isFinite(v)) {
        if (v < minV) minV = v;
        if (v > maxV) maxV = v;
      }
    }
  }
  if (minV === Infinity) return;  // no numeric data
  const range = Math.max(maxV - minV, 1e-9);
  const padding = 12;
  const yScale = (input.heightPx - padding * 2) / range;
  
  function yPx(v: number): number {
    return input.heightPx - padding - (v - minV) * yScale;
  }
  
  // Draw axes
  ctx.strokeStyle = 'rgba(255,255,255,0.15)';
  ctx.lineWidth = 1;
  ctx.beginPath();
  ctx.moveTo(0, input.heightPx - padding);
  ctx.lineTo(input.widthPx, input.heightPx - padding);
  ctx.moveTo(0, padding);
  ctx.lineTo(input.widthPx, padding);
  ctx.stroke();
  
  // Y-axis labels
  ctx.fillStyle = 'rgba(255,255,255,0.5)';
  ctx.font = '10px var(--font-mono, monospace)';
  ctx.textBaseline = 'top';
  ctx.fillText(maxV.toFixed(2), 4, padding + 2);
  ctx.textBaseline = 'bottom';
  ctx.fillText(minV.toFixed(2), 4, input.heightPx - padding - 2);
  
  // Draw one line per column
  ctx.lineWidth = 1.5;
  for (const c of input.data.columns) {
    ctx.strokeStyle = input.columnColors.get(c) ?? '#5b9dff';
    ctx.beginPath();
    let started = false;
    for (const s of input.data.samples) {
      const v = s.values[c];
      if (v === null || v === undefined || !Number.isFinite(v)) {
        // Gap in data; lift pen
        started = false;
        continue;
      }
      const x = (new Date(s.publishWallclock).getTime() - from) * xScale;
      const y = yPx(v as number);
      if (!started) { ctx.moveTo(x, y); started = true; }
      else { ctx.lineTo(x, y); }
    }
    ctx.stroke();
  }
  
  // Legend
  let legendX = input.widthPx - 200;
  let legendY = padding + 4;
  ctx.textAlign = 'left';
  ctx.textBaseline = 'middle';
  ctx.font = '11px var(--font-sans, sans-serif)';
  for (const c of input.data.columns) {
    ctx.fillStyle = input.columnColors.get(c) ?? '#5b9dff';
    ctx.fillRect(legendX, legendY - 6, 12, 2);
    ctx.fillStyle = 'rgba(255,255,255,0.8)';
    ctx.fillText(c, legendX + 18, legendY);
    legendY += 16;
  }
}
```

---

## 11. URL State and Cross-View Navigation

### 11.1 URL Pattern

```
/v/entity/{entityId}?session={sessionId}&from={iso}&to={iso}&select={eventId}&fastStateTopic={topic}&fastStateColumns={col1},{col2}
```

Components:
- `/v/entity/{entityId}` — base path
- `session` — required; entity ID is unique within a session
- `from`, `to` — time range; defaults to entity's lifespan
- `select` — selected event ID in the event strip
- `fastStateTopic` — current fast-state topic (if drill-down expanded)
- `fastStateColumns` — comma-separated column names

### 11.2 useEntityHistoryUrl

```typescript
// src/composables/useEntityHistoryUrl.ts
import { watch } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { useEntityHistoryStore } from '@/stores/entityHistoryStore';
import { debounce } from '@/utils/debounce';

export function useEntityHistoryUrl() {
  const route = useRoute();
  const router = useRouter();
  const store = useEntityHistoryStore();
  
  // URL → store
  watch(() => ({ params: route.params, query: route.query }), ({ params, query }) => {
    const entityId = params.entityId as string;
    const sessionId = query.session as string;
    if (!entityId || !sessionId) return;
    
    store.setEntity(entityId, sessionId);
    
    if (query.from && query.to) {
      store.setTimeRange(new Date(query.from as string), new Date(query.to as string));
    }
    if (query.select) store.selectedEventId = query.select as string;
  }, { immediate: true });
  
  // Store → URL
  const writeUrl = debounce(() => {
    if (!store.entityId || !store.sessionId) return;
    const q: Record<string, string> = {
      session: store.sessionId,
      from: store.timeRange.from.toISOString(),
      to:   store.timeRange.to.toISOString(),
    };
    if (store.selectedEventId) q.select = store.selectedEventId;
    router.replace({ query: q });
  }, 250);
  
  watch(() => [
    store.timeRange.from.getTime(),
    store.timeRange.to.getTime(),
    store.selectedEventId,
  ], writeUrl);
}
```

### 11.3 Cross-View Pivots

The pivot catalog is now complete. Phase 7 makes the "Show entity history" pivot functional everywhere, and adds the reverse pivots from the entity history view.

| From | Pivot | To | Behavior |
|---|---|---|---|
| Timeline (Phase 5) | "Show entity history" | EntityHistoryView | Enabled when event has non-null `entity_id`. `/v/entity/{eid}?session={sid}` |
| CausalTree (Phase 6) | "Show entity history" | EntityHistoryView | Same |
| EntityHistoryView event strip | "Show in timeline" | TimelineView | `/v/timeline/{sid}?from=(t-2s)&to=(t+2s)&select={eid}` |
| EntityHistoryView event strip | "Show causal tree" | CausalTreeView | `/v/causal/{eid}` |
| EntityHistoryView slow-state click | "Show in timeline" | TimelineView | Targets the slow-state event's publish_wallclock |
| EntityHistoryView slow-state click | "Show causal tree" | CausalTreeView | Uses the slow-state sample's `trace_id` if non-zero; disabled if zero |

The `EventInspector` component's `showEntityHistoryPivot` prop is now enabled (false in Phase 6 by stub). Component:

```vue
<!-- inside EventInspector.vue setup -->
function pivotToEntity() {
  if (!props.event.entityId || !props.sessionId) return;
  router.push({
    name: 'entity-history',
    params: { entityId: props.event.entityId },
    query: { session: props.sessionId },
  });
}
```

### 11.4 Router Configuration

```typescript
// In src/router/index.ts, additions:
{
  path: '/v/entity/:entityId',
  name: 'entity-history',
  component: () => import('@/views/EntityHistoryView.vue'),
},
{
  path: '/v/entities/:sessionId',
  name: 'entity-picker',
  component: () => import('@/views/EntityPickerView.vue'),
},
```

### 11.5 EntityPickerView

When the engineer doesn't know the entity ID, they go through the Session Browser's "Entities" tab → see the entity list → pick one.

```vue
<!-- src/views/EntityPickerView.vue -->
<script setup lang="ts">
import { onMounted, ref } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { useApi } from '@/api/useApi';
import type { EntitySummaryDto } from '@/types/entityHistory';

const route = useRoute();
const router = useRouter();
const api = useApi();
const sessionId = route.params.sessionId as string;
const entities = ref<EntitySummaryDto[]>([]);
const filter = ref('');
const loading = ref(false);

async function load() {
  loading.value = true;
  try {
    const result = await api.listEntities(sessionId, 200);
    entities.value = result.entities;
  } finally { loading.value = false; }
}

const filtered = computed(() => {
  if (!filter.value) return entities.value;
  const f = filter.value.toLowerCase();
  return entities.value.filter(e =>
    e.entityId.toLowerCase().includes(f) ||
    e.samplePlayerId?.toLowerCase().includes(f) ||
    e.topics.some(t => t.toLowerCase().includes(f))
  );
});

function open(e: EntitySummaryDto) {
  router.push({
    name: 'entity-history',
    params: { entityId: e.entityId },
    query: { session: sessionId },
  });
}

onMounted(load);
</script>

<template>
  <div class="entity-picker">
    <header class="entity-picker__header">
      <h1>Entities ({{ entities.length }})</h1>
      <input
        v-model="filter"
        placeholder="Filter by ID, player, topic…"
        class="entity-picker__filter"
      />
    </header>
    <div v-if="loading">Loading…</div>
    <ul v-else class="entity-picker__list">
      <li v-for="e in filtered" :key="e.entityId" class="entity-picker__item" @click="open(e)">
        <div class="entity-picker__id">{{ e.entityId }}</div>
        <div class="entity-picker__meta">
          {{ e.eventCount.toLocaleString() }} events ·
          {{ e.topics.length }} topics
          <span v-if="e.samplePlayerId">· {{ e.samplePlayerId }}</span>
        </div>
        <div class="entity-picker__topics">
          {{ e.topics.slice(0, 5).join(', ') }}
          <span v-if="e.topics.length > 5">+{{ e.topics.length - 5 }} more</span>
        </div>
      </li>
    </ul>
  </div>
</template>
```

Linked from the Session Browser (Phase 3): each session card gets an "Entities" button alongside the existing affordances.

---

## 12. Test Plan for Phase 7

### 12.1 Backend Unit Tests

**Parquet/ParquetReaderTests.cs**
- `InspectSchemaAsync`: returns column names and types for a known Parquet file
- `InspectSchemaAsync`: numeric vs non-numeric flag correct
- `ReadTimeSeriesAsync`: with maxSamples >= totalSamples: returns all data, downsampled=false
- `ReadTimeSeriesAsync`: with maxSamples < totalSamples: stride-downsampled, downsampled=true
- `ReadTimeSeriesAsync`: time-range filter pushed into Parquet read
- `ReadTimeSeriesAsync`: with non-existent file: empty result, no exception
- Multi-file overload: reads from all listed files, merges chronologically

**Parquet/ParquetSchemaInspectorTests.cs**
- Sanity check that DESCRIBE syntax against a synthetic Parquet file yields expected columns

**WebApi/EntityDiscoveryServiceTests.cs**
- Returns entities with first/last seen, event count, topics
- Topic filter restricts to matching entities
- Player filter restricts appropriately
- Empty session: empty list
- Limit clamped

**WebApi/EntityEventsServiceTests.cs**
- Returns events with `entity_id = X` in time range
- Empty: returns empty
- Truncation at limit: `truncated=true`

**WebApi/EntitySlowStateServiceTests.cs**
- Groups samples by topic correctly
- Empty result for entity with no slow-state events
- Topic filter applied

**WebApi/EntityFastStateServiceTests.cs**
- `GetAvailableTopics`: discovers topics by walking fast_state directories
- `GetSchemaAsync`: returns null when no Parquet exists for (entity, topic)
- `ReadAsync`: returns expected samples
- Multi-interval entity (samples spread across two interval directories): all returned

**WebApi/EntityEndpointsTests.cs**
- All endpoints return correct status codes
- Invalid time ranges (to < from): 400
- maxSamples out of range: 400
- Empty column list on fast-state read: 400
- Unknown entity: appropriate 200 with empty result (vs 404 for cases like summary)

### 12.2 Backend Integration Tests

**EntityHistoryRoundTripTests.cs**
- Push known events with `entity_id = X`, slow-state samples for X, fast-state Parquet for X
- Query all endpoints
- Verify expected results
- Build a bundle including fast-state for X
- Open in offline viewer
- Run identical queries; assert results match

**FastStateParquetRoundTripTests.cs**
- Write a Parquet file with known data
- Read back via `ParquetReader`
- Verify exact sample-by-sample equality
- Multiple intervals containing data for same entity: combined read yields chronologically merged samples

### 12.3 Frontend Unit Tests (Vitest)

**slowStateChartRenderer.spec.ts**
- Numeric rendering: stepped line crosses each sample point
- Categorical rendering: bands of correct widths at correct positions
- Empty samples: no error, nothing drawn
- Single-sample series: extends to right edge
- Numeric field with all-same values: range collapses to constant line at mid-height

**eventStripRenderer.spec.ts**
- One marker per event at correct x position
- Selected event has highlighted ring
- Notable events have distinct marker

**fastStateChartRenderer.spec.ts**
- Multiple columns drawn with distinct colors
- Y-axis range covers all data
- Gaps in data (null values) break the line
- Legend present with column names

**useEntityHistoryQuery.spec.ts**
- Sequential summary fetch then parallel events/slowState/fastStateTopics fetches
- Time range defaults to entity's lifespan
- Cancellation on rapid entity switches

### 12.4 E2E Tests (Playwright)

```typescript
test('navigate to entity history from timeline', async ({ page }) => {
  await page.goto('http://localhost:5300/v/timeline/test-session');
  await page.locator('.timeline-canvas').click({ position: { x: 500, y: 200 } });
  await page.locator('.event-inspector__pivot-entity').click();
  await expect(page).toHaveURL(/\/v\/entity\//);
  await expect(page.locator('.slow-state-chart')).toBeVisible();
});

test('expand fast-state drill-down and select columns', async ({ page }) => {
  await page.goto('http://localhost:5300/v/entity/known-entity?session=known-session');
  await page.locator('.fast-state-drill-down__toggle').click();
  await page.selectOption('.fast-state-drill-down select', 'transforms');
  await page.locator('.column-picker__chip:has-text("x")').click();
  await expect(page.locator('canvas')).toBeVisible();
});

test('shareable URL', async ({ page }) => {
  const url = 'http://localhost:5300/v/entity/known-entity?session=known-session&fastStateTopic=transforms&fastStateColumns=x,y';
  await page.goto(url);
  // The view should land in the expanded drill-down state with topic and columns selected
  await expect(page.locator('.fast-state-drill-down__body')).toBeVisible();
});
```

### 12.5 Performance Tests

- Entity discovery on a 200-entity session: < 500 ms
- Entity events for 5000-event entity: < 200 ms
- Entity slow-state for 100-sample entity: < 100 ms
- Fast-state read of 30-minute, 60Hz entity (108K samples), downsampled to 5000: < 1 second
- Cold-cache navigate to entity history view → all panels rendered: < 1.5 seconds

---

## 13. Phase 7 Risks and Mitigations

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| Parquet reading via DuckDB has higher per-query overhead than expected | Medium | Medium | Test on day 1 with realistic file sizes. If > 100 ms per file open is observed, consider a `ParquetReader` connection pool. |
| `ANY_VALUE` semantics for player_id misleads users when ownership changes | Medium | Low | The lifecycle ribbon shows the full ownership history. Document the field as "sample player" in the UI. |
| Lifecycle topic-name conventions don't match the customer's integration | High | Medium | The conventions are documented in `docs/lifecycle-classification.md`; if customers diverge significantly, Phase 8 makes it configurable. Until then, the view still works (just shows no lifecycle band). |
| Categorical slow-state field with hundreds of distinct values renders chaotically | Medium | Low | Cap distinct-value count at 15; values beyond that render as a generic gray band labeled "other". |
| Multi-axis disparity in fast-state chart misleads users | High | Medium | Document the single-Y-axis caveat. Multi-axis is Phase 10+ work. |
| Fast-state column-picker overwhelms with non-relevant fields | Medium | Low | Numeric-only filter already hides non-plottable columns. UX feedback in week 3 will guide further filtering. |
| BundleOpenManager dependency in EntityFastStateService breaks Observer DI (offline-only service in observer mode) | Low | Medium | Make `BundleOpenManager` optional dependency (nullable parameter); only the offline viewer registers it. |
| Pre-Phase-7 intervals lack the slow_state entity index; queries slow | Medium | Low | Same approach as Phase 6: retention naturally evicts; document. |
| Slow-state field detection (auto-pick numeric/categorical) sometimes picks the wrong default field | High | Low | Easy fix: the user picks a different field from the dropdown. Document. |

---

## 14. Definition of Done for Phase 7

### Build & Run

- [ ] `Tracer.Storage.Parquet` builds clean
- [ ] All new endpoints registered in both Observer and Offline Viewer
- [ ] Frontend builds with the new view, components, composables
- [ ] OpenAPI document includes `/api/entities/*` endpoints

### Schema

- [ ] `idx_slow_state_entity_time` created on all new intervals/bundles
- [ ] Existing intervals tolerated (slower, not broken)

### Backend: Discovery

- [ ] `GET /api/entities?sessionId=X` returns entities with summary fields
- [ ] Topic and player filters work
- [ ] Empty session returns empty list (not 404)

### Backend: Events / Slow State

- [ ] `GET /api/entities/{id}/events`: returns events with that `entity_id`
- [ ] `GET /api/entities/{id}/slow-state`: returns samples grouped by topic
- [ ] Cross-interval queries return data from multiple intervals

### Backend: Fast State

- [ ] `GET /api/entities/{id}/fast-state/topics`: lists topics with Parquet files
- [ ] `GET /api/entities/{id}/fast-state/{topic}/schema`: returns column list
- [ ] `GET /api/entities/{id}/fast-state/{topic}`: returns time-series data
- [ ] Downsampling kicks in above `maxSamples`; result indicates `downsampled=true`
- [ ] Multi-interval Parquet reads merged correctly

### Frontend: Entity History View

- [ ] Renders for a known entity with summary, lifecycle, slow-state, events, and (collapsed) fast-state drill-down
- [ ] Each slow-state topic gets its own chart row
- [ ] Numeric fields plot as stepped line; categorical fields plot as color bands
- [ ] Event strip shows all events touching the entity
- [ ] Click on event marker selects it; selection persists in URL

### Frontend: Fast-State Drill-Down

- [ ] Collapsed by default
- [ ] Expand → topic picker shows topics with data
- [ ] Topic selection → column picker shows numeric columns
- [ ] Column selection → chart renders within 1 second
- [ ] Downsampling notice appears when applicable
- [ ] Selections persist in URL

### Frontend: Lifecycle Ribbon

- [ ] Spawn / ownership / destruction markers in distinct colors
- [ ] Ownership-period bands span their respective durations
- [ ] Hovering a marker shows tooltip with topic and time

### Frontend: Entity Picker

- [ ] Lists entities for a session with summary fields
- [ ] Filter input narrows by ID, player, or topic substring
- [ ] Clicking an entity navigates to its entity history view

### Cross-View Navigation

- [ ] "Show entity history" pivot works from Timeline (Phase 5) and CausalTree (Phase 6)
- [ ] From EntityHistoryView event click: "Show in timeline" and "Show causal tree" work
- [ ] Slow-state event with trace_id > 0: "Show causal tree" is enabled; with trace_id = 0: disabled
- [ ] URL state survives reload

### Testing

- [ ] All Phase 1-6 tests pass
- [ ] Phase 7 backend unit tests pass (target: 40+)
- [ ] Phase 7 backend integration tests pass: round-trip parity, multi-interval, fast-state Parquet
- [ ] Phase 7 frontend unit tests pass
- [ ] At least one Playwright E2E test passes

### Performance

- [ ] Entity discovery on 200-entity session: < 500 ms
- [ ] Entity history view full load: < 1.5 seconds cold cache
- [ ] Fast-state Parquet read (30-min entity, downsampled to 5000): < 1 second

### Documentation

- [ ] `docs/entity-history.md` explains the view's structure
- [ ] `docs/lifecycle-classification.md` lists the topic-name conventions
- [ ] `docs/api-entities.md` documents the new endpoints
- [ ] CHANGELOG entry

---

## 15. Handoff to Phase 8

What Phase 8 inherits from Phase 7:

- **Full cross-view navigation** — every view connects to every other view that makes sense. Phase 8 adds annotations as a fourth dimension: every view can show annotations attached to events/entities/traces.
- **The Parquet-reading pattern** — Phase 9 (replication latency) reuses `ParquetReader` for any topic that's heavy enough to warrant Parquet storage.
- **The shareable URL pattern** — Phase 8's saved views are URL templates with bookmark metadata.
- **The DTO/contract pattern** — Phase 8's annotation API follows the same shape.

What Phase 8 must address that Phase 7 deferred:

- **Annotations on events, entities, and traces**: persisted to the bundle's `annotations/` directory and the Observer's annotation store
- **Saved views**: bookmark a viewport+filter combination with a label and description
- **Trigger evaluation log**: scenario-author-facing view, separate workflow
- **Lifecycle topic conventions** become configurable
- **Multi-axis fast-state charts** if the engineering community asks for them

What's now possible after Phase 7:

The complete diagnostic toolkit. Three core views (timeline, causal tree, entity history) connect every dimension of the data:

- **Time**: what happened across all nodes in a window? → Timeline
- **Causation**: what caused this event? → Causal Tree
- **Entity**: what's the lifecycle of this specific thing? → Entity History

Every view answers questions the others can't. Every event opens every view. The trace_id and parent_event_id machinery from Phase 1 pays off across all three. The separated fast-state storage from architecture §4 validates: events stay queryable at high rates while fast-state data sits cold until specifically needed.

By Phase 7, Tracer is no longer "a viewer for the simulation engineers" — it's a coherent diagnostic system. Subsequent phases add polish (annotations, saved views, SQL escape hatches, advanced analyses) but the architectural shape is locked in.
