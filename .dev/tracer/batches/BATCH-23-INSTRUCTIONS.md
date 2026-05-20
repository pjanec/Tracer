# BATCH-23 Instructions

**Tasks:** TRC-P5-002 (`/api/events` List & Aggregate Endpoints) and TRC-P5-003 (Extended SSE for Filtered Events)  
**Depends on:** TRC-P5-001 (committed `aaa5bde`) — `LiveMultiIntervalReader`, `IntervalSetTracker` are fully implemented.  
**Branch:** implement from current HEAD (`aaa5bde`).

---

## Relevant Architecture Context

### LiveMultiIntervalReader API
Located at `src/Tracer.Storage.DuckDB.MultiInterval/LiveMultiIntervalReader.cs`.

```csharp
// Acquire a pooled connection — must be disposed (await using) to return it to pool
await using var pooled = await _reader.AcquireAsync(ct);

// Build the UNION ALL SQL for all attached intervals, with optional WHERE pushdown
// Returns "SELECT NULL WHERE FALSE" if snapshot is empty (safe degenerate case)
string unionSql = pooled.BuildEventsUnionSql(whereClause: "WHERE publish_wallclock >= $from AND publish_wallclock < $to");

// Wrap any SQL in the multi-interval CTE (FROM events resolves to UNION ALL across intervals)
string fullSql = pooled.WithEventsCte("SELECT * FROM events WHERE topic = $topic");

// The underlying DuckDB connection
DuckDBConnection conn = pooled.Connection;
```

`BuildEventsUnionSql(whereClause)` returns a SQL fragment:
```sql
SELECT * FROM iv_20260519T140000Z.events WHERE publish_wallclock >= $from AND ...
UNION ALL
SELECT * FROM iv_20260519T150000Z.events WHERE publish_wallclock >= $from AND ...
```

`WithEventsCte(sql)` wraps `sql` in:
```sql
WITH events AS (<union-all-sql>) <sql>
```

### DuckDB parameter note
`DuckDBParameter` does **not** accept `DateTimeOffset`. Use `.UtcDateTime` when binding:
```csharp
cmd.Parameters.Add(new DuckDBParameter("from", query.From.ToDateTimeOffset().UtcDateTime));
cmd.Parameters.Add(new DuckDBParameter("to",   query.To.ToDateTimeOffset().UtcDateTime));
```

### Column names in the DuckDB events table
`event_id`, `trace_id`, `parent_event_id`, `sequence_number`, `publish_wallclock`, `receive_wallclock`, `publisher_node`, `subscriber_node`, `topic`, `entity_id`, `owning_player_id`, `scenario_phase`, `severity`, `notable_label`, `payload`

### Existing query service pattern
See `src/Tracer.WebApi/Queries/EventLookupService.cs` for the canonical pattern:
- Constructor takes `LiveMultiIntervalReader` via primary constructor syntax
- `await using var pooled = await _multiReader.AcquireAsync(ct);`
- `pooled.WithEventsCte(sql)` for SQL wrapping
- `pooled.Connection.CreateCommand()` for command creation
- Synchronous `cmd.ExecuteReader()` (not async) is what's used in existing services

### Existing services for reference
- `SessionQueryService` — has `ListAsync` and `GetAsync(sessionId, ct)` patterns (you need to add `GetAsync` to `SessionQueryService`; see below)
- `EventLookupService` — full mapper example showing column index ordering

### WebApiFixture (for unit tests)
Located at `src/Tracer.TestHarness/Observer/WebApiFixture.cs`. Used by all unit-level HTTP tests in `Tracer.Tests.Unit/WebApi/`. It hosts the WebApi with a no-op `LiveMultiIntervalReader` (not initialized). Tests that call query services backed by real DuckDB must use `ObserverFixture` from `src/Tracer.TestHarness/Observer/ObserverFixture.cs`.

### EventRecord fields (for SseFilter.Matches)
`EventRecord` is in `Tracer.Core.Records`. Relevant fields:
```csharp
public string NotableLabel { get; init; }   // null if not notable
public TopicName Topic { get; init; }        // .Value for string
public AgentId PublisherNode { get; init; }  // .Value for string  
public TraceId TraceId { get; init; }        // .Value is ulong; use .ToString("X16") for hex
public string? EntityId { get; init; }
public string? OwningPlayerId { get; init; }
public Severity? Severity { get; init; }     // .ToString() for "info"/"warning"/"error"
```

---

## TRC-P5-002 — `/api/events` List & Aggregate Endpoints

### 1. Add `GetSessionTimeRangeAsync` to `SessionQueryService`

Add a method to `src/Tracer.WebApi/Queries/SessionQueryService.cs` that looks up the time range for a session:

```csharp
/// <summary>
/// Returns the (Start, End) time range for the given session, or null if not found.
/// End is null for active sessions.
/// </summary>
public async Task<(WallclockTime Start, WallclockTime? End)?> GetSessionTimeRangeAsync(
    string sessionId, CancellationToken ct)
```

Implementation: query the `events` table for `topic = 'system.session_start'` and `topic = 'system.session_end'` matching the sessionId in payload JSON. Return the start `publish_wallclock` and end `publish_wallclock` (nullable). If no session_start found, return null.

You will need `using Tracer.Core.Time;` for `WallclockTime`.

### 2. Add new DTOs to `src/Tracer.WebApi/Contracts/Dto/`

Create a new file `src/Tracer.WebApi/Contracts/Dto/EventListDto.cs`:

```csharp
using System.Text.Json.Serialization;
using Tracer.WebApi.Contracts.Dto;

namespace Tracer.WebApi.Contracts.Dto;

public sealed record EventListDto
{
    public required IReadOnlyList<EventDto> Events { get; init; }
    public required long TotalMatching { get; init; }
    public required int Returned { get; init; }
    public required bool Truncated { get; init; }
}

public sealed record EventAggregateBucketGroupDto
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? GroupKey { get; init; }
    public required long Count { get; init; }
}

public sealed record EventAggregateBucketDto
{
    public required DateTimeOffset BucketStartUtc { get; init; }
    public required IReadOnlyList<EventAggregateBucketGroupDto> Groups { get; init; }
    public required long Total { get; init; }
}

public sealed record EventAggregateDto
{
    public required string BucketDuration { get; init; }
    public required IReadOnlyList<EventAggregateBucketDto> Buckets { get; init; }
}
```

Add JSON source generation for these types to `src/Tracer.WebApi/OpenApi/` (look at the existing `JsonContext` if one exists, or add a `[JsonSerializable]` attribute to the existing serializer context in the project).

### 3. Add `QueryPredicateBuilder`

Create `src/Tracer.WebApi/Queries/QueryPredicateBuilder.cs`:

This class builds the SQL WHERE clause and DuckDB parameter list for the event filter fields shared between `EventQueryService` and `EventAggregationService`.

Input: an object with these nullable filter fields (use an interface or pass the fields directly):
- `IReadOnlyList<string>? Topics`
- `IReadOnlyList<string>? Nodes`
- `string? TraceId` (hex string)
- `IReadOnlyList<string>? EntityIds`
- `IReadOnlyList<string>? PlayerIds`
- `IReadOnlyList<string>? Severities`
- `bool NotablesOnly`

Output: a `(string WhereSql, IReadOnlyList<string> BoundListParams)` tuple. The `WhereSql` starts with `WHERE` when any filter is active, or is empty string when no filters are specified.

**SQL pattern for list filters**: use `IN` with a DuckDB list literal. For example, topics:
```sql
AND topic IN (SELECT UNNEST($topics))
```
where `$topics` is bound as a `string[]`.

For `TraceId`: convert the hex string to `ulong` and bind as `ulong`:
```sql
AND trace_id = $traceId
```

For `NotablesOnly`:
```sql
AND notable_label IS NOT NULL
```

**BindParameters method**: takes a `DuckDBCommand` and binds all the filter parameters.

```csharp
public static class QueryPredicateBuilder
{
    // Returns (whereSql, paramNames) — paramNames are the names of list parameters bound
    public static (string WhereSql, IReadOnlyList<string> ParamNames) Build(IEventFilter filter);
    
    // Binds all filter parameters to the command
    public static void BindParameters(DuckDBCommand cmd, IEventFilter filter);
}

public interface IEventFilter
{
    IReadOnlyList<string>? Topics { get; }
    IReadOnlyList<string>? Nodes { get; }
    string? TraceId { get; }
    IReadOnlyList<string>? EntityIds { get; }
    IReadOnlyList<string>? PlayerIds { get; }
    IReadOnlyList<string>? Severities { get; }
    bool NotablesOnly { get; }
}
```

### 4. Add `EventQueryService`

Create `src/Tracer.WebApi/Queries/EventQueryService.cs`:

```csharp
namespace Tracer.WebApi.Queries;

public sealed class EventQueryService(LiveMultiIntervalReader reader, ILogger<EventQueryService> logger)
{
    public async Task<EventListResult> ListAsync(EventQuery query, CancellationToken ct);
}

public sealed record EventQuery : IEventFilter
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
    public required IReadOnlyList<EventDto> Events { get; init; }
    public required long TotalMatching { get; init; }
    public required int Returned { get; init; }
    public required bool Truncated { get; init; }
}
```

**`ListAsync` implementation:**
1. `await using var pooled = await _reader.AcquireAsync(ct);`
2. Build union SQL with time-range WHERE pushdown: `BuildEventsUnionSql("WHERE publish_wallclock >= $from AND publish_wallclock < $to")`
3. Build outer filter WHERE clause via `QueryPredicateBuilder.Build(query)`
4. Execute list query: `WITH unioned AS ({unionSql}) SELECT event_id, trace_id, parent_event_id, sequence_number, publish_wallclock, receive_wallclock, publisher_node, subscriber_node, topic, entity_id, owning_player_id, scenario_phase, severity, notable_label, payload FROM unioned {filterSql} ORDER BY publish_wallclock {(query.OrderDescending ? "DESC" : "ASC")} LIMIT $limit`
5. Execute count query: `WITH unioned AS ({unionSql}) SELECT COUNT(*) FROM unioned {filterSql}`
6. Map rows to `EventDto` using the same column indices as `EventLookupService`
7. Return `EventListResult` with `Truncated = events.Count < totalMatching`

**Important:** bind `$from` and `$to` using `.ToDateTimeOffset().UtcDateTime` (not `DateTimeOffset`). Bind `$limit` as `int`.

### 5. Add `EventAggregationService`

Create `src/Tracer.WebApi/Queries/EventAggregationService.cs`:

```csharp
namespace Tracer.WebApi.Queries;

public sealed class EventAggregationService(LiveMultiIntervalReader reader, ILogger<EventAggregationService> logger)
{
    public async Task<AggregateResult> AggregateAsync(AggregateQuery query, CancellationToken ct);
}

public sealed record AggregateQuery : IEventFilter
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

**`AggregateAsync` implementation:**
1. Validate `BucketDuration` — allowed values: `"100ms"`, `"1s"`, `"5s"`, `"30s"`, `"1m"`, `"5m"`, `"30m"`, `"1h"`. Throw `ArgumentException` if not in list.
2. Map to DuckDB interval: `"100ms"→"100 milliseconds"`, `"1s"→"1 second"`, `"5s"→"5 seconds"`, `"30s"→"30 seconds"`, `"1m"→"1 minute"`, `"5m"→"5 minutes"`, `"30m"→"30 minutes"`, `"1h"→"1 hour"`.
3. Group-by expression: `Node→"publisher_node"`, `Topic→"topic"`, `Severity→"severity"`, `None→"NULL"`.
4. SQL:
```sql
WITH unioned AS ({BuildEventsUnionSql("WHERE publish_wallclock >= $from AND publish_wallclock < $to")}),
filtered AS (SELECT * FROM unioned {filterSql})
SELECT
    time_bucket(INTERVAL '{interval}', publish_wallclock) AS bucket_start,
    {groupByExpr} AS group_key,
    COUNT(*) AS cnt
FROM filtered
GROUP BY bucket_start, group_key
ORDER BY bucket_start, group_key;
```
5. Read into `SortedDictionary<DateTime, List<AggregateGroup>>` (use `reader.GetDateTime(0)` converted to `DateTimeOffset` via `new DateTimeOffset(dt, TimeSpan.Zero)`).
6. Return `AggregateResult` with flattened buckets.

### 6. Extend `EventEndpoints`

Replace the contents of `src/Tracer.WebApi/Endpoints/EventEndpoints.cs` to add `GET /api/events` and `GET /api/events/aggregate`:

```csharp
public static void Map(WebApplication app)
{
    // Phase 3 — unchanged
    app.MapGet("/api/events/{eventId}", HandleGetByIdAsync)
        .WithName("GetEvent").WithOpenApi();
    
    // Phase 5
    app.MapGet("/api/events", HandleListAsync)
        .WithName("ListEvents").WithOpenApi();
    
    app.MapGet("/api/events/aggregate", HandleAggregateAsync)
        .WithName("AggregateEvents").WithOpenApi();
}
```

**`HandleListAsync` signature:**
```csharp
internal static async Task<Results<Ok<EventListDto>, ProblemHttpResult>> HandleListAsync(
    [FromQuery] string? sessionId,
    [FromQuery] DateTimeOffset? from,
    [FromQuery] DateTimeOffset? to,
    [FromQuery] string[]? topic,
    [FromQuery] string[]? node,
    [FromQuery] string? traceId,
    [FromQuery] string[]? entityId,
    [FromQuery] string[]? playerId,
    [FromQuery] string[]? severity,
    [FromQuery] bool notablesOnly,
    [FromQuery] int limit,
    [FromQuery] string? orderBy,
    [FromServices] EventQueryService eventSvc,
    [FromServices] SessionQueryService sessionSvc,
    CancellationToken ct)
```

Defaults: `limit` defaults to `5000` (use `= 5000` in signature or check for 0), `orderBy` defaults to `null` (means ascending).

Validation:
- If `sessionId` is null/empty → 400 with problem detail "sessionId is required"
- If `limit < 1 || limit > 5000` → 400 with problem detail "limit must be 1..5000"
- Look up session time range via `sessionSvc.GetSessionTimeRangeAsync(sessionId, ct)`
- If session not found → 404

Build `EventQuery` with `From = from?.let(WallclockTime.FromDateTimeOffset) ?? sessionStart`, `To = to?.let(WallclockTime.FromDateTimeOffset) ?? sessionEnd ?? WallclockTime.Now`.

Map result to `EventListDto` and return `TypedResults.Ok(dto)`.

**`HandleAggregateAsync` signature:**
```csharp
internal static async Task<Results<Ok<EventAggregateDto>, ProblemHttpResult>> HandleAggregateAsync(
    [FromQuery] string? sessionId,
    [FromQuery] DateTimeOffset? from,
    [FromQuery] DateTimeOffset? to,
    [FromQuery] string? bucketDuration,
    [FromQuery] string? groupBy,
    [FromQuery] string[]? topic,
    [FromQuery] string[]? node,
    [FromQuery] string? traceId,
    [FromQuery] string[]? entityId,
    [FromQuery] string[]? playerId,
    [FromQuery] string[]? severity,
    [FromQuery] bool notablesOnly,
    [FromServices] EventAggregationService aggSvc,
    [FromServices] SessionQueryService sessionSvc,
    CancellationToken ct)
```

Validation:
- `sessionId` required → 400
- `bucketDuration` required → 400
- Try to parse `bucketDuration`; if invalid → 400

Use try/catch around `AggregateAsync` to catch `ArgumentException` → 400.

Map result to `EventAggregateDto` and return `TypedResults.Ok(dto)`.

### 7. Register new services in `WebApiFixture` and `ObserverHostBuilder`

**`WebApiFixture`** (`src/Tracer.TestHarness/Observer/WebApiFixture.cs`):
Add these registrations:
```csharp
builder.Services.AddSingleton<EventQueryService>();
builder.Services.AddSingleton<EventAggregationService>();
```
Also map the new endpoints in the `Map` calls section:
The endpoints are already called via `EventEndpoints.Map(app)` — no change needed since `HandleListAsync` and `HandleAggregateAsync` are wired in `EventEndpoints.Map`.

**`ObserverHostBuilder`** (`src/Tracer.Observer/ObserverHostBuilder.cs`):
Add:
```csharp
services.AddSingleton<EventQueryService>();
services.AddSingleton<EventAggregationService>();
```

---

## TRC-P5-003 — Extended SSE for Filtered Events

### 8. Replace `SseFilter` with extended version

Replace `src/Tracer.WebApi/Streaming/SseFilter.cs` entirely:

```csharp
using Tracer.Core.Records;

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
        if (NotablesOnly && ev.NotableLabel is null) return false;
        if (Topics is not null && !Topics.Contains(ev.Topic.Value)) return false;
        if (Nodes is not null && !Nodes.Contains(ev.PublisherNode.Value)) return false;
        if (TraceId is not null && ev.TraceId.Value.ToString("X16") != TraceId) return false;
        if (EntityIds is not null && (ev.EntityId is null || !EntityIds.Contains(ev.EntityId))) return false;
        if (PlayerIds is not null && (ev.OwningPlayerId is null || !PlayerIds.Contains(ev.OwningPlayerId))) return false;
        if (Severities is not null && (ev.Severity is null || !Severities.Contains(ev.Severity.Value.ToString()))) return false;
        return true;
    }
}
```

**Important:** The existing `SseEndpoints.cs` creates `new SseFilter(NotablesOnly: notablesOnly ?? true, SessionId: sessionId)`. This uses positional/named constructor syntax which won't compile after the change since `SseFilter` is now an init-only record. Update the existing `/api/live/notables` handler in `SseEndpoints.cs` to:
```csharp
var filter = new SseFilter
{
    NotablesOnly = notablesOnly ?? true,
    SessionId = sessionId,
};
```

### 9. Update `SseConnection.Enqueue` to use `Filter.Matches`

In `src/Tracer.WebApi/Streaming/SseConnection.cs`, replace the inline filter logic in `Enqueue` with:
```csharp
public void Enqueue(EventRecord ev)
{
    ArgumentNullException.ThrowIfNull(ev);
    if (!Filter.Matches(ev)) return;
    
    if (_channel.Reader.Count >= _bufferSize)
        Interlocked.Increment(ref _dropCount);
    _channel.Writer.TryWrite(ev);
}
```

The session filtering currently in `Enqueue` (checking `PayloadJson`) is moved into `SseFilter.Matches`. The `SessionId` filter in `SseFilter.Matches` can keep the same logic (best-effort payload JSON check) or be removed if it's not part of the Phase 5 spec. Looking at the design (§4.7): "SessionId filter: Phase 5 simplification — broadcast all events to all session subscribers". Keep `SessionId` as an opt-in field but don't add filtering on it in `Matches` for now — just match all events regardless of sessionId.

Actually, checking the existing `SseConnection.Enqueue`: it DOES currently filter by `SessionId` via payload JSON. Since the design says Phase 5 simplification, keep the existing SessionId behavior but move it into `Matches`. Or, per the design comment: simply don't filter on SessionId in `Matches` — the frontend handles it.

**Decision**: Keep `SessionId` in `SseFilter` for backward compatibility but don't filter on it in `Matches` (matching the design comment). Remove the old sessionId filtering from `SseConnection.Enqueue`.

### 10. Add `GET /api/live/events` to `SseEndpoints`

In `src/Tracer.WebApi/Endpoints/SseEndpoints.cs`, add a new handler in the `Map` method:

```csharp
app.MapGet("/api/live/events", async (
    HttpContext context,
    [FromQuery] string? sessionId,
    [FromQuery] string[]? topic,
    [FromQuery] string[]? node,
    [FromQuery] string? traceId,
    [FromQuery] string[]? entityId,
    [FromQuery] string[]? playerId,
    [FromQuery] string[]? severity,
    [FromQuery] bool notablesOnly,
    SseConnectionManager connectionManager,
    SseStreamingOptions options,
    CancellationToken ct) =>
{
    var filter = new SseFilter
    {
        SessionId = sessionId,
        Topics = topic?.Length > 0 ? new HashSet<string>(topic, StringComparer.Ordinal) : null,
        Nodes = node?.Length > 0 ? new HashSet<string>(node, StringComparer.Ordinal) : null,
        TraceId = traceId,
        EntityIds = entityId?.Length > 0 ? new HashSet<string>(entityId, StringComparer.Ordinal) : null,
        PlayerIds = playerId?.Length > 0 ? new HashSet<string>(playerId, StringComparer.Ordinal) : null,
        Severities = severity?.Length > 0 ? new HashSet<string>(severity, StringComparer.Ordinal) : null,
        NotablesOnly = notablesOnly,
    };
    
    // ... same SSE response pattern as /api/live/notables ...
    // Map events to EventDto using DtoMappers.ToDto(ev)
    // Serialize with _sseJsonOptions (camelCase)
}).WithName("GetLiveEvents").WithOpenApi();
```

The SSE streaming loop (headers, heartbeat task, foreach, drain) is identical to `/api/live/notables`. Extract the shared logic into a `private static async Task StreamEventsAsync(...)` helper to avoid duplication. Or just copy the pattern — your choice, but do NOT refactor the existing `/api/live/notables` handler.

For the event DTO, use `DtoMappers.ToDto(ev)` which maps `EventRecord → EventDto`.

---

## Unit Tests

All unit tests go in `tests/Tracer.Tests.Unit/WebApi/`.

### 11. `EventQueryServiceTests.cs`

Create `tests/Tracer.Tests.Unit/WebApi/EventQueryServiceTests.cs`.

This test class uses `ObserverFixture` from `Tracer.TestHarness` to push real events into DuckDB and verify query results.

```csharp
public sealed class EventQueryServiceTests : IAsyncDisposable
{
    private ObserverFixture _fixture = null!;
    private EventQueryService _svc = null!;

    private async Task InitAsync(CancellationToken ct = default)
    {
        _fixture = await ObserverFixture.CreateAsync(ct: ct);
        _svc = new EventQueryService(
            _fixture.MultiReader,
            NullLogger<EventQueryService>.Instance);
    }
    // ...
}
```

Look at `ObserverFixture` to understand how to `PushAsync`, `ForceRotationAsync`, and access `MultiReader`. The fixture is in `src/Tracer.TestHarness/Observer/ObserverFixture.cs`.

**Required test methods (all must pass):**

1. `ListAsync_NoFilter_ReturnsAllEventsInTimeOrder` — push 10 events, call ListAsync with no filter (cover full time range), verify 10 events returned in ascending `OccurredAtUtc` order.

2. `ListAsync_TimeRange_ExcludesEventsOutsideRange` — push events at T1, T2, T3 (spaced apart). Query `[T1, T3)`. Verify T1 and T2 present, T3 absent (exclusive upper bound).

3. `ListAsync_TopicFilter_ReturnsOnlyMatchingTopics` — push 5 events on `"test.alpha"`, 5 on `"test.beta"`. Filter `Topics = ["test.alpha"]`. Verify 5 returned, all on `"test.alpha"`.

4. `ListAsync_MultiTopicFilter_OrsWithinFilter` — push events on `"test.alpha"`, `"test.beta"`, `"test.gamma"`. Filter `Topics = ["test.alpha", "test.beta"]`. Verify alpha + beta present, gamma absent.

5. `ListAsync_MultipleFilterTypes_AndsAcrossFilters` — push events: (topic=A, severity=error), (topic=A, severity=info), (topic=B, severity=error). Filter `Topics=["A"], Severities=["error"]`. Verify only the (A, error) event returned.

6. `ListAsync_TraceIdFilter_ReturnsOnlyThatTrace` — push 3 events with different trace IDs. Filter by one specific trace ID hex. Verify only that event returned.

7. `ListAsync_Limit_TruncatesAndSetsTruncatedFlag` — push 10 events. Query with `Limit=3`. Verify `Returned=3`, `TotalMatching=10`, `Truncated=true`.

8. `ListAsync_OrderDescending_ReturnsByNewestFirst` — push 5 events in order. Query with `OrderDescending=true`. Verify `result.Events[0].OccurredAtUtc` > `result.Events[4].OccurredAtUtc`.

9. `ListAsync_EmptyResult_ReturnsTotalMatchingZero` — query with a topic filter matching nothing. Verify `TotalMatching=0`, `Events` empty, `Truncated=false`.

10. `ListAsync_NotablesOnly_ExcludesNonNotables` — push 5 events with `NotableLabel="hit"`, 5 without. Query with `NotablesOnly=true`. Verify 5 returned.

**Helper**: Create a `MakeEvent` helper that builds an `EventRecord` with sensible defaults, accepting optional overrides for `topic`, `traceId`, `severity`, `notableLabel`. See `ObserverFixture.PushAsync` for the expected input type.

### 12. `EventAggregationServiceTests.cs`

Create `tests/Tracer.Tests.Unit/WebApi/EventAggregationServiceTests.cs`.

Uses `ObserverFixture` for real DuckDB queries.

**Required test methods:**

1. `AggregateAsync_OneHourAt5sBuckets_ReturnsExpectedBucketCount` — push events covering a 1-hour range (spaced throughout). Query with `bucketDuration="5s"`. Verify `Buckets.Count <= 720`.

2. `AggregateAsync_EmptyRange_ReturnsEmptyBuckets` — query a time range with no events. Verify `Buckets.Count == 0`.

3. `AggregateAsync_GroupByNone_EachBucketHasSingleGroupWithNullKey` — push events. Query with `GroupBy=None`. Verify every bucket has exactly 1 group with `GroupKey == null`.

4. `AggregateAsync_GroupByNode_GroupsArePublisherNodes` — push events from nodes "node-A" and "node-B". Query with `GroupBy=Node`. Verify the group keys in each bucket are "node-A" and/or "node-B".

5. `AggregateAsync_FilterAppliedBeforeAggregation_ExcludesNonMatchingEvents` — push 10 events on topic "keep" and 10 on topic "discard". Query with `Topics=["keep"]`. Verify total count across all buckets sums to 10.

6. `AggregateAsync_BucketTotalsEqualSumOfGroupCounts` — push events. For every bucket: `bucket.Total == bucket.Groups.Sum(g => g.Count)`.

7. `AggregateAsync_InvalidBucketDuration_ThrowsArgumentException` — call `AggregateAsync` with `BucketDuration="invalid"`. Verify `ArgumentException` thrown.

### 13. `EventEndpointsListTests.cs`

Create `tests/Tracer.Tests.Unit/WebApi/EventEndpointsListTests.cs`.

Uses `WebApiFixture` (HTTP-level tests, no real DuckDB).

**Required test methods:**

1. `HandleListAsync_NoFilter_Returns200WithEventList` — GET `/api/events?sessionId=test`. Expect 200 (even if empty result from no-op reader). Deserialize JSON and verify the response has `events`, `totalMatching`, `returned`, `truncated` fields.

2. `HandleListAsync_LimitZero_Returns400ProblemDetails` — GET `/api/events?sessionId=test&limit=0`. Expect 400. Response body should be a problem details JSON.

3. `HandleListAsync_LimitOverMax_Returns400ProblemDetails` — GET `/api/events?sessionId=test&limit=9999`. Expect 400.

4. `HandleListAsync_UnknownSessionId_Returns404ProblemDetails` — GET `/api/events?sessionId=nonexistent-session`. Expect 404.

   *Note:* Since `WebApiFixture` uses a no-op `LiveMultiIntervalReader`, `GetSessionTimeRangeAsync` will return null for any session. This verifies the 404 code path.

5. `HandleListAsync_MultipleTopicParams_PassedAsListToService` — GET `/api/events?sessionId=test&topic=a&topic=b`. Expect the response to be 200 or 404 (not 400). The point is to verify that multiple `topic` query params are accepted without error (they form a list).

### 14. `EventEndpointsAggregateTests.cs`

Create `tests/Tracer.Tests.Unit/WebApi/EventEndpointsAggregateTests.cs`.

Uses `WebApiFixture`.

**Required test methods:**

1. `HandleAggregateAsync_ValidRequest_Returns200WithAggregateDto` — GET `/api/events/aggregate?sessionId=test&bucketDuration=5s`. Expect 200 with `bucketDuration` and `buckets` in response JSON.

   *Note:* The no-op reader means no real events, so `buckets` will be empty. That's fine for endpoint tests.

2. `HandleAggregateAsync_InvalidBucketDuration_Returns400ProblemDetails` — GET `/api/events/aggregate?sessionId=test&bucketDuration=invalid`. Expect 400.

3. `HandleAggregateAsync_MissingSessionId_Returns400` — GET `/api/events/aggregate?bucketDuration=5s` (no sessionId). Expect 400.

---

## SSE Unit Tests

### 15. `SseFilterTests.cs`

Create `tests/Tracer.Tests.Unit/WebApi/SseFilterTests.cs`.

Pure unit tests — no fixture needed, just construct `SseFilter` and `EventRecord` instances.

Use the same `EventRecord`-building helper pattern as in `SseEndpointTests.cs` (see `tests/Tracer.Tests.Unit/WebApi/SseEndpointTests.cs` for reference).

**Required test methods:**

1. `Matches_NotablesOnly_ExcludesEventsWithoutLabel` — `SseFilter { NotablesOnly = true }`. Event with `NotableLabel = null` → `Matches` returns `false`. Event with `NotableLabel = "hit"` → `true`.

2. `Matches_TopicFilter_ExcludesNonMatchingTopic` — `SseFilter { Topics = new HashSet<string> { "combat.fire" } }`. Event on `"combat.fire"` → true. Event on `"movement.update"` → false.

3. `Matches_MultipleTopics_MatchesAnyListed` — filter with `Topics = { "alpha", "beta" }`. Event on `"alpha"` → true. Event on `"beta"` → true. Event on `"gamma"` → false.

4. `Matches_NodeFilter_ExcludesNonMatchingNode` — `SseFilter { Nodes = { "node-A" } }`. Event from `"node-A"` → true. Event from `"node-B"` → false.

5. `Matches_TraceIdFilter_ExcludesNonMatchingTrace` — filter by specific `TraceId = "000000000000000A"`. Event with that trace ID → true. Event with different trace ID → false.

6. `Matches_EntityIdFilter_ExcludesNonMatchingEntityId` — `SseFilter { EntityIds = { "vehicle:1" } }`. Event with `EntityId = "vehicle:1"` → true. Event with `EntityId = "vehicle:2"` → false. Event with `EntityId = null` → false.

7. `Matches_PlayerIdFilter_ExcludesNonMatchingPlayerId` — similar pattern for `PlayerIds`.

8. `Matches_SeverityFilter_ExcludesNonMatchingSeverity` — `SseFilter { Severities = { "error" } }`. Event with `Severity = Severity.Error` → true. Event with `Severity = Severity.Info` → false. Event with `Severity = null` → false.

9. `Matches_MultipleFilterTypesCompose_RequiresAllToMatch` — filter `Topics = { "A" }, Severities = { "error" }`. Event on topic "A" with severity "info" → false (severity doesn't match). Event on topic "A" with severity "error" → true.

10. `Matches_EmptyFilter_AllEventsMatch` — `new SseFilter()` (all null/false). Various events all return `true`.

### 16. `LiveEventBroadcasterTests.cs`

Create `tests/Tracer.Tests.Unit/WebApi/LiveEventBroadcasterTests.cs`.

**Required test methods:**

1. `Publish_ConnectionWithTopicFilter_OnlyDeliverMatchingEvents`

   Setup: Create a `SseConnectionManager` with default options. Register two connections:
   - Connection A: `SseFilter { Topics = { "match.topic" } }`
   - Connection B: `new SseFilter()` (no filter — accepts all)
   
   Get `LiveEventBroadcaster` via its constructor `(connectionManager, logger)` and start it with `ExecuteAsync(ct)` running in the background.
   
   Publish 3 events: one with topic `"match.topic"`, two with topic `"other.topic"`.
   
   Wait briefly (e.g., `Task.Delay(100)`).
   
   Read from connection A's channel and verify only 1 event received (the matching topic).
   Read from connection B's channel and verify all 3 events received.
   
   *Note:* Since `SseConnection` is sealed, access its channel indirectly via `ReadAsync` with a short-timeout CancellationToken, or expose a test accessor. Look at how existing `SseEndpointTests.cs` reads events from connections.

2. `Publish_TenClientsAtThousandEventsPerSecond_NoDropsOrCrashes`

   Create 10 SSE connections (no filter). Create broadcaster. Start broadcaster background loop. Publish 1000 events in rapid succession. After publishing, wait 500ms. Cancel the broadcaster.
   
   Verify: no exceptions thrown. Each connection's `DropCount` is reasonable (may have drops with BoundedChannel but no crashes). Verify total events received across all connections >= some threshold (e.g., 80% of 10,000).

### 17. `LiveEventStreamEndpointsTests.cs`

Create `tests/Tracer.Tests.Unit/WebApi/LiveEventStreamEndpointsTests.cs`.

Uses `WebApiFixture`.

**Required test methods:**

1. `GetLiveEvents_ContentTypeIsTextEventStream` — GET `/api/live/events`. Verify `Content-Type: text/event-stream`.

2. `GetLiveEvents_WithTopicFilter_OnlyMatchingEventsDelivered`

   Setup: `await CreateFixtureAsync()`. Start a long-running GET `/api/live/events?topic=match.topic` with `HttpCompletionOption.ResponseHeadersRead`.
   
   Inject events via `fixture.Broadcaster.Publish(ev)` — one matching topic, one non-matching.
   
   Read the SSE stream for 200ms. Verify only the matching event appears in the SSE data.

3. `GetLiveEvents_XAccelBufferingNoCache_HeadersPresent` — GET `/api/live/events`. Verify response headers include `X-Accel-Buffering: no` and `Cache-Control: no-cache`.

---

## Success Condition Notes

### SC-5 (TRC-P5-002): TypeScript client regeneration
After implementing the endpoints, run NSwag to regenerate the TypeScript client:
```
cd src/Tracer.WebApi
nswag run nswag.json
```
This regenerates `tracer-viewer/src/api/tracerApiClient.ts` with `listEvents` and `aggregateEvents` methods. **Commit the regenerated file.**

If NSwag is not installed, install it: `dotnet tool install NSwag.Console --global` or use the local tool version.

Check if a `.config/dotnet-tools.json` or `dotnet-tools.json` exists for the local tool.

### SC-7 (TRC-P5-002): Performance test
Add a performance test to the existing `tests/Tracer.Tests.Integration/` project (or create a new file `PerformanceTests.cs`):

```csharp
// PerformanceTests.EventList_1MEventSession_Under300ms
// Seeds 1M events using ObserverFixture (with multiple rotations), 
// measures GET /api/events?sessionId=X p95 < 300ms
```

This test can be marked with `[Trait("Category", "Performance")]` and may be slow. It should be skipped in normal test runs and only run explicitly. Use `[Fact(Skip = "Performance test — run explicitly")]` or a custom `[FactIf]` attribute if available in the project. Check how other integration tests are tagged.

**Alternatively**, if seeding 1M events is too complex for this batch, skip the performance test and note it as an outstanding item. The other 7 success conditions can still be verified.

### SC-6 (TRC-P5-003): End-to-end SSE latency test
Add `SseEvent_ArrivesAtClientWithinBudget` to `tests/Tracer.Tests.Integration/`:
- Start SSE connection
- Record timestamp T1 just before `Broadcaster.Publish(ev)`
- Read SSE frame, record T2
- Verify `T2 - T1 < 50ms` (median budget)

This can use `ObserverFixture` and `WebApiFixture` together, or just `WebApiFixture` with direct `Broadcaster.Publish` injection.

---

## Build & Test Verification

After implementing all changes, verify:

```bash
# Build the solution
dotnet build Tracer.sln --configuration Release

# Run unit tests
dotnet test tests/Tracer.Tests.Unit/Tracer.Tests.Unit.csproj --configuration Release

# Run integration tests (excluding the known flaky PDB-lock test)
dotnet test tests/Tracer.Tests.Integration/Tracer.Tests.Integration.csproj --configuration Release --filter "FullyQualifiedName!~Publish_ProducesExpectedLayout"
```

All existing tests must continue to pass (274 unit, 72 integration at minimum).

---

## Files to Create / Modify

### New files:
- `src/Tracer.WebApi/Contracts/Dto/EventListDto.cs`
- `src/Tracer.WebApi/Queries/QueryPredicateBuilder.cs`
- `src/Tracer.WebApi/Queries/EventQueryService.cs`
- `src/Tracer.WebApi/Queries/EventAggregationService.cs`
- `tests/Tracer.Tests.Unit/WebApi/EventQueryServiceTests.cs`
- `tests/Tracer.Tests.Unit/WebApi/EventAggregationServiceTests.cs`
- `tests/Tracer.Tests.Unit/WebApi/EventEndpointsListTests.cs`
- `tests/Tracer.Tests.Unit/WebApi/EventEndpointsAggregateTests.cs`
- `tests/Tracer.Tests.Unit/WebApi/SseFilterTests.cs`
- `tests/Tracer.Tests.Unit/WebApi/LiveEventBroadcasterTests.cs`
- `tests/Tracer.Tests.Unit/WebApi/LiveEventStreamEndpointsTests.cs`

### Modified files:
- `src/Tracer.WebApi/Streaming/SseFilter.cs` — replace entirely
- `src/Tracer.WebApi/Streaming/SseConnection.cs` — update `Enqueue` to call `Filter.Matches`
- `src/Tracer.WebApi/Endpoints/EventEndpoints.cs` — add list and aggregate handlers
- `src/Tracer.WebApi/Endpoints/SseEndpoints.cs` — add `/api/live/events`, fix `SseFilter` construction
- `src/Tracer.WebApi/Queries/SessionQueryService.cs` — add `GetSessionTimeRangeAsync`
- `src/Tracer.Observer/ObserverHostBuilder.cs` — register `EventQueryService` and `EventAggregationService`
- `src/Tracer.TestHarness/Observer/WebApiFixture.cs` — register `EventQueryService` and `EventAggregationService`

### OpenAPI / TypeScript client:
- Run `nswag run nswag.json` from `src/Tracer.WebApi/`
- Commit the updated `tracer-viewer/src/api/tracerApiClient.ts`

---

## Batch Report Format

Submit a batch report in `.dev/tracer/reports/BATCH-23-REPORT.md` with:
- Status: COMPLETE / PARTIAL / BLOCKED
- Files created/modified (with line counts)
- Test counts: unit / integration
- Build output (errors: 0, warnings: 0)
- Outstanding issues (if any)
- Notes on any design deviations
