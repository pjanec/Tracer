# BATCH-07: Corrective Fixes + REST Endpoints + SSE Live Streaming

**Batch Number:** BATCH-07  
**Tasks:** Corrective (DT-010, DT-011, DT-012), TRC-P3-003, TRC-P3-004, TRC-P3-005  
**Phase:** Phase 3 — TracerObserver, Web API, Vue SPA, Session Browser & Scenario View  
**Estimated Effort:** 18–20 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-06 (Observer + WebApi infrastructure)

---

## 📋 Onboarding & Workflow

### Developer Instructions

This batch does three things:
1. **Corrective tasks** — Fix hollow tests and wrong stub names from BATCH-06 (P1 debt items DT-010, DT-011, DT-012). Read BATCH-06 review first.
2. **REST Endpoints** — Implement `SessionEndpoints`, `TopologyEndpoints`, `ScenarioEndpoints`, `EventEndpoints`, and their query services (TRC-P3-003, TRC-P3-004).
3. **SSE Live Streaming** — Implement `LiveEventBroadcaster`, `SseConnectionManager`, `SseConnection`, `SseFilter`, and `SseEndpoints` (TRC-P3-005).

After this batch, the entire backend is functional: FakeNode → Observer → REST API + SSE.

### Required Reading (IN ORDER)

1. **Workflow:** `.github/skills/developer/SKILL.md`
2. **BATCH-06 Review:** `.dev/tracer/reviews/BATCH-06-REVIEW.md` — understand what needs fixing and why
3. **Task Definitions:** `docs/TASK-DETAIL.md` — see TRC-P3-003, TRC-P3-004, TRC-P3-005
4. **Phase 3 Design:** `docs/tracer_phase3_design.md` — §4 (Endpoints, DTOs, Query Services), §5 (SSE)

### Source Code Location

- **Endpoints/DTOs/Services:** `src/Tracer.WebApi/`
- **Tests:** `tests/Tracer.Tests.Unit/Observer/`, `tests/Tracer.Tests.Unit/WebApi/`
- **Integration stubs:** `tests/Tracer.Tests.Integration/`

### Run Tests

```powershell
cd d:\Work\Tracer
dotnet test Tracer.sln --configuration Release
```

### Report Submission

`.dev/tracer/reports/BATCH-07-REPORT.md`

---

## ⚡ MANDATORY: No Stopping

Fix every failure before writing the report. No permission needed to run tests, fix compiler errors, or rewrite passing-but-hollow tests. The batch is done only when `dotnet test Tracer.sln --configuration Release` exits code 0.

---

## 🔄 MANDATORY WORKFLOW

1. Corrective tasks first (fix hollow tests, rename stubs)
2. Implement DTOs in `Tracer.WebApi/Contracts/Dto/`
3. Implement `DtoMappers` in `Tracer.WebApi/Contracts/Mapping/`
4. Implement query services (`SessionQueryService`, `TopologyQueryService`, `ScenarioQueryService`, `EventLookupService`)
5. Implement endpoints (`SessionEndpoints`, `TopologyEndpoints`, `ScenarioEndpoints`, `EventEndpoints`)
6. Write unit tests for endpoints/DTOs — run — fix
7. Implement `SseConnectionManager`, `SseConnection`, `SseEndpoints`, full `LiveEventBroadcaster`
8. Write SSE unit tests — run — fix
9. Add DtoMappingTests
10. Final `dotnet test Tracer.sln --configuration Release` → 0 failures → write report

---

## 🛠️ Corrective Task 0 — Fix BATCH-06 Test Quality Failures

### DT-010: Rewrite `ObserverIngestionTests` to actually use the pipeline

**File:** `tests/Tracer.Tests.Unit/Observer/ObserverIngestionTests.cs`

**The problem:** The current tests bypass `ObserverIngestionPipeline.RunAsync` entirely. Replace all 6 tests with proper ones that create a fake `IDiagnosticDataSource`, call `pipeline.RunAsync(ct)`, and assert on observable outputs.

**How to write a proper fake data source:**

```csharp
// Helper: create a fake IDiagnosticDataSource that yields a fixed sequence
private sealed class FixedDataSource(IEnumerable<DiagnosticRecord> records) : IDiagnosticDataSource
{
    public async IAsyncEnumerable<DiagnosticRecord> ReadAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var r in records)
        {
            ct.ThrowIfCancellationRequested();
            yield return r;
            await Task.Yield(); // allow pipeline to process one record
        }
    }
}
```

**How to count broadcaster publishes:**

```csharp
// Instead of using the real LiveEventBroadcaster (which is a BackgroundService),
// the pipeline should accept it by reference and you can count via a subclass or wrapper.
// If LiveEventBroadcaster.Publish is a virtual method, override it in a test subclass.
// If it is sealed/non-virtual, extract a counting wrapper or use the existing PublishedCount property if present.
// Check LiveEventBroadcaster.cs for the actual API — don't guess.
```

**How to verify DuckDB count:**

After `pipeline.RunAsync(ct)` returns, use `DuckDbStorageReader` to open the interval's `events.duckdb` and call `CountEventsAsync(EventFilter.All, default)`.

**Required test replacements (same names as in TRC-P3-001 SC10):**

1. `Records_WrittenToCurrentWriter` — Open rotator with `OpenCurrentAsync`. Create `FixedDataSource` with 10 `EventRecord`s. Build pipeline with this source. Call `pipeline.RunAsync(default)`. Assert: `StateReporter.Snapshot().IngestedTotal == 10` AND open DuckDB reader at `rotator.CurrentDirectory.EventsDbPath` and verify `CountEventsAsync == 10`.

2. `Events_PublishedToLiveBroadcaster` — Create `FixedDataSource` with 3 `EventRecord`s. Build pipeline. Call `RunAsync`. Assert that the broadcaster received exactly 3 publish calls. You must track this via a counting mechanism on the broadcaster (subclass override, test field, or checking a published list).

3. `SlowState_WrittenButNotBroadcast` — Create `FixedDataSource` with 2 slow-rate `StateSampleRecord`s. Call `RunAsync`. Assert: broadcaster publish count == 0; `IngestedTotal == 2`.

4. `FastState_WrittenViaAppendFastStateAsync` — Create `FixedDataSource` with 1 fast-rate `StateSampleRecord`. Call `RunAsync`. Assert: `IngestedTotal == 1` (the record was processed, not dropped); no exception thrown.

5. `Cancellation_PropagatesCleanly` — Create a blocking `IDiagnosticDataSource` that yields one record then waits indefinitely (blocks on `ct.WaitHandle` or `await Task.Delay(Timeout.Infinite, ct)`). Cancel the token mid-stream. Assert: `RunAsync` completes without throwing `OperationCanceledException` (pipeline catches it internally and returns).

6. `WriteFailure_IncrementsDropCounter_PipelineContinues` — Create a faulting `IDiagnosticStorageWriter` (subclass of real `DuckDbStorageWriter` or a fake `IDiagnosticStorageWriter` implementation) that throws on the first `AppendEventAsync` call. Create `FixedDataSource` with 3 `EventRecord`s. Build a pipeline wired with this faulting writer (you may need to inject the writer directly or use a patched rotator). Call `RunAsync`. Assert: `StateReporter.Snapshot().DroppedTotal == 1` AND `IngestedTotal == 2` (first fails, second and third succeed).

> **Implementation note on injecting a faulting writer:** `ObserverIngestionPipeline.ProcessOneAsync` calls `_rotator.CurrentWriter`. Since `IntervalRotator.CurrentWriter` is the `DuckDbStorageWriter` opened by `OpenCurrentAsync`, you have two options: (a) if `IDiagnosticStorageWriter` is the actual type used, inject a fake writer by testing via a seam you add; (b) alternatively, build the pipeline after `OpenCurrentAsync` and replace the writer via a test-visible property if the design supports it. If there is no seam, the cleanest path is to make `ProcessOneAsync` accept a writer argument or to expose `CurrentWriter` as a settable property on `IntervalRotator` for test use. Make the minimal change needed and document it.

### DT-011: Rename integration test stub method names

**Files to fix:**
- `tests/Tracer.Tests.Integration/ObserverFakeNodeEndToEndTests.cs`
- `tests/Tracer.Tests.Integration/ObserverRotationIntegrationTests.cs`

**Required method names per TRC-P3-001 SC14 (ObserverFakeNodeEndToEndTests):**
- `GetSessions_ReturnsActiveSession`
- `GetScenarioNotables_ReturnsNotablesFromScenario`
- `GetScenarioPhases_ReturnsActivePhaseName`

**Required method names per TRC-P3-001 SC15 (ObserverRotationIntegrationTests):**
- `FirstInterval_FinalizedWithReady_AfterRotation`
- `SecondInterval_QueriesReturnCurrentIntervalEvents`
- `Queries_DuringRotation_SucceedAfterBriefBlock`

Replace the existing wrongly-named stub methods with correctly-named stubs using `[Fact(Skip = "Deferred to TRC-P3-009")]`.

Also add the additional test methods required by TRC-P3-009 SC5-SC9 to `ObserverRotationIntegrationTests` as skipped stubs:
- `MultipleNodes_EventsFromAllNodesIngested` — `[Fact(Skip = "Deferred to TRC-P3-009")]`

### DT-012: Add assertion to `OnGracefulShutdown_FinalRotationHasGracefulReason`

**File:** `tests/Tracer.Tests.Unit/Observer/ObserverHostedServiceTests.cs`

Replace the `await Task.CompletedTask` no-op assertion with a real one:
- After `StopAsync`, the `_rotator` should have written a manifest. Read the last manifest file from the temp `_tempDir` using `ManifestWriter.ReadAsync` or `System.Text.Json.JsonSerializer.Deserialize`, and assert `manifest.FinalizationReason == ManifestFinalizationReason.GracefulShutdown`.

---

## ✅ Task 1: TRC-P3-003 — Session and Topology Endpoints

**Full task definition:** `docs/TASK-DETAIL.md#trc-p3-003--session-and-topology-endpoints`  
**Design reference:** `docs/tracer_phase3_design.md §4.2`, `§4.3`, `§4.4`

### 1.1 DTOs (replace stubs in `src/Tracer.WebApi/Contracts/Dto/`)

Implement all DTOs as `sealed record` classes with JSON-serializable properties. Read the DTO shapes from design §4.3.

**`SessionDto.cs`**
```csharp
public sealed record SessionDto
{
    public required string SessionId { get; init; }
    public required DateTimeOffset StartUtc { get; init; }
    public DateTimeOffset? EndUtc { get; init; }
    public required string Status { get; init; }      // "Active" | "Completed"
    public required int EventCount { get; init; }
    public required IReadOnlyList<string> ParticipatingNodes { get; init; }
    public string? ScenarioId { get; init; }
    public string? Label { get; init; }
}
```

**`NodeInfoDto.cs`** — per node in topology:
```csharp
public sealed record NodeInfoDto
{
    public required string NodeId { get; init; }
    public required DateTimeOffset FirstSeenUtc { get; init; }
    public required DateTimeOffset LastSeenUtc { get; init; }
    public required long EventsPublished { get; init; }
}
```

**`TopologyDto.cs`**
```csharp
public sealed record TopologyDto
{
    public required IReadOnlyList<NodeInfoDto> Nodes { get; init; }
    public required DateTimeOffset AsOfUtc { get; init; }
}
```

**`NotableEventDto.cs`**
```csharp
public sealed record NotableEventDto
{
    public required string EventId { get; init; }        // 16-char uppercase hex
    public required string TraceId { get; init; }        // 16-char uppercase hex
    public required DateTimeOffset OccurredAtUtc { get; init; }
    public required string Topic { get; init; }
    public required string NotableLabel { get; init; }
    public string? Severity { get; init; }
    public string? EntityId { get; init; }
    public string? ScenarioPhase { get; init; }
    public string? PayloadJson { get; init; }
}
```

**`ScenarioPhaseDto.cs`**
```csharp
public sealed record ScenarioPhaseDto
{
    public required string PhaseName { get; init; }
    public required DateTimeOffset StartedAtUtc { get; init; }
    public DateTimeOffset? EndedAtUtc { get; init; }
    public required string Status { get; init; }   // "Active" | "Completed"
}
```

**`ScenarioStateDto.cs`**
```csharp
public sealed record ScenarioStateDto
{
    public string? CurrentPhase { get; init; }
    public required long TotalEvents { get; init; }
    public required long TotalNotables { get; init; }
    public required IReadOnlyList<string> ParticipatingNodes { get; init; }
}
```

**`EventDto.cs`**
```csharp
public sealed record EventDto
{
    public required string EventId { get; init; }        // 16-char uppercase hex
    public required string TraceId { get; init; }        // 16-char uppercase hex
    public string? ParentEventId { get; init; }
    public required DateTimeOffset OccurredAtUtc { get; init; }
    public required string PublisherNode { get; init; }
    public required string SubscriberNode { get; init; }
    public required string Topic { get; init; }
    public required long SequenceNumber { get; init; }
    public string? EntityId { get; init; }
    public string? OwningPlayerId { get; init; }
    public string? ScenarioPhase { get; init; }
    public string? Severity { get; init; }
    public string? NotableLabel { get; init; }
    public string? PayloadJson { get; init; }
}
```

**`LiveStatusDto.cs`**
```csharp
public sealed record LiveStatusDto
{
    public required bool IngestionHealthy { get; init; }
    public required long IngestedTotal { get; init; }
    public required long DroppedTotal { get; init; }
    public required int ActiveSseClients { get; init; }
    public DateTimeOffset? LastEventUtc { get; init; }
}
```

### 1.2 DtoMappers (replace stub in `src/Tracer.WebApi/Contracts/Mapping/DtoMappers.cs`)

```csharp
public static class DtoMappers
{
    public static string ToHex(EventId id) => id.Value.ToString("X16");
    public static string ToHex(TraceId id) => id.Value.ToString("X16");
    
    public static EventDto ToDto(EventRecord ev) => new EventDto { ... };
    public static NotableEventDto ToNotableDto(EventRecord ev) => new NotableEventDto { ... };
    
    // Session mapping — receives raw query result rows (sessionId string, startUtc, endUtc?, etc.)
    // Topology mapping — receives raw query result rows (nodeId, firstSeen, lastSeen, count)
    // ScenarioPhase mapping
    // ScenarioState mapping
}
```

Implement all mapping methods. Key rules (per design §4.3 and TRC-P3-003 SC requirements):
- `EventId` and `TraceId` are formatted as 16-char uppercase hex (e.g., `"000000000000002A"`)
- `WallclockTime` values are converted to `DateTimeOffset` via `ToDateTimeOffset()` then serialized as ISO 8601
- Nullable fields (`EntityId`, `OwningPlayerId`, `ScenarioPhase`, `Severity`, `NotableLabel`) must serialize as absent JSON keys (not `null` literals) — use `[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]` or equivalent

### 1.3 Query Services

**`SessionQueryService.cs`** — See design §4.4 for the two-step approach:
1. Find `system.session_start` events via `SELECT ... FROM events WHERE topic = 'system.session_start'`; extract `sessionId` from `payload_json` using `JSON_EXTRACT_STRING`.
2. For each session, do an aggregate query for `participatingNodes` (distinct `publisher_node`) and `eventCount`.
3. Pair session-start with matching `system.session_end` events by `sessionId` to derive `status` ("Active" or "Completed") and `endUtc`.
4. Order sessions by `startUtc` descending.
5. Apply optional `from`/`to` time range filter.

Acquire connections from `ReadOnlyConnectionPool`.

**`TopologyQueryService.cs`** — Single aggregate query:
```sql
SELECT publisher_node, MIN(publish_wallclock) as first_seen, 
       MAX(publish_wallclock) as last_seen, COUNT(*) as events_published
FROM events
GROUP BY publisher_node
```

**`EventLookupService.cs`** — Single-row lookup:
```sql
SELECT * FROM events WHERE event_id = $id LIMIT 1
```

### 1.4 Endpoints (replace stubs with real implementations)

**`SessionEndpoints.cs`** — See design §4.2 and TRC-P3-003 success conditions SC1–SC6. Key requirements:
- `GET /api/sessions` — returns 200 with array; empty array when no sessions
- `GET /api/sessions?from=&to=` — time-range filter (exclude sessions whose start is outside range)
- `GET /api/sessions/{sessionId}` — 200 or 404
- Session DTO must include `participatingNodes` and `eventCount`

**`TopologyEndpoints.cs`** — `GET /api/topology` returns `TopologyDto`.

### 1.5 Unit Tests

**`tests/Tracer.Tests.Unit/WebApi/SessionEndpointTests.cs`** — Use `WebApiFixture` + pre-inserted records. Required tests from TRC-P3-003 SC7:
- `ListSessions_EmptyDb_ReturnsEmptyArray`
- `ListSessions_OrderedByStartTimeDesc`
- `ActiveSession_HasStatusActive`
- `CompletedSession_HasStatusCompletedAndEndUtcSet`
- `TimeRangeFilter_ExcludesOutOfRangeSessions`
- `GetSession_UnknownId_Returns404`
- `EventCountAndNodes_ReflectSessionTimeRange`

For tests that need data: use `ObserverFixture.PushAsync` to push known `system.session_start` / `system.session_end` `EventRecord`s with structured payload JSON, then query via `_fixture.Client.GetAsync("/api/sessions")`.

**`tests/Tracer.Tests.Unit/WebApi/DtoMappingTests.cs`** — Pure unit tests, no HTTP. Required tests from TRC-P3-003 SC8:
- `SessionDto_AllFieldsMapped`
- `TopologyDto_AllFieldsMapped`
- `TraceId_FormattedAs16CharUppercaseHex` — `DtoMappers.ToHex(new TraceId(255)) == "00000000000000FF"`
- `EventId_FormattedAs16CharUppercaseHex`
- `NullableFields_SerializeAsMissingKeysNotNullLiterals` — serialize a DTO with null optional fields; the JSON string doesn't contain `"entityId":null`
- `DateTimeOffset_RoundTripsThroughIso8601`

Add a stub integration test in `tests/Tracer.Tests.Integration/WebApiQueryRoundTripTests.cs`:
```csharp
public class WebApiQueryRoundTripTests
{
    [Fact(Skip = "Deferred to TRC-P3-010")] public Task GetSessions_AfterIngestion_ReturnsCorrectSessions() => Task.CompletedTask;
    [Fact(Skip = "Deferred to TRC-P3-010")] public Task GetSession_ById_ReturnsMatchingDto() => Task.CompletedTask;
    // ... all 9 methods from TRC-P3-010 success conditions
}
```

---

## ✅ Task 2: TRC-P3-004 — Scenario and Event Endpoints

**Full task definition:** `docs/TASK-DETAIL.md#trc-p3-004--scenario-and-event-endpoints`  
**Design reference:** `docs/tracer_phase3_design.md §4.2`, `§4.3`, `§4.4`

### 2.1 Query Services

**`ScenarioQueryService.cs`** — Three methods:

1. `GetNotablesAsync(string sessionId, int limit, DateTimeOffset? before, CancellationToken)`:
```sql
SELECT * FROM events 
WHERE JSON_EXTRACT_STRING(payload_json, '$.sessionId') = $sessionId
  AND notable_label IS NOT NULL
  AND ($before IS NULL OR publish_wallclock < $before)
ORDER BY publish_wallclock DESC
LIMIT $limit
```

2. `GetPhasesAsync(string sessionId, CancellationToken)` — Pair `scenario.phase_started` / `scenario.phase_ended` events by `phase_name` from payload:
```sql
SELECT topic, publish_wallclock, payload_json FROM events
WHERE JSON_EXTRACT_STRING(payload_json, '$.sessionId') = $sessionId
  AND topic IN ('scenario.phase_started', 'scenario.phase_ended')
ORDER BY publish_wallclock ASC
```
Then pair in-memory: for each `scenario.phase_started`, find the matching `scenario.phase_ended` by `phase_name` payload field.

3. `GetCurrentStateAsync(string sessionId, CancellationToken)` — Aggregate query for `totalEvents`, `totalNotables`, distinct nodes, and the latest unmatched phase-start.

### 2.2 Endpoints

**`ScenarioEndpoints.cs`** — Key validation:
- `limit` must be 1–500; otherwise 400 with `ProblemDetails`
- `before` cursor is an ISO 8601 `DateTimeOffset?` query param

**`EventEndpoints.cs`** — `GET /api/events/{eventId}`:
- Validate: `eventId` must be a 16-character hex string (exactly). Return 400 if not exactly 16 hex chars.
- Parse to `ulong`, construct `EventId`, query `EventLookupService`.
- Return 200 or 404.

### 2.3 Unit Tests

**`tests/Tracer.Tests.Unit/WebApi/ScenarioEndpointTests.cs`** — Required tests from TRC-P3-004 SC9:
- `GetNotables_ReturnsOnlyNotableEvents`
- `GetNotables_PaginationWithBeforeCursor`
- `GetNotables_LimitOutOfRange_Returns400`
- `GetPhases_PairsStartAndEndEvents`
- `GetPhases_UnpairedStart_StatusActive`
- `GetState_ReflectsCurrentPhaseAndAggregates`

**`tests/Tracer.Tests.Unit/WebApi/EventEndpointTests.cs`** — Required tests from TRC-P3-004 SC10:
- `GetEvent_ValidHexId_Returns200WithEventDto`
- `GetEvent_UnknownId_Returns404`
- `GetEvent_NonHexId_Returns400`
- `GetEvent_WrongLengthHexId_Returns400`

**Extend `DtoMappingTests.cs`** with TRC-P3-004 SC11 tests:
- `EventRecord_ToEventDto_AllFieldsMapped`
- `EventRecord_ToNotableEventDto_ExcludesSubscriberAndSequenceNumber`
- `Severity_SerializesAsTitleCaseString`

---

## ✅ Task 3: TRC-P3-005 — SSE Live Streaming

**Full task definition:** `docs/TASK-DETAIL.md#trc-p3-005--sse-live-streaming`  
**Design reference:** `docs/tracer_phase3_design.md §5` (§5.1–§5.5)

### 3.1 Core SSE Types (replace stubs in `src/Tracer.WebApi/Streaming/`)

**`SseFilter.cs`** — already stubbed as `sealed record SseFilter(bool NotablesOnly = false, string? SessionId = null)`. No changes needed.

**`SseConnection.cs`** — NEW file in `src/Tracer.WebApi/Streaming/`:

```csharp
namespace Tracer.WebApi.Streaming;

/// <summary>
/// Per-client SSE connection. Holds a bounded channel; slow clients drop oldest events.
/// </summary>
public sealed class SseConnection : IAsyncDisposable
{
    private readonly Channel<EventRecord> _channel;
    private long _dropCount;

    public SseConnection(int bufferSize)
    {
        _channel = Channel.CreateBounded<EventRecord>(new BoundedChannelOptions(bufferSize)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public long DropCount => Interlocked.Read(ref _dropCount);

    /// <summary>Enqueue an event for delivery to this client. Drops oldest if full.</summary>
    public void Enqueue(EventRecord ev)
    {
        if (!_channel.Writer.TryWrite(ev))
            Interlocked.Increment(ref _dropCount);
    }

    /// <summary>Returns events as an async sequence; terminates when the channel is completed.</summary>
    public IAsyncEnumerable<EventRecord> ReadAsync(CancellationToken ct)
        => _channel.Reader.ReadAllAsync(ct);

    /// <summary>Signal that the client has disconnected; no more events will be written.</summary>
    public void Complete() => _channel.Writer.TryComplete();

    public ValueTask DisposeAsync()
    {
        Complete();
        return ValueTask.CompletedTask;
    }
}
```

**`SseConnectionManager.cs`** — Replace stub with full implementation:

```csharp
namespace Tracer.WebApi.Streaming;

public sealed class SseConnectionManager
{
    private readonly ConcurrentDictionary<Guid, SseConnection> _connections = new();
    private readonly int _maxConcurrent;
    private readonly int _perClientBufferSize;

    public SseConnectionManager(LiveStreamingConfig config)
    {
        _maxConcurrent = config.MaxConcurrentSseClients;
        _perClientBufferSize = config.PerClientBufferSize;
    }

    public int ActiveCount => _connections.Count;

    /// <summary>
    /// Try to register a new client. Returns null if at capacity (caller returns 503).
    /// </summary>
    public (Guid id, SseConnection connection)? TryRegister()
    {
        if (_connections.Count >= _maxConcurrent) return null;
        var id = Guid.NewGuid();
        var conn = new SseConnection(_perClientBufferSize);
        _connections[id] = conn;
        return (id, conn);
    }

    /// <summary>Deregister and complete the connection (called on client disconnect).</summary>
    public void Deregister(Guid id)
    {
        if (_connections.TryRemove(id, out var conn))
            conn.Complete();
    }

    /// <summary>Fan out to all registered connections.</summary>
    public async Task BroadcastAsync(EventRecord ev, SseFilter filter, CancellationToken ct)
    {
        foreach (var (_, conn) in _connections)
        {
            if (!MatchesFilter(ev, filter)) continue;
            conn.Enqueue(ev);
        }
        await Task.CompletedTask;
    }

    private static bool MatchesFilter(EventRecord ev, SseFilter filter)
    {
        if (filter.NotablesOnly && ev.NotableLabel is null) return false;
        // SessionId filter — check payload_json for sessionId field
        if (filter.SessionId is not null)
        {
            // Simple JSON extraction: look for "sessionId":"value" in payload
            // Using System.Text.Json for correctness
            try
            {
                if (ev.PayloadJson is not null)
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(ev.PayloadJson);
                    if (doc.RootElement.TryGetProperty("sessionId", out var sid))
                    {
                        if (sid.GetString() != filter.SessionId) return false;
                    }
                    else return false; // no sessionId in payload
                }
                else return false;
            }
            catch { return false; }
        }
        return true;
    }
}
```

**`LiveEventBroadcaster.cs`** — Replace stub with full implementation:

The broadcaster is a `BackgroundService` with an internal `Channel<EventRecord>` (unbounded, single-reader) that fans out to `SseConnectionManager`. The `Publish` method is synchronous (non-blocking write to the internal channel). The background loop reads from the channel and calls `SseConnectionManager.BroadcastAsync`.

```csharp
namespace Tracer.WebApi.Streaming;

public sealed class LiveEventBroadcaster : BackgroundService
{
    private readonly Channel<EventRecord> _inbox;
    private readonly SseConnectionManager _connectionManager;
    private readonly ILogger<LiveEventBroadcaster> _logger;

    public LiveEventBroadcaster(SseConnectionManager connectionManager, ILogger<LiveEventBroadcaster> logger)
    {
        _inbox = Channel.CreateUnbounded<EventRecord>(new UnboundedChannelOptions
        {
            SingleReader = true,
            AllowSynchronousContinuations = false
        });
        _connectionManager = connectionManager;
        _logger = logger;
    }

    /// <summary>
    /// Enqueue an event for fan-out. Called on the ingestion thread — must be non-blocking.
    /// </summary>
    public void Publish(EventRecord ev) => _inbox.Writer.TryWrite(ev);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var ev in _inbox.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await _connectionManager.BroadcastAsync(ev, SseFilter.NotablesAndAll, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error broadcasting event");
            }
        }
    }
}
```

> Note: `SseFilter.NotablesAndAll` is a static convenience instance with `NotablesOnly = false, SessionId = null`. Add it to `SseFilter`. Individual SSE connections maintain their own `SseFilter` — the broadcaster broadcasts all events to `SseConnectionManager`, and `BroadcastAsync` applies each connection's own filter. Actually re-read design §5.2 carefully to determine the exact fan-out filter architecture. The design may have `SseConnection` holding its own filter that `BroadcastAsync` checks per-connection.

### 3.2 SSE Endpoints (replace stub in `src/Tracer.WebApi/Endpoints/SseEndpoints.cs`)

**`GET /api/live/notables`** — SSE endpoint per design §5.3:
- Register with `SseConnectionManager`. If at capacity → 503.
- Set headers: `Content-Type: text/event-stream`, `Cache-Control: no-cache`, `X-Accel-Buffering: no`
- Start heartbeat task (`Task.Run` → every `HeartbeatInterval` writes `: keepalive\n\n`)
- Iterate `connection.ReadAsync(ct)` and for each event: write `data: {json}\n\n`, flush
- On `RequestAborted`: deregister from manager, complete connection

**`GET /api/live/status`** — Returns `LiveStatusDto`:
- `IngestionHealthy`: true when `ObserverStateReporter.Snapshot().LastEventUtc` is non-null and within 60 seconds of now
- `ActiveSseClients`: `SseConnectionManager.ActiveCount`
- Other fields from reporter snapshot

### 3.3 Unit Tests

**`tests/Tracer.Tests.Unit/WebApi/SseEndpointTests.cs`** — Required tests from TRC-P3-005 SC9. Use `WebApiFixture` with an `HttpClient` configured for streaming responses:
- `SseEndpoint_Returns200_WithEventStreamContentType`
- `Heartbeat_SentWithinConfiguredInterval` — set `HeartbeatInterval = 500ms` in test config; connect; wait 700ms; assert `: keepalive` received
- `NotableEvent_AppearsOnStream` — publish a notable event via `LiveEventBroadcaster.Publish`; assert `data:` line appears within 500ms
- `NonNotableEvent_NotSentOnNotablesOnlyStream` — publish non-notable event; assert no `data:` line in 500ms
- `AtCapacity_Returns503` — `MaxConcurrentSseClients = 1`; one client connected; second request → 503
- `ClientDisconnect_DeregistersConnection` — connect, disconnect (cancel request), assert `SseConnectionManager.ActiveCount == 0`
- `SlowClient_DropOldest_StreamStaysAlive` — fill per-client buffer beyond capacity; assert `DropCount > 0` and stream still alive

**`tests/Tracer.Tests.Unit/WebApi/LiveStatusTests.cs`** — Required tests from TRC-P3-005 SC10:
- `LiveStatus_ReflectsStateReporterCounters`
- `IngestionHealthy_TrueWhenLastEventWithin60s`
- `IngestionHealthy_FalseWhenNoEventsOrStale`
- `ActiveSseClients_MatchesConnectionManagerCount`

Add a stub integration test class:
**`tests/Tracer.Tests.Integration/LiveStreamingTests.cs`**
```csharp
public class LiveStreamingTests
{
    [Fact(Skip = "Deferred to TRC-P3-011")] public Task PushNotableEvents_AppearOnStreamInOrder() => Task.CompletedTask;
    [Fact(Skip = "Deferred to TRC-P3-011")] public Task ClientReconnect_ReceivesNewEventsAfterReconnect() => Task.CompletedTask;
    [Fact(Skip = "Deferred to TRC-P3-011")] public Task SlowClient_DropsCountedButStreamRemainsAlive() => Task.CompletedTask;
}
```

---

## 🧪 Testing Requirements

**Minimum additions:**
- 6 rewritten `ObserverIngestionTests` (using real `pipeline.RunAsync`)
- 7 `SessionEndpointTests` + 6 `DtoMappingTests` (extended with 3 more from TRC-P3-004)
- 6 `ScenarioEndpointTests` + 4 `EventEndpointTests`
- 7 `SseEndpointTests` + 4 `LiveStatusTests`
- Integration stub classes: `WebApiQueryRoundTripTests`, `LiveStreamingTests`

**Quality standards:**
- Every endpoint test must push real data through `ObserverFixture.PushAsync` and then query via HTTP — no mocks of query services. Tests must verify actual field values match what was pushed.
- SSE tests must use a real streaming `HttpClient` (not just checking status codes). The `Heartbeat` and `NotableEvent_AppearsOnStream` tests must actually read SSE lines from the response stream.
- `WriteFailure_IncrementsDropCounter_PipelineContinues` must verify `IngestedTotal == 2` (showing pipeline continued processing after the first failure).
- All Phase 1, 2, and 3 tests must pass.

---

## ⚠️ Important Notes

### Testing SSE with HttpClient
For SSE endpoint tests, use:
```csharp
var request = new HttpRequestMessage(HttpMethod.Get, "/api/live/notables");
var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
var stream = await response.Content.ReadAsStreamAsync();
var reader = new StreamReader(stream);
```
Read lines with a `CancellationTokenSource` timeout to prevent test hangs.

### Session query system.session_start payload format
`SessionQueryService` extracts `sessionId` from the `PayloadJson` field of events with topic `system.session_start`. Use this payload structure consistently in tests:
```json
{"sessionId":"session-abc","scenarioId":"CombatEngagement","label":"Test Run 1"}
```

### Phase filter in Scenario queries
`GetNotablesAsync` and `GetPhasesAsync` filter by `JSON_EXTRACT_STRING(payload_json, '$.sessionId') = $sessionId`. Tests must push events with this payload field populated.

### ConnectionPool not needed for unit endpoint tests
In `WebApiFixture`, if the `ObserverHostedService` is not running (just the WebApp is hosted), the `ReadOnlyConnectionPool` may not be initialized. Use `ObserverFixture` (which has the full host running) for endpoint tests that need actual data. For tests that need only 400/404/503 responses, `WebApiFixture` with an uninitialized or stub pool is fine.

---

## 📊 Report Requirements

Submit `.dev/tracer/reports/BATCH-07-REPORT.md` with:

**Q1:** What issues did you encounter during corrective fixes? How did you implement the pipeline-based `ObserverIngestionTests`?

**Q2:** What challenges came up implementing the DuckDB JSON extraction queries? Any SQL quirks?

**Q3:** What design decisions did you make for SSE endpoint test reading (polling strategy, timeout handling)?

**Q4:** What edge cases did you discover in the session pairing logic or phase pairing?

**Q5:** Any performance concerns in the SSE fan-out implementation?

**Q6:** Suggested commit message.

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] DT-010, DT-011, DT-012 corrected — pipeline tests call `RunAsync`, stubs renamed, shutdown assertion added
- [ ] All DTOs implemented with proper JSON serialization
- [ ] `SessionQueryService`, `TopologyQueryService`, `ScenarioQueryService`, `EventLookupService` implemented
- [ ] All REST endpoints functional (`/api/sessions`, `/api/topology`, `/api/scenario/*`, `/api/events/{id}`)
- [ ] Full `LiveEventBroadcaster`, `SseConnectionManager`, `SseConnection` implemented
- [ ] `GET /api/live/notables` returns SSE stream; `GET /api/live/status` returns DTO
- [ ] All required unit tests pass (≥ 34 new test methods)
- [ ] Integration stub classes created with correct skip-annotated method names
- [ ] `dotnet test Tracer.sln --configuration Release` exits code 0
- [ ] Report submitted

---

## 📚 Reference Materials

- **Task Defs:** `docs/TASK-DETAIL.md` — TRC-P3-003, TRC-P3-004, TRC-P3-005
- **Phase 3 Design:** `docs/tracer_phase3_design.md §4` (endpoints), `§5` (SSE)
- **BATCH-06 Review:** `.dev/tracer/reviews/BATCH-06-REVIEW.md`
- **Existing agent tests for query pattern reference:** `tests/Tracer.Tests.Unit/Storage/QueryBuilderTests.cs`
- **DuckDB SQL JSON functions:** https://duckdb.org/docs/data/json/json_functions.html (use `json_extract_string`)
