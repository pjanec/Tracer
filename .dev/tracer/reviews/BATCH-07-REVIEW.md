# BATCH-07 Review

## Decision: CHANGES REQUIRED

## Summary

BATCH-07 delivered significant working code: all three corrective tasks (DT-010/011/012) are properly fixed, the full REST endpoint suite is implemented and tested with real HTTP + real DuckDB, and the SSE streaming pipeline is functionally sound. 181 unit tests and 20 integration tests pass with 0 failures. However, the scenario endpoint routes deviate from the spec in a way that will break BATCH-09 integration tests — this is a P1 issue. Three P2 issues in test quality are also noted.

---

## Corrective Tasks Review

### DT-010: `ObserverIngestionTests` — ✅ FIXED

All 6 tests now create a `FixedDataSource` (or `BlockingDataSource`), call `pipeline.RunAsync(ct)`, and assert on observable outputs:
- `Records_WrittenToCurrentWriter`: calls `RunAsync`, flushes writer, opens DuckDB reader, asserts `count == 10` ✅
- `Events_PublishedToLiveBroadcaster`: uses `CountingBroadcaster` (virtual override), asserts `PublishCount == 3` ✅
- `SlowState_WrittenButNotBroadcast`: asserts `PublishCount == 0` AND `IngestedTotal == 2` ✅
- `FastState_WrittenViaAppendFastStateAsync`: asserts `IngestedTotal == 1` AND `DroppedTotal == 0` ✅
- `Cancellation_PropagatesCleanly`: `BlockingDataSource` waits on `Task.Delay(Infinite, ct)`; `RunAsync` returns after cancel without throwing ✅
- `WriteFailure_IncrementsDropCounter_PipelineContinues`: `FaultingWriter` throws on first call; asserts `DroppedTotal == 1` AND `IngestedTotal == 2` ✅

The test seam required adding a public setter to `IntervalRotator.CurrentWriter`. See P3 note below.

### DT-011: Integration stub method names — ✅ FIXED

`ObserverFakeNodeEndToEndTests` now has: `GetSessions_ReturnsActiveSession`, `GetScenarioNotables_ReturnsNotablesFromScenario`, `GetScenarioPhases_ReturnsActivePhaseName`. `ObserverRotationIntegrationTests` now has: `FirstInterval_FinalizedWithReady_AfterRotation`, `SecondInterval_QueriesReturnCurrentIntervalEvents`, `Queries_DuringRotation_SucceedAfterBriefBlock`, `MultipleNodes_EventsFromAllNodesIngested`. All correct per spec.

`WebApiQueryRoundTripTests` (9 stubs → TRC-P3-010) and `LiveStreamingTests` (3 stubs → TRC-P3-011) are also present with correct deferred-to labels.

### DT-012: `OnGracefulShutdown_FinalRotationHasGracefulReason` — ✅ FIXED

Now reads `manifest.json` using `ManifestWriter.ReadAsync` and asserts `FinalizationReason == GracefulShutdown`. Real assertion.

---

## TRC-P3-003 — Session and Topology Endpoints

### Code Quality ✅

- `SessionQueryService`: correct `payload` column name (not `payload_json`); `TIMESTAMP_NS` → `DateTime` via `GetValue(n)` cast; `UBIGINT` → `Convert.ToUInt64`. N+1 queries per session are suboptimal but acceptable for Phase 3.
- `TopologyQueryService`: single aggregate `GROUP BY publisher_node` query — correct.
- `DtoMappers.ToHex`: uses `"X16"` format → 16-char uppercase hex ✅
- All DTOs use `[JsonIgnore(Condition = WhenWritingNull)]` on nullable fields ✅

### P2 — `ListSessions_OrderedByStartTimeDesc` test is missing ⚠️

Required by TRC-P3-003 SC7 but not present. Instead `ListSessions_ReturnsSessionWithCorrectFields` was added, which checks field values but not ordering. The `SessionQueryService.ListAsync` sorts by `publish_wallclock DESC` but there is no test verifying that two sessions with different start times are returned in descending order. Must be added in BATCH-08.

### Remainder ✅

`ListSessions_EmptyDb_ReturnsEmptyArray`, `ActiveSession_HasStatusActive`, `CompletedSession_HasStatusCompletedAndEndUtcSet`, `GetSession_UnknownId_Returns404`, `EventCountAndNodes_ReflectSessionTimeRange`, `TimeRangeFilter_ExcludesOutOfRangeSessions` — all correct and verified against real DuckDB data.

`DtoMappingTests`: 9 tests covering hex formatting, field mapping, nullable JSON omission, ISO 8601 round-trip, severity as string, parent ID presence/absence. All correct.

---

## TRC-P3-004 — Scenario and Event Endpoints

### P1 — Scenario endpoint routes deviate from spec ❌

The spec (`docs/TASK-DETAIL.md`, `docs/tracer_phase3_design.md §4.1`) defines:

```
GET /api/scenario/notables?sessionId={id}&limit=...&before=...
GET /api/scenario/phases?sessionId={id}
GET /api/scenario/state?sessionId={id}
```

The implementation uses:

```
GET /api/scenarios/{sessionId}/notables?limit=...&before=...
GET /api/scenarios/{sessionId}/phases
GET /api/scenarios/{sessionId}/state
```

Two differences: `scenario` (singular) → `scenarios` (plural), and `sessionId` moved from query param to path segment.

This breaks two downstream consumers:

1. **TRC-P3-009 integration tests** (`ObserverFakeNodeEndToEndTests`): SC3 explicitly states `"GET /api/scenario/notables?sessionId={id}"` and SC4 states `"GET /api/scenario/phases?sessionId={id}"`. These stubs will call the spec route — not the `/scenarios/{id}/...` route — when implemented in BATCH-09.

2. **TRC-P3-010 WebApi round-trip tests** (`WebApiQueryRoundTripTests`): SC4/SC5 in TASK-DETAIL.md also reference spec routes.

The TypeScript client generated in BATCH-08 will be generated from the OpenAPI document. If the OpenAPI doc has the `/api/scenarios/{sessionId}/...` routes, the client will use those routes — which are internally consistent with the unit tests but deviate from everything else in the spec.

**Required fix (BATCH-08 corrective task 0):** Change route registration in `ScenarioEndpoints.cs` to:
```csharp
app.MapGet("/api/scenario/notables", async (
    [FromQuery] string sessionId, [FromQuery] int? limit, [FromQuery] string? before, ...) => ...);
app.MapGet("/api/scenario/phases", async ([FromQuery] string sessionId, ...) => ...);
app.MapGet("/api/scenario/state", async ([FromQuery] string sessionId, ...) => ...);
```
And update `ScenarioEndpointTests.cs` to call the spec routes.

### P2 — `GetNotables_PaginationWithBeforeCursor` weak assertion ⚠️

The test pushes 5 notables at different timestamps, gets all of them, extracts the last event ID, calls with `before={eventId}`, then asserts only `StatusCode == 200`. It does not assert that the returned subset is correct (e.g., that it contains fewer events than the full result, or that no returned event is at or after the cursor). Must be strengthened in BATCH-08.

### Remainder ✅

`ScenarioQueryService` phase-pairing logic is correct. `GetCurrentStateAsync` returns null for an unknown session. `EventEndpoints` validates exactly 16 hex chars with compiled `Regex` and returns 400/404/200 correctly. `EventEndpointTests` 4 tests are all correct.

`DtoMappingTests` extended with `EventRecord_ToNotableEventDto_ExcludesSubscriberAndSequenceNumber`, `Severity_SerializesAsTitleCaseString`, and parent event ID presence/absence tests. All correct.

---

## TRC-P3-005 — SSE Live Streaming

### Code Quality ✅

- `SseConnection`: bounded `Channel` with `DropOldest`, filter applied in `Enqueue` (short-circuit on `NotablesOnly` and `SessionId`), `DropCount` tracked atomically ✅
- `SseConnectionManager`: `ConcurrentDictionary<Guid, SseConnection>` — no lock on hot broadcast path; `TryRegister` returns null at capacity ✅
- `LiveEventBroadcaster`: `virtual void Publish` for test subclassing; `BackgroundService` with `Channel<EventRecord>` fan-out; null-safe constructor for parameterless (test) construction ✅
- `SseEndpoints`: proper headers (`Content-Type: text/event-stream`, `Cache-Control: no-cache`, `X-Accel-Buffering: no`), heartbeat task, `ReadAllAsync` event loop, deregisters on disconnect/exception ✅

### Test Quality ✅

`SseEndpointTests` (7 tests) are real streaming tests using `StreamReader` on the actual response stream:
- `Heartbeat_SentWithinConfiguredInterval`: reads lines until `: keepalive` found; timeout 2s ✅
- `NotableEvent_AppearsOnStream`: publishes after connection registered; reads lines until `data:` found ✅
- `NonNotableEvent_NotSentOnNotablesOnlyStream`: waits 500ms, asserts no `data:` line ✅
- `AtCapacity_Returns503`: second connection hits 503 with `MaxConcurrentSseClients = 1` ✅
- `ClientDisconnect_DeregistersConnection`: cancel request, wait 200ms, assert `ActiveCount == 0` ✅
- `SlowClient_DropOldest_StreamStaysAlive`: fill buffer beyond capacity, assert `ActiveCount > 0` ✅

`LiveStatusTests` (4 tests) verify `GET /api/live/status` DTO fields against `ObserverStateReporter` state ✅

---

## P3 — `IntervalRotator.CurrentWriter` public setter

The sub-agent added a public setter to inject a `FaultingWriter` in `WriteFailure_IncrementsDropCounter_PipelineContinues`. The comment is appropriate (`"Settable for test injection of faulting writers"`) and the approach works. However, a public setter on a production class exposes an internal state manipulation vector that wasn't there before. Should be changed to `internal set` with `[assembly: InternalsVisibleTo("Tracer.Tests.Unit")]` in the production assembly. Low priority — this is a localhost internal tool and not a security boundary.

---

## Debt Tracker Updates

| ID | Priority | Source | Description | Target |
|----|----------|--------|-------------|--------|
| DT-013 | P1 | BATCH-07 | Scenario endpoint routes deviate from spec: `/api/scenarios/{sessionId}/...` used instead of `/api/scenario/...?sessionId=...` — breaks TRC-P3-009 and TRC-P3-010 integration test routes | BATCH-08 corrective |
| DT-014 | P2 | BATCH-07 | `GetNotables_PaginationWithBeforeCursor` only asserts status 200 — must verify returned events are before the cursor | BATCH-08 |
| DT-015 | P2 | BATCH-07 | `ListSessions_OrderedByStartTimeDesc` test missing from `SessionEndpointTests` | BATCH-08 |
| DT-016 | P3 | BATCH-07 | `IntervalRotator.CurrentWriter` has a public setter for test injection; should be `internal set` + `InternalsVisibleTo` | BATCH-09 |
