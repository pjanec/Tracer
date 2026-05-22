# BATCH-36 Instructions — Phase 7 Entity History: Service Layer and API Wiring

**Batch type:** Implementation  
**Estimated effort:** 20–24 hours  
**Developer skill doc:** `d:\WORK\Tracer\.github\skills\developer\SKILL.md`

---

## Context

BATCH-35 added the foundation for Phase 7:
- `Tracer.Storage.Parquet/ParquetReader.cs` — reads Parquet files via DuckDB `read_parquet()`
- `src/Tracer.WebApi/Queries/FastStateFileLocator.cs` — locates fast-state files per (topic, entity)
- `BuildSlowStateUnionSql(whereClause, orderByClause, limit?)` on `PooledMultiIntervalConnection`
- `idx_slow_state_entity_time` index on `slow_state(instance_key, publish_wallclock)`

**Important deviations from design doc established in BATCH-35:**
1. `FastStateFileLocator` constructor takes `Func<string?>? getBundleWorkingDirectory = null` (NOT `BundleOpenManager?`) to avoid a circular dependency (`OfflineViewer → WebApi`). For OfflineViewer DI, pass `() => bundleOpenManager.Current?.WorkingDirectory`.
2. `FastStateFileLocator.GetAvailableTopicsForEntity` returns `BundleNaming.SafeFileName`-encoded directory names, NOT original topic names. **Do not use `GetAvailableTopicsForEntity` to populate entity topic lists in `EntityDiscoveryService`** — use the events-table `ARRAY_AGG(DISTINCT topic)` query instead. `GetAvailableTopicsForEntity` is only used by `EntityFastStateService.GetAvailableTopics`.
3. The `slow_state` table uses `instance_key` (not `entity_id`) as the entity identifier column.

---

## Tasks

### Task 1 — TRC-P7-003: EntityDiscoveryService

**File:** `src/Tracer.WebApi/Queries/EntityDiscoveryService.cs` (new)  
**Tests:** `tests/Tracer.Tests.Unit/WebApi/EntityDiscoveryServiceTests.cs` (new)  
**Full spec:** [TASK-DETAIL.md §TRC-P7-003](../../../docs/TASK-DETAIL.md#trc-p7-003--entitydiscoveryservice)

**Implementation pattern:**
```csharp
public sealed class EntityDiscoveryService(LiveMultiIntervalReader reader, ILogger<EntityDiscoveryService> logger)
{
    public async Task<IReadOnlyList<EntitySummary>> DiscoverAsync(
        string sessionId,
        WallclockTime sessionStart, WallclockTime sessionEnd,
        string? topicFilter, string? playerFilter,
        int limit,
        CancellationToken ct)
    {
        await using var pooled = await reader.AcquireAsync(ct);
        
        var whereExtra = "";
        if (topicFilter != null)    whereExtra += " AND topic = $topicFilter";
        if (playerFilter != null)   whereExtra += " AND owning_player_id = $playerFilter";
        
        var sql = pooled.WithEventsCte($"""
            SELECT entity_id,
                   MIN(publish_wallclock)            AS first_seen,
                   MAX(publish_wallclock)            AS last_seen,
                   COUNT(*)                          AS event_count,
                   ANY_VALUE(owning_player_id)       AS sample_player_id,
                   ARRAY_AGG(DISTINCT topic ORDER BY topic) AS topics
            FROM events
            WHERE entity_id IS NOT NULL
              AND publish_wallclock >= $from
              AND publish_wallclock < $to
              {whereExtra}
            GROUP BY entity_id
            ORDER BY event_count DESC
            LIMIT $limit
            """);
        
        using var cmd = pooled.Connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new DuckDBParameter("from", sessionStart.ToDateTimeOffset().UtcDateTime));
        cmd.Parameters.Add(new DuckDBParameter("to",   sessionEnd.ToDateTimeOffset().UtcDateTime));
        cmd.Parameters.Add(new DuckDBParameter("limit", limit));
        if (topicFilter != null)  cmd.Parameters.Add(new DuckDBParameter("topicFilter", topicFilter));
        if (playerFilter != null) cmd.Parameters.Add(new DuckDBParameter("playerFilter", playerFilter));
        
        var results = new List<EntitySummary>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var entityId = r.GetString(0);
            // map remaining columns...
            var topics = ReadStringList(r, 5); // ARRAY_AGG column
            results.Add(new EntitySummary { EntityId = entityId, ... });
        }
        return results;
    }
    
    private static IReadOnlyList<string> ReadStringList(DbDataReader r, int col)
    {
        // DuckDB returns ARRAY_AGG as a List<string> boxed in an object
        if (r.IsDBNull(col)) return Array.Empty<string>();
        var raw = r.GetValue(col);
        if (raw is List<string> list) return list;
        if (raw is IEnumerable<object> enumerable)
            return enumerable.Select(x => x?.ToString() ?? "").ToList();
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

**Note:** Use `ObserverFixture`-style setup in tests (fake DuckDB intervals). See `EventQueryServiceTests.cs` for the test setup pattern using `StubMultiIntervalReader` or `ObserverFixture`.

**Required tests (from spec §SC1–SC8):** All 8 success condition tests. Use the `ObserverFixture` from `Tracer.TestHarness` or the existing `StubTracker`/`MultiIntervalReader` test setup pattern used in existing WebApi tests.

---

### Task 2 — TRC-P7-004: EntityEventsService

**File:** `src/Tracer.WebApi/Queries/EntityEventsService.cs` (new)  
**Tests:** `tests/Tracer.Tests.Unit/WebApi/EntityEventsServiceTests.cs` (new)  
**Full spec:** [TASK-DETAIL.md §TRC-P7-004](../../../docs/TASK-DETAIL.md#trc-p7-004--entityeventsservice)

**Implementation pattern:**
```csharp
public sealed record EntityEventsResult
{
    public required string EntityId { get; init; }
    public required IReadOnlyList<EventRecord> Events { get; init; }
    public required bool Truncated { get; init; }
}

public sealed class EntityEventsService(LiveMultiIntervalReader reader, ILogger<EntityEventsService> logger)
{
    public async Task<EntityEventsResult> GetEventsAsync(
        string entityId, WallclockTime from, WallclockTime to, int limit, CancellationToken ct)
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
        cmd.Parameters.Add(new DuckDBParameter("entityId",   entityId));
        cmd.Parameters.Add(new DuckDBParameter("from",       from.ToDateTimeOffset().UtcDateTime));
        cmd.Parameters.Add(new DuckDBParameter("to",         to.ToDateTimeOffset().UtcDateTime));
        cmd.Parameters.Add(new DuckDBParameter("limitPlus1", limit + 1));
        
        var events = new List<EventRecord>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) events.Add(EventRecordMapper.FromReader(r));
        
        bool truncated = events.Count > limit;
        if (truncated) events.RemoveAt(events.Count - 1);
        return new EntityEventsResult { EntityId = entityId, Events = events, Truncated = truncated };
    }
}
```

**Required tests (from spec §SC1–SC7):** All 7 success condition tests. Note: SC7 (SQL injection check) — inspect that `$entityId` appears in the SQL string, not the entity ID value as a literal.

---

### Task 3 — TRC-P7-005: EntitySlowStateService

**File:** `src/Tracer.WebApi/Queries/EntitySlowStateService.cs` (new)  
**Tests:** `tests/Tracer.Tests.Unit/WebApi/EntitySlowStateServiceTests.cs` (new)  
**Full spec:** [TASK-DETAIL.md §TRC-P7-005](../../../docs/TASK-DETAIL.md#trc-p7-005--entityslowstateservice)

**Key implementation notes:**
- The slow_state entity identifier is `instance_key` (NOT `entity_id`). The design doc used `entity_id` loosely — the actual column name is `instance_key`.
- Use `pooled.BuildSlowStateUnionSql(whereClause: "...", orderByClause: "ORDER BY publish_wallclock")` directly (NOT `WithEventsCte`).
- For topic filter: build named params `$topic0`, `$topic1`, ... and `IN ($topic0, $topic1, ...)` WHERE clause — never string-interpolate.
- Results are grouped in-memory by topic using `SortedDictionary<string, List<SlowStateSample>>` then converted to `IReadOnlyDictionary`.

**Implementation pattern:**
```csharp
public sealed record SlowStateSample
{
    public required string Topic { get; init; }
    public required WallclockTime PublishWallclock { get; init; }
    public required string PayloadJson { get; init; }
    public required ulong TraceId { get; init; }  // 0 when DB null
}

public sealed record EntitySlowStateResult
{
    public required string EntityId { get; init; }
    public required IReadOnlyDictionary<string, IReadOnlyList<SlowStateSample>> ByTopic { get; init; }
}

public sealed class EntitySlowStateService(LiveMultiIntervalReader reader, ILogger<EntitySlowStateService> logger)
{
    public async Task<EntitySlowStateResult> GetAsync(
        string entityId, WallclockTime from, WallclockTime to,
        IReadOnlyList<string>? topicFilter, CancellationToken ct)
    {
        await using var pooled = await reader.AcquireAsync(ct);
        
        var whereClause = "WHERE instance_key = $entityId" +
                          " AND publish_wallclock >= $from" +
                          " AND publish_wallclock < $to";
        if (topicFilter?.Count > 0)
        {
            var inList = string.Join(",", Enumerable.Range(0, topicFilter.Count).Select(i => $"$topic{i}"));
            whereClause += $" AND topic IN ({inList})";
        }
        
        var sql = pooled.BuildSlowStateUnionSql(
            whereClause: whereClause,
            orderByClause: "ORDER BY publish_wallclock");
        
        using var cmd = pooled.Connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.Add(new DuckDBParameter("entityId", entityId));
        cmd.Parameters.Add(new DuckDBParameter("from",     from.ToDateTimeOffset().UtcDateTime));
        cmd.Parameters.Add(new DuckDBParameter("to",       to.ToDateTimeOffset().UtcDateTime));
        for (int i = 0; topicFilter != null && i < topicFilter.Count; i++)
            cmd.Parameters.Add(new DuckDBParameter($"topic{i}", topicFilter[i]));
        
        var byTopic = new SortedDictionary<string, List<SlowStateSample>>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            // slow_state columns: sequence_number(0), publish_wallclock(1), receive_wallclock(2),
            // publisher_node(3), subscriber_node(4), topic(5), instance_key(6), trace_id(7), payload(8)
            // Note: when using BuildSlowStateUnionSql with multiple attached DBs, __source_alias 
            // may be appended at the end (col 9). Read only cols 0-8.
            var topic    = r.GetString(5);
            var wallclock = WallclockTime.FromUnixNanoseconds(
                ((DateTime)r.GetValue(1)).ToUniversalTime().Ticks * 100L - 
                WallclockTime.TicksToNanoseconds); // see existing WallclockTime parsing pattern
            var payload  = r.GetString(8);
            var traceId  = r.IsDBNull(7) ? 0UL : Convert.ToUInt64(r.GetValue(7));
            
            if (!byTopic.TryGetValue(topic, out var list))
                byTopic[topic] = list = new List<SlowStateSample>();
            list.Add(new SlowStateSample { Topic = topic, PublishWallclock = wallclock, PayloadJson = payload, TraceId = traceId });
        }
        
        return new EntitySlowStateResult
        {
            EntityId = entityId,
            ByTopic = byTopic.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<SlowStateSample>)kv.Value)
        };
    }
}
```

**Important:** For `WallclockTime` parsing from DuckDB TIMESTAMP_NS, use the same approach as `EventRecordMapper` (see `GetWallclock` helper there). Do NOT reimplement — extract or reuse the helper.

**Required tests (from spec §SC1–SC6):** All 6 success condition tests. Note SC6 (SQL injection in topic filter) — verify that `slow_state` table still exists after injection attempt.

---

### Task 4 — TRC-P7-008: EntityFastStateService

**File:** `src/Tracer.WebApi/Queries/EntityFastStateService.cs` (new)  
**Tests:** `tests/Tracer.Tests.Unit/WebApi/EntityFastStateServiceTests.cs` (new)  
**Full spec:** [TASK-DETAIL.md §TRC-P7-008](../../../docs/TASK-DETAIL.md#trc-p7-008--entityfaststateservice)

**Implementation:**
```csharp
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

public sealed class EntityFastStateService(
    ParquetReader parquet,
    FastStateFileLocator locator,
    ILogger<EntityFastStateService> logger)
{
    public IReadOnlyList<string> GetAvailableTopics(string entityId)
        => locator.GetAvailableTopicsForEntity(entityId);
    
    public async Task<FastStateTopicSchema?> GetSchemaAsync(
        string entityId, string topic, CancellationToken ct)
    {
        var paths = locator.LocateFiles(topic, entityId);
        if (paths.Count == 0) return null;
        var schema = await parquet.InspectSchemaAsync(paths[0], ct);
        var cols = schema.Columns
            .Where(c => c.Name != "publish_wallclock" && c.Name != "instance_key")
            .ToList();
        return new FastStateTopicSchema { EntityId = entityId, Topic = topic, Columns = cols };
    }
    
    public async Task<EntityFastStateResult> ReadAsync(
        string entityId, string topic,
        IReadOnlyList<string> columns,
        WallclockTime from, WallclockTime to,
        int maxSamples, CancellationToken ct)
    {
        var paths = locator.LocateFiles(topic, entityId);
        if (paths.Count == 0)
            return new EntityFastStateResult { EntityId = entityId, Topic = topic,
                Columns = Array.Empty<string>(), Samples = Array.Empty<ParquetSample>(),
                TotalSamples = 0, Downsampled = false };
        
        var result = await parquet.ReadTimeSeriesAsync(paths, entityId, columns, from, to, maxSamples, ct);
        return new EntityFastStateResult
        {
            EntityId = entityId, Topic = topic,
            Columns = columns.ToList(),
            Samples = result.Samples,
            TotalSamples = result.TotalSamples,
            Downsampled = result.Downsampled
        };
    }
}
```

**Required tests (from spec §SC1–SC7):** All 7 success condition tests. Tests may use real temp Parquet files (same approach as `ParquetReaderTests`).

---

### Task 5 — TRC-P7-009: Entity Web API Endpoints, DTOs, and Wiring

**New files:**
- `src/Tracer.WebApi/Endpoints/EntityEndpoints.cs`
- `src/Tracer.WebApi/Contracts/Dto/EntityDtos.cs` — all 9 DTO types
- `tests/Tracer.Tests.Unit/WebApi/EntityEndpointsTests.cs` (unit tests via WebApiFixture)
- `tests/Tracer.Tests.Integration/EntityHistoryRoundTripTests.cs` (integration test)

**Modified files:**
- `src/Tracer.Observer/ObserverHostBuilder.cs` — add 6 new service registrations + EntityEndpoints.Map
- `src/Tracer.OfflineViewer/OfflineViewerHostBuilder.cs` — add 6 new service registrations + EntityEndpoints.Map
- `src/Tracer.TestHarness/Observer/WebApiFixture.cs` — add new services to enable unit tests
- `src/Tracer.TestHarness/Observer/ObserverFixture.cs` — add new services for integration test

**Full spec:** [TASK-DETAIL.md §TRC-P7-009](../../../docs/TASK-DETAIL.md#trc-p7-009--entity-web-api-endpoints-dtos-and-wiring)

#### EntityEndpoints.cs

Follow the exact handler signatures and validation logic from the design doc (§5.2). Key points:
- `HandleFastStateAsync`: returns HTTP 400 with `Title = "Missing column"` if `column` is null or empty
- `HandleFastStateAsync`: returns HTTP 400 with `Title = "maxSamples out of range"` if outside [10, 10000]
- `HandleSummaryAsync`: returns `TypedResults.NotFound()` when session is null OR entity not in discovery results
- `HandleListAsync`: clamps `limit` with `Math.Clamp(limit, 1, 5000)` BEFORE calling `DiscoverAsync`
- Use `TypedResults.Problem(new ProblemDetails { Title = "...", Status = 400 })` for validation errors
- All routes use `.WithOpenApi()` — no `.WithName(...)` required

#### DTOs (EntityDtos.cs)

Copy exactly from design doc §5.3. Place all 9 DTOs in one file `EntityDtos.cs` in `Tracer.WebApi.Contracts.Dto`:
- `EntityListDto`, `EntitySummaryDto`, `EntityEventsDto`, `EntitySlowStateDto`, `SlowStateSampleDto`
- `FastStateTopicSchemaDto`, `FastStateColumnDto`, `EntityFastStateDto`, `FastStateSampleDto`

DTO mapper helpers (inline static classes in `EntityEndpoints.cs` or in a separate `EntityDtoMapper.cs`):
- `EntityDtoMapper.Map(EntitySummary)` → `EntitySummaryDto`
- `EntityEventsDtoMapper.Map(EntityEventsResult)` → `EntityEventsDto` (reuse existing `EventDto`)
- `EntitySlowStateDtoMapper.Map(EntitySlowStateResult)` → `EntitySlowStateDto`; `SlowStateSampleDto.TraceId` is null when `TraceId == 0`
- `FastStateSchemaDtoMapper.Map(FastStateTopicSchema)` → `FastStateTopicSchemaDto`
- `EntityFastStateDtoMapper.Map(EntityFastStateResult)` → `EntityFastStateDto`

#### DI Wiring

**ObserverHostBuilder.cs** — add after `TraceQueryService` registration:
```csharp
// ── Entity history services (Phase 7) ─────────────────────────────────
builder.Services.AddSingleton<ParquetReader>();
builder.Services.AddSingleton<FastStateFileLocator>();
builder.Services.AddSingleton<EntityDiscoveryService>();
builder.Services.AddSingleton<EntityEventsService>();
builder.Services.AddSingleton<EntitySlowStateService>();
builder.Services.AddSingleton<EntityFastStateService>();
```
Add `EntityEndpoints.Map(app)` after `TraceEndpoints.Map(app)`.

**OfflineViewerHostBuilder.cs** — add after `TraceQueryService` registration:
```csharp
builder.Services.AddSingleton<ParquetReader>();
builder.Services.AddSingleton<FastStateFileLocator>(sp =>
    new FastStateFileLocator(
        sp.GetRequiredService<BundleIntervalSetTracker>(),
        () => sp.GetRequiredService<BundleOpenManager>().Current?.WorkingDirectory));
builder.Services.AddSingleton<EntityDiscoveryService>();
builder.Services.AddSingleton<EntityEventsService>();
builder.Services.AddSingleton<EntitySlowStateService>();
builder.Services.AddSingleton<EntityFastStateService>();
```
Add `EntityEndpoints.Map(app)` after `TraceEndpoints.Map(app)`.

**WebApiFixture.cs** — add to `CreateAsync`:
```csharp
builder.Services.AddSingleton<ParquetReader>();
builder.Services.AddSingleton<FastStateFileLocator>();
builder.Services.AddSingleton<EntityDiscoveryService>();
builder.Services.AddSingleton<EntityEventsService>();
builder.Services.AddSingleton<EntitySlowStateService>();
builder.Services.AddSingleton<EntityFastStateService>();
```
Add `EntityEndpoints.Map(app)` after existing `Map(app)` calls.

**ObserverFixture.cs** — same additions as WebApiFixture.cs.

#### Unit Tests (EntityEndpointsTests.cs)

Use `WebApiFixture` (TestServer, no real data). The fixture exposes `Broadcaster` and `Client`. For entity endpoints, the `LiveMultiIntervalReader` has no attached intervals (empty — uses `NullIntervalSetTracker`), so discovery returns empty lists.

Required tests (from spec §SC1–SC12):
1. `GET /api/entities?sessionId=...` — returns 404 when session missing (use `WebApiFixture`, session won't exist)
2. `GET /api/entities/{entityId}/fast-state/{topic}` with no `column` — returns 400, title contains "column"
3. `GET /api/entities/{entityId}/fast-state/{topic}?column=x&maxSamples=9` — returns 400, title contains "maxSamples"
4. `GET /api/entities/{entityId}/fast-state/{topic}?column=x&maxSamples=10001` — returns 400
5. DI wiring test: resolve all 6 services from Observer host — `sp.GetRequiredService<ParquetReader>()`, etc.

For tests requiring session/entity data, use `ObserverFixture` (full in-process Observer with real DuckDB).

#### Integration Test (EntityHistoryRoundTripTests.cs)

```csharp
[Fact]
public async Task EntityHistory_RoundTrip_EventsAndSlowState()
{
    // 1. Start session
    // 2. Push 20 events for "ent-X" with topic "combat.hit"
    // 3. Push 5 slow-state rows for "ent-X" with topic "pose"
    // 4. GET /api/entities?sessionId=... → assert entity appears, eventCount >= 20
    // 5. GET /api/entities/ent-X/events?from=...&to=... → assert 20 events
    // 6. GET /api/entities/ent-X/slow-state?from=...&to=... → assert byTopic["pose"] has 5 entries
    // 7. GET /api/entities/ent-X/events?...&limit=5 → assert truncated=true, events.length=5
}
```

Use `ObserverFixture` with `AppendStateAsync` for slow_state. Reference `WebApiQueryRoundTripTests.cs` for the push pattern.

**Required tests (from spec §SC11 integration):** The round-trip test per SC11.

---

## Key Patterns and Reference Files

| Pattern | Reference |
|---|---|
| Acquiring a pooled connection | `EventQueryService.cs:ListAsync` |
| `WithEventsCte` usage | `SessionQueryService.cs`, `EventQueryService.cs` |
| `BuildSlowStateUnionSql` | `LiveMultiIntervalReader.cs` (PooledMultiIntervalConnection) |
| DuckDB parameter binding | `EventQueryService.cs:ListAsync` |
| WallclockTime from DuckDB timestamp | `EventRecordMapper.cs:GetWallclock` |
| DTO record declarations | `Dtos.cs`, `TraceDtos.cs` |
| Endpoint handler pattern | `EventEndpoints.cs:HandleListAsync` |
| Integration test setup | `WebApiQueryRoundTripTests.cs` |
| Observer DI wiring | `ObserverHostBuilder.cs` (lines 155–162) |
| OfflineViewer DI wiring | `OfflineViewerHostBuilder.cs` (lines 60–66) |

---

## Deliverables

1. `src/Tracer.WebApi/Queries/EntityDiscoveryService.cs` (+ `EntitySummary` record)
2. `src/Tracer.WebApi/Queries/EntityEventsService.cs` (+ `EntityEventsResult` record)
3. `src/Tracer.WebApi/Queries/EntitySlowStateService.cs` (+ `SlowStateSample`, `EntitySlowStateResult` records)
4. `src/Tracer.WebApi/Queries/EntityFastStateService.cs` (+ `FastStateTopicSchema`, `EntityFastStateResult` records)
5. `src/Tracer.WebApi/Endpoints/EntityEndpoints.cs` (all 7 handlers + DTO mappers)
6. `src/Tracer.WebApi/Contracts/Dto/EntityDtos.cs` (all 9 DTOs)
7. `tests/Tracer.Tests.Unit/WebApi/EntityDiscoveryServiceTests.cs` (≥ 8 tests)
8. `tests/Tracer.Tests.Unit/WebApi/EntityEventsServiceTests.cs` (≥ 7 tests)
9. `tests/Tracer.Tests.Unit/WebApi/EntitySlowStateServiceTests.cs` (≥ 6 tests)
10. `tests/Tracer.Tests.Unit/WebApi/EntityFastStateServiceTests.cs` (≥ 7 tests)
11. `tests/Tracer.Tests.Unit/WebApi/EntityEndpointsTests.cs` (≥ 5 tests)
12. `tests/Tracer.Tests.Integration/EntityHistoryRoundTripTests.cs` (≥ 1 integration test)
13. Modified: `ObserverHostBuilder.cs`, `OfflineViewerHostBuilder.cs`, `WebApiFixture.cs`, `ObserverFixture.cs`

**Build must pass:** `dotnet build d:\Work\Tracer\Tracer.sln -c Release --no-incremental`  
**Tests must pass:** `dotnet test d:\Work\Tracer\tests\Tracer.Tests.Unit -c Release --no-build --filter "FullyQualifiedName!~Publish_ProducesExpectedLayout"`

---

## Technical Debt Notes

- **DT-026 (P2):** `FastStateFileLocator.GetAvailableTopicsForEntity` returns safe-encoded names. `EntityFastStateService.GetAvailableTopics` exposes these directly to the frontend. This is acceptable for now since the frontend uses the returned topic names only to call back to `GET /fast-state/{topic}` — which then calls `FastStateFileLocator.LocateFiles(topic, entityId)` using the original topic name from the client's request, bypassing the safe-encoding issue.
- **DT-023/DT-025:** Redundant index on `slow_state(instance_key, publish_wallclock)` — no action needed.
