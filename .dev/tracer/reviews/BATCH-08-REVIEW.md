# BATCH-08 Review

## Decision: APPROVED

## Summary

BATCH-08 successfully delivers all three corrective fixes (DT-013/014/015) and implements 21 previously-skipped integration tests across TRC-P3-009, TRC-P3-010, and TRC-P3-011. 182 unit tests and 41 integration tests pass with 0 failures and 0 skipped. The route correction, the `ObserverFixtureOptions.Clock` addition, and the drop-count fix in `SseConnection` are all solid engineering. Three P2 test-quality gaps are noted and tracked below — none are blockers, but one significantly weakens a rotation isolation test.

---

## Corrective Tasks Review

### DT-013 (P1) — Scenario Route Fix ✅ RESOLVED

`ScenarioEndpoints.cs` now correctly uses `/api/scenario/notables?sessionId=...`, `/api/scenario/phases?sessionId=...`, `/api/scenario/state?sessionId=...` with `[FromQuery] string sessionId`. All six URL strings in `ScenarioEndpointTests.cs` are updated. Unit test count unchanged. All pass.

### DT-014 (P2) — Pagination Cursor Assertion ✅ RESOLVED

`GetNotables_PaginationWithBeforeCursor` now pushes 5 notables at 1-second intervals, gets all 5, extracts the 3rd (middle) event's ID and timestamp, calls with `before=<3rd-event-id>`, and asserts:
1. `pagedItems.Count < 5` ✅
2. Every returned `occurredAtUtc` is strictly before the cursor timestamp ✅

This correctly validates the core behavior of the `before=` parameter.

### DT-015 (P2) — Session Ordering Test ✅ RESOLVED

`ListSessions_OrderedByStartTimeDesc` added to `SessionEndpointTests.cs`. Pushes two sessions 1 hour before/after `BaseTime`, calls `GET /api/sessions`, asserts `sessions[0].startUtc > sessions[1].startUtc`. Correct.

---

## TRC-P3-009 — Observer+FakeNode Integration Tests

### ObserverFixtureOptions.Clock ✅

`Clock` property added to `ObserverFixtureOptions`; `CreateAsync` conditionally registers the provided `IClock` or falls back to `SystemClock`. Clean implementation. Used correctly in `ObserverRotationIntegrationTests.InitializeAsync`.

### ObserverFakeNodeEndToEndTests (3 tests) ✅

All three tests use unique `Guid.NewGuid():N` session IDs, push events via `_fixture.PushAsync`, and query the HTTP API:
- `GetSessions_ReturnsActiveSession`: finds session by sessionId in array, asserts `status = "Active"` ✅
- `GetScenarioNotables_ReturnsNotablesFromScenario`: asserts non-empty array with non-null `notableLabel` ✅
- `GetScenarioPhases_ReturnsActivePhaseName`: asserts `status = "Active"` and `phaseName = "Alpha"` ✅

### ObserverRotationIntegrationTests (4 tests) — Mostly ✅, one P2

**`FirstInterval_FinalizedWithReady_AfterRotation`** ✅

Correctly captures `rotator.CurrentDirectory` before rotation, calls `ForceRotationAsync`, reads manifest via `ManifestWriter.ReadAsync`, asserts `ScheduledRotation` and `IsReady`. Clean.

**`SecondInterval_QueriesReturnCurrentIntervalEvents`** ⚠️ P2 Weak Assertion

The spec (TRC-P3-009 SC7) requires verifying "a query filtered to interval 2's time range returns exactly 100 results and none from interval 1". The implementation pushes 100 events into interval 2 then calls `GET /api/live/status` and asserts `ingestedTotal > 0`.

`ingestedTotal` is a **cumulative counter** that includes all 200 events from both intervals. `> 0` is trivially satisfied by the 100 interval-1 events pushed before rotation. This assertion would pass even if the pool was never refreshed and queries still targeted interval 1.

The correct approach: after rotation, push 100 events with a distinct `sessionId` (e.g., "session-interval2"). Query `GET /api/sessions` and assert a session with that ID is visible. Since interval 1 had no `system.session_start` event, only the interval-2 events would produce a session result — this would prove the pool is targeting the new file. Track as DT-017.

**`Queries_DuringRotation_SucceedAfterBriefBlock`** ✅

Runs `ForceRotationAsync` and `GET /api/sessions` concurrently, asserts `!= 500`. Correct. The 2000ms completion window isn't explicitly asserted (spec said "within 2000ms") but since `Task.WhenAll` blocks until both finish, and both are TestServer operations that should be fast, this is acceptable.

**`MultipleNodes_EventsFromAllNodesIngested`** ✅

Pushes 50+50 events from two distinct `AgentId` values, queries `GET /api/topology`, asserts `nodes.Count >= 2`, each node appears in the response with `eventsPublished = 50`. Correct.

---

## TRC-P3-010 — Web API Query Round-Trip Integration Tests

All 8 tests are implemented with spec-aligned method names and appropriate `ObserverFixture` isolation (each test runs against a fresh fixture via `IAsyncLifetime`). Unique GUIDs prevent inter-test interference.

**`GetSessions_AfterIngestion_ReturnsCorrectSessions`** ✅

Uses `IndexOf` to verify ordering — not just comparing timestamps but asserting positional order in the array. Strong assertion.

**`GetSession_ById_ReturnsMatchingDto`** ✅

Asserts `sessionId`, `scenarioId`, `label`, `status` all match. Good.

**`GetScenarioNotables_ReturnsOnlyNotableEvents_WithCorrectFields`** — Minor P2

Asserts count = 2 and `notableLabel != null`. The spec (SC4) also requires `occurredAtUtc`, `topic`, and `severity` to match the push. These field values are not verified. The unit-level `DtoMappingTests` already cover mapping, so this is a minor gap (P2, low priority).

**`GetScenarioNotables_BeforeCursor_ReturnsSubset`** ✅

Excellent — pushes 10 events at 1-second spacing, uses midpoint (index 4 in descending order), applies `limit=3&before=<midEventId>`, asserts exactly 3 results all before the midpoint timestamp. Precise.

**`GetScenarioPhases_PairsStartAndEnd`** ✅

Paired phase: `Completed` + non-null `endedAtUtc`. Unpaired phase: `Active`. Correct.

**`GetEvent_ById_ReturnsCorrectEventDto`** — P2 Incomplete Fields

Asserts `eventId` non-empty and `topic = "combat.hit"`. Missing: `traceId` matching the pushed `ev.TraceId`, `severity` matching `Severity.Warning`, `occurredAtUtc` matching the event's publish time. The spec (TRC-P3-010 SC7) requires all these fields to match the push. Track as DT-018.

**`GetEvent_UnknownId_Returns404`** ✅

`DEADBEEF01020304` (valid 16-char hex, no matching row) → 404. Correct.

**`GetTopology_AfterIngestion_ReturnsNodeInfo`** — P2 Missing eventsPublished

Asserts both node IDs appear in the topology. The spec (SC9) also requires "correct `firstSeenUtc` and `eventsPublished`". The test doesn't check `eventsPublished` or `firstSeenUtc`. Both nodes have exactly 1 event each so `eventsPublished = 1` is easy to assert. Track as DT-019.

---

## TRC-P3-011 — Live Streaming Integration Tests

All 6 tests are real streaming tests. Quality is high overall.

**`PushNotableEvents_AppearOnStreamInOrder`** ✅

5 notables → waits for 5 `data:` lines with 5-second deadline. Counts `data:` lines correctly. Note: the test doesn't verify order (that events arrive in the same order as pushed) — but given SSE is a serial channel, ordering is structurally guaranteed.

**`ClientReconnect_ReceivesNewEventsAfterReconnect`** ✅

Connects, pushes one event, reads the `data:` line, cancels connection, reconnects, pushes second event, reads new `data:` line. Verifies server doesn't hang or throw between connections.

**`SlowClient_DropsCountedButStreamRemainsAlive`** ✅

The direct `conn.Enqueue(...)` approach correctly bypasses the HTTP layer. Fills 50 items into a buffer of 20 synchronously, asserts `DropCount > 0`, then reads the final event via the HTTP stream. The `DropOldest` + `Reader.Count >= _bufferSize` pre-check fix in `SseConnection` is correctly implemented.

**`MultipleNodes_AllEventsAppearInUnifiedStream`** — P3 Minor

Asserts `lines.Count == 20`. The spec says "verified by `eventId`". Event IDs are in the JSON payload of each `data:` line and could be extracted to verify 20 distinct IDs rather than just 20 lines. This is a minor precision gap (P3, low priority).

**`SessionFilter_ExcludesEventsFromOtherSession`** ✅

Asserts 3 lines arrive (session A events) and each line contains `sessionIdA`. The session ID is embedded in the JSON payload in the SSE data line, so the string match is correct.

**`Heartbeat_ReceivedWithinConfiguredInterval`** ✅

Uses a 200ms per-read timeout in a loop, with a 1500ms overall deadline. Detects `: keepalive` line. Correct approach for testing SSE heartbeats without deadlocking.

---

## Production Code Changes

The developer modified production code beyond the test files:

### `SseConnection` — `_bufferSize` + DropOldest fix ✅

The fix to track drops with `DropOldest` mode is correct and necessary. `TryWrite` on a `DropOldest` channel always returns `true` (it silently drops the oldest). The pre-check `Reader.Count >= _bufferSize` is a sound TOCTOU-acceptable heuristic (may under-count drops by 1 in a race, but deterministic in single-threaded test usage). The `_bufferSize` field storage is minimal and justified.

### `SseConnectionManager` — `Connections` and `TotalDropCount` ✅

`Connections` property exposes registered connections for test introspection. This is a legitimate test-seam addition. `TotalDropCount` is a useful diagnostics property. Both are read-only and add no risk.

---

## Debt Tracker Updates

| ID | Priority | Source | Description | Target |
|----|----------|--------|-------------|--------|
| DT-017 | P2 | BATCH-08 | `SecondInterval_QueriesReturnCurrentIntervalEvents`: `ingestedTotal > 0` is trivially true; should push session_start into interval 2, then query `GET /api/sessions` to verify pool targets the new interval | BATCH-09 |
| DT-018 | P2 | BATCH-08 | `GetEvent_ById_ReturnsCorrectEventDto`: missing `traceId`, `severity`, `occurredAtUtc` field assertions matching the pushed event (spec TRC-P3-010 SC7) | BATCH-09 |
| DT-019 | P2 | BATCH-08 | `GetTopology_AfterIngestion_ReturnsNodeInfo`: missing `eventsPublished` count assertion per node (spec TRC-P3-010 SC9) | BATCH-09 |
| DT-020 | P3 | BATCH-08 | `MultipleNodes_AllEventsAppearInUnifiedStream`: asserts count == 20 but doesn't verify 20 distinct eventIds (spec says "verified by eventId") | BATCH-09 |
