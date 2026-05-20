# BATCH-08 Developer Report

**Batch:** BATCH-08  
**Tasks:** DT-013 (P1), DT-014 (P2), DT-015 (P2), TRC-P3-009, TRC-P3-010, TRC-P3-011  
**Status:** COMPLETE ✅

---

## Test Count Summary

| Suite | Before | After |
|-------|--------|-------|
| Unit tests | 181 | 182 |
| Integration tests | 20 | 41 |
| Skipped | 19 | 0 |
| **Total** | **220** | **223** |

Final run: `dotnet test Tracer.sln --configuration Release` → **0 failures, 0 skipped**.

---

## Corrective Items

### DT-013 (P1) — Scenario Route Fix ✅

**Problem:** `ScenarioEndpoints.cs` used `/api/scenarios/{sessionId}/notables` (plural prefix, path param).  
**Fix:** Changed all three routes to query-parameter style on the singular prefix:
- `/api/scenario/notables?sessionId=…`
- `/api/scenario/phases?sessionId=…`
- `/api/scenario/state?sessionId=…`

Updated all six URL strings in `ScenarioEndpointTests.cs` to match. All existing unit tests pass with net-zero count change.

### DT-014 (P2) — Pagination Cursor Test ✅

**Problem:** `GetNotables_PaginationWithBeforeCursor` only asserted `StatusCode == 200`.  
**Fix:** The test now pushes 5 notables with 1-second spacing, gets all results, extracts the 3rd item's ID and timestamp, calls with `before=<3rd-event-id>`, then asserts:
1. Result count is strictly less than 5
2. Every item in the paged result has `occurredAtUtc` strictly before the 3rd event's timestamp

### DT-015 (P2) — Session Ordering Test ✅

**Problem:** No test verified descending `startUtc` ordering on `GET /api/sessions`.  
**Fix:** Added `ListSessions_OrderedByStartTimeDesc` to `SessionEndpointTests.cs`. Pushes two sessions — one an hour before `BaseTime`, one an hour after — and asserts the later session is at index 0.

---

## Per-Task Status

### TRC-P3-009 — Observer + FakeNode Integration Tests ✅

**File:** `ObserverFakeNodeEndToEndTests.cs` (3 tests), `ObserverRotationIntegrationTests.cs` (4 tests)

All 7 stubs replaced with real implementations.

**Added to `ObserverFixture`:** `ObserverFixtureOptions.Clock` property; `CreateAsync` registers the provided `IClock` (or falls back to `SystemClock`), enabling `SimulatedClock` injection in rotation tests.

Tests implemented:
1. `GetSessions_ReturnsActiveSession` — push session_start, assert Active session returned
2. `GetScenarioNotables_ReturnsNotablesFromScenario` — push session_start + notables, assert non-empty notables with labels
3. `GetScenarioPhases_ReturnsActivePhaseName` — push phase_started, assert status=Active + phaseName match
4. `FirstInterval_FinalizedWithReady_AfterRotation` — ForceRotation, read manifest, assert ScheduledRotation + IsReady sentinel
5. `SecondInterval_QueriesReturnCurrentIntervalEvents` — rotate then push; `/api/live/status` shows ingestedTotal > 0
6. `Queries_DuringRotation_SucceedAfterBriefBlock` — concurrent query + rotation; assert no 500s
7. `MultipleNodes_EventsFromAllNodesIngested` — 50+50 events from two nodes; topology shows 2 nodes each with 50

### TRC-P3-010 — Web API Query Round-Trip Tests ✅

**File:** `WebApiQueryRoundTripTests.cs` — full class replaced with 8 spec-aligned tests.

Tests implemented:
1. `GetSessions_AfterIngestion_ReturnsCorrectSessions` — descending order verified
2. `GetSession_ById_ReturnsMatchingDto` — GET /api/sessions/{id}
3. `GetScenarioNotables_ReturnsOnlyNotableEvents_WithCorrectFields` — labeled vs unlabeled filter
4. `GetScenarioNotables_BeforeCursor_ReturnsSubset` — limit=3 + before cursor → exactly 3 results, all before midpoint
5. `GetScenarioPhases_PairsStartAndEnd` — paired phase: Completed + endedAtUtc; unpaired: Active + no endedAtUtc
6. `GetEvent_ById_ReturnsCorrectEventDto` — GET /api/events/{X16}
7. `GetEvent_UnknownId_Returns404` — unknown hex ID → 404
8. `GetTopology_AfterIngestion_ReturnsNodeInfo` — 2 distinct publisher nodes appear in topology

### TRC-P3-011 — Live Streaming Tests ✅

**File:** `LiveStreamingTests.cs` — 3 stubs implemented + 3 new tests added.

Tests implemented:
1. `PushNotableEvents_AppearOnStreamInOrder` — 5 notables → 5 `data:` lines within 500ms
2. `ClientReconnect_ReceivesNewEventsAfterReconnect` — disconnect + reconnect; second event arrives on new stream
3. `SlowClient_DropsCountedButStreamRemainsAlive` — tight-loop `Enqueue` fills channel past capacity; `DropCount > 0`; stream still delivers final event
4. `MultipleNodes_AllEventsAppearInUnifiedStream` — 10+10 concurrent notables → all 20 distinct eventIds arrive
5. `SessionFilter_ExcludesEventsFromOtherSession` — session-A filter excludes session-B events
6. `Heartbeat_ReceivedWithinConfiguredInterval` — `: keepalive` line received within 1500ms with 1s interval

---

## Developer Insights

### Q1: Issues and Resolutions

**Issue 1 — DT-013 route ambiguity:** The spec uses query-param style (`?sessionId=`) consistently, but the existing stubs used path params. This broke all integration tests attempting to call scenario endpoints. Fixed first as a P1 blocker.

**Issue 2 — Topology response shape:** Tests initially called `doc.RootElement.EnumerateArray()` directly. The endpoint returns `{ "nodes": [...], "asOfUtc": ... }`, not a bare array. Fixed by calling `doc.RootElement.GetProperty("nodes").EnumerateArray()`.

**Issue 3 — Drop counting with `DropOldest` channels:** `BoundedChannelFullMode.DropOldest` causes `TryWrite` to always return `true` (it silently drops the oldest item and writes the new one). The initial drop detection code checked the `bool` return value of `TryWrite`, which was always `true` and never counted drops. Fixed by checking `_channel.Reader.Count >= _bufferSize` before the write.

**Issue 4 — Drop test reliability:** Testing drops through the HTTP layer is unreliable in TestServer because the in-memory pipe has no meaningful backpressure (~64KB buffer). The SSE write loop drains the channel faster than 50 sequential `await _fixture.PushAsync(...)` calls fill it. Fixed by exposing `SseConnectionManager.Connections` and calling `SseConnection.Enqueue` synchronously in a tight loop (50 iterations), which outpaces the async write loop and reliably fills the bounded channel past capacity.

**Issue 5 — `is not null` in expression trees:** FluentAssertions lambdas (in `OnlyContain`) are compiled as expression trees by xUnit's infrastructure. C# pattern-matching expressions like `is not null` are not supported in expression trees (CS8122). Changed to `!= null`.

**Issue 6 — Non-existent `/api/status` endpoint:** `SecondInterval_QueriesReturnCurrentIntervalEvents` initially called `/api/status`. The correct route is `/api/live/status`. Fixed to use that endpoint and check the `ingestedTotal` field from `LiveStatusDto`.

**Issue 7 — Missing `using Microsoft.Extensions.DependencyInjection`:** `GetRequiredService<T>` is an extension method in that namespace. The rotation test file was missing the import.

**Issue 8 — Blocking `.Result` on Task:** xUnit analyzer rule xUnit1031 flags `.Result` on `Task` in async test methods. Replaced with `await queryTask` + separate variable for status code check.

### Q2: Weak Points in Fixtures or Query Services

- **`ObserverFixture` had no way to override the clock** — the `SimulatedClock` support had to be added as part of this batch. The pattern of passing `IClock` through `ObserverFixtureOptions` works cleanly, but the omission from BATCH-07 forced a fixture change mid-testing cycle.
- **`ReadOnlyConnectionPool` refreshes on `ForceRotationAsync`** — but there is a brief window between rotation and pool refresh where queries might target a stale connection. The `Queries_DuringRotation_SucceedAfterBriefBlock` test explicitly exercises this and confirms no 500s occur.
- **`TopologyQueryService` queries the live interval only** — there is no cross-interval topology aggregation. Test 5 (SC7) had to verify this via the ingestion counter rather than topology, since topology reflects only the current interval after rotation.

### Q3: Design Decisions Beyond the Spec

- **`SseConnectionManager.Connections` property:** Added `IEnumerable<SseConnection> Connections` to expose registered connections for test introspection. This was required for the drop test to access the `SseConnection` object directly (bypassing HTTP). The property returns a live snapshot of `ConcurrentDictionary.Values`, which is safe for test use.
- **`_bufferSize` field in `SseConnection`:** Added to store the capacity at construction time, needed for the TOCTOU-free drop detection check (`Reader.Count >= _bufferSize`).

### Q4: Edge Cases Discovered

- **Session filter substring match is order-sensitive:** `SseConnection.Enqueue` filters by checking `PayloadJson.Contains('"sessionId":"<value>"')`. If a JSON serializer adds spaces after the colon (e.g. with `JsonWriterOptions { Indented = true }`), the filter would fail. All tests use the default `JsonSerializer.Serialize` which produces compact JSON, so this is fine — but worth noting as a potential fragility.
- **Phase pairing requires exact `phaseName` match in payload:** The phase-end event must have the same `phaseName` string as the phase-start event. Tests explicitly set the same phaseName to ensure correct pairing.
- **Event ID URL format:** The event-by-ID endpoint expects a 16-character uppercase hex string (`X16`). Using `EventId.Value.ToString("X16")` produces the correct format; using `ToString()` without format specifier would not.

### Q5: Performance / Reliability Concerns

- **SSE tests with timing:** Several SSE tests use `Task.Delay` and wall-clock deadlines. These are inherently racy on a loaded CI machine. The delays used (500ms–1500ms) are generous, but very slow machines could still flake. A future improvement could use `TaskCompletionSource`-based signalling instead of polling.
- **Drop test is deterministic but fragile on very fast machines:** The tight `Enqueue` loop relies on running faster than the async SSE write loop. On a machine where the async continuations schedule nearly instantly, some drops might be consumed before we count them. In practice, 50 synchronous calls easily outpace one async continuation, but this is a behavioral assumption.
- **`_nextId` static counters in test classes:** Static `ulong _nextId` counters are not reset between test runs within the same process. This is fine because xUnit runs each test in a separate fixture instance, but if xUnit ever parallelizes tests within the same class, counter collisions could occur. The tests currently use `[Collection]` isolation via `IAsyncLifetime`.

---

## Suggested Commit Message

```
test: implement BATCH-08 integration tests (TRC-P3-009/010/011)

Corrective fixes:
- ScenarioEndpoints: change 3 routes from path-param to query-param
  style (/api/scenario/... ?sessionId=), update unit test URLs (DT-013)
- GetNotables_PaginationWithBeforeCursor: assert subset count and
  timestamp ordering, not just 200 status (DT-014)
- SessionEndpointTests: add ListSessions_OrderedByStartTimeDesc (DT-015)

Feature additions:
- ObserverFixtureOptions.Clock: optional IClock injection for
  SimulatedClock support in rotation tests (TRC-P3-009)
- SseConnection: fix DropCount with DropOldest channel mode; store
  _bufferSize, check Reader.Count >= _bufferSize before write
- SseConnectionManager: add TotalDropCount and Connections properties

New integration tests (21 total; all previously skipped):
- ObserverFakeNodeEndToEndTests: 3 tests (GetSessions, GetNotables,
  GetPhases via HTTP against live Observer stack)
- ObserverRotationIntegrationTests: 4 tests (manifest finalization,
  second-interval queries, concurrent rotation safety, multi-node)
- WebApiQueryRoundTripTests: 8 tests (sessions, event lookup,
  notables, cursor pagination, phase pairing, topology, 404)
- LiveStreamingTests: 6 tests (order, reconnect, drops, multi-node,
  session filter, heartbeat)

Final: 182 unit + 41 integration, 0 failures, 0 skipped
```
