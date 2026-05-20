# BATCH-08: Corrective Route Fix + Backend Integration Tests

**Batch Number:** BATCH-08  
**Tasks:** Corrective (DT-013, DT-014, DT-015), TRC-P3-009, TRC-P3-010, TRC-P3-011  
**Phase:** Phase 3 — TracerObserver, Web API, Session Browser & Scenario View  
**Estimated Effort:** 16–18 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-07 (Observer + WebApi + SSE all implemented)

---

## 📋 Onboarding & Workflow

### Developer Instructions

This batch has two goals:
1. **Corrective tasks** — Fix three issues from BATCH-07 review (one P1 route deviation that breaks all downstream tests, two P2 test-quality gaps). **Start here; do not proceed to integration tests until all correctives are in and tests pass.**
2. **Backend integration tests** — Implement the three sets of integration test stubs that were deferred in BATCH-07: Observer+FakeNode end-to-end tests (TRC-P3-009), Web API query round-trip tests (TRC-P3-010), and Live Streaming integration tests (TRC-P3-011).

### Required Reading (IN ORDER)

1. **Workflow:** `.github/skills/developer/SKILL.md`
2. **BATCH-07 Review:** `.dev/tracer/reviews/BATCH-07-REVIEW.md` — understand what DT-013/014/015 are and why they matter
3. **Task Definitions:** `docs/TASK-DETAIL.md` — TRC-P3-009, TRC-P3-010, TRC-P3-011 (all success conditions)
4. **Phase 3 Design:** `docs/tracer_phase3_design.md` — §8.2 Backend Integration Tests, §5 Live Streaming via SSE
5. **Debt Tracker:** `.dev/tracer/DEBT-TRACKER.md` — DT-013, DT-014, DT-015

### Source Code Locations

- **Scenario endpoint (route fix):** `src/Tracer.WebApi/Endpoints/ScenarioEndpoints.cs`
- **Scenario unit tests:** `tests/Tracer.Tests.Unit/WebApi/ScenarioEndpointTests.cs`
- **Session unit tests:** `tests/Tracer.Tests.Unit/WebApi/SessionEndpointTests.cs`
- **ObserverFixture (TestHarness):** `src/Tracer.TestHarness/Observer/ObserverFixture.cs`
- **Integration test stubs (to implement):**
  - `tests/Tracer.Tests.Integration/ObserverFakeNodeEndToEndTests.cs`
  - `tests/Tracer.Tests.Integration/ObserverRotationIntegrationTests.cs`
  - `tests/Tracer.Tests.Integration/WebApiQueryRoundTripTests.cs`
  - `tests/Tracer.Tests.Integration/LiveStreamingTests.cs`

### Run Tests

```powershell
cd d:\Work\Tracer
dotnet test Tracer.sln --configuration Release
```

### Report Submission

`.dev/tracer/reports/BATCH-08-REPORT.md`

If you have questions: `.dev/tracer/questions/BATCH-08-QUESTIONS.md`

---

## ⚡ MANDATORY: No Stopping

Complete every task in sequence. Fix compile errors and test failures immediately — do NOT write the report until `dotnet test` exits with 0 failures. Do not ask for permission to run tests, fix root causes, or proceed to the next task. The report is written only after all tests pass.

---

## 🔄 MANDATORY WORKFLOW

Execute in strict sequence:

1. **Corrective Task 0** (DT-013) → fix routes → all tests pass ✅
2. **Corrective Task 1** (DT-014) → fix pagination test → all tests pass ✅
3. **Corrective Task 2** (DT-015) → add ordering test → all tests pass ✅
4. **TRC-P3-009** → implement integration tests → all tests pass ✅
5. **TRC-P3-010** → implement round-trip tests → all tests pass ✅
6. **TRC-P3-011** → implement live streaming tests → all tests pass ✅
7. Write report ✅

---

## ✅ Tasks

---

### Corrective Task 0 — Fix Scenario Endpoint Routes (DT-013, P1)

**This is a P1 blocker. All integration tests in this batch depend on the correct routes.**

**Problem:** `ScenarioEndpoints.cs` uses `/api/scenarios/{sessionId}/notables` (plural `scenarios`, session ID as path segment) instead of the spec routes `/api/scenario/notables?sessionId=...` (singular `scenario`, session ID as query parameter). See BATCH-07 review §TRC-P3-004 for the full analysis.

**Files to change:**

#### `src/Tracer.WebApi/Endpoints/ScenarioEndpoints.cs`

Change all three routes to use query-parameter `sessionId` on the singular `/api/scenario/` prefix:

```csharp
app.MapGet("/api/scenario/notables", async (
    [FromQuery] string sessionId,
    [FromQuery] int? limit,
    [FromQuery] string? before,
    ScenarioQueryService svc,
    CancellationToken ct) => ...
```

```csharp
app.MapGet("/api/scenario/phases", async (
    [FromQuery] string sessionId,
    ScenarioQueryService svc,
    CancellationToken ct) => ...
```

```csharp
app.MapGet("/api/scenario/state", async (
    [FromQuery] string sessionId,
    ScenarioQueryService svc,
    CancellationToken ct) => ...
```

The handler logic inside each lambda is unchanged — only the route template and parameter binding style changes.

#### `tests/Tracer.Tests.Unit/WebApi/ScenarioEndpointTests.cs`

Update every URL string in the test file to match the new route pattern:

| Old | New |
|-----|-----|
| `/api/scenarios/{sessionId}/notables?limit=50` | `/api/scenario/notables?sessionId={sessionId}&limit=50` |
| `/api/scenarios/{sessionId}/notables?limit=10` | `/api/scenario/notables?sessionId={sessionId}&limit=10` |
| `/api/scenarios/{sessionId}/notables?limit=10&before={id}` | `/api/scenario/notables?sessionId={sessionId}&limit=10&before={id}` |
| `/api/scenarios/any-session/notables?limit=1000` | `/api/scenario/notables?sessionId=any-session&limit=1000` |
| `/api/scenarios/{sessionId}/phases` | `/api/scenario/phases?sessionId={sessionId}` |
| `/api/scenarios/{sessionId}/state` | `/api/scenario/state?sessionId={sessionId}` |

After this change, `dotnet test` must still show 0 failures (the endpoint tests pass with updated URLs).

---

### Corrective Task 1 — Strengthen Pagination Test (DT-014)

**File:** `tests/Tracer.Tests.Unit/WebApi/ScenarioEndpointTests.cs`  
**Method:** `GetNotables_PaginationWithBeforeCursor`

**Current problem:** The test pushes 5 notables, gets the last event ID, calls with `before={eventId}`, then only asserts `StatusCode == 200`. It does not verify the returned subset.

**Fix:** The test must assert:
1. The result with `before=` has **fewer items** than the result without `before=`
2. **Every** item in the paginated result has `occurredAtUtc` strictly less than the timestamp of the `before` cursor event

Concretely, push 5 notable events with distinct, ascending timestamps (e.g., spaced 1 second apart). Get all 5 without `before`. Extract the `occurredAtUtc` of the 3rd event (index 2 from descending order = the middle event). Call with `before={eventId of 3rd event}`. Assert:
- Response count < 5
- All items have `occurredAtUtc < timestamp-of-the-3rd-event`

---

### Corrective Task 2 — Add Session Ordering Test (DT-015)

**File:** `tests/Tracer.Tests.Unit/WebApi/SessionEndpointTests.cs`  
**New method:** `ListSessions_OrderedByStartTimeDesc`

Push two `system.session_start` events with distinct `publishWallclock` values (one 10 minutes before the other) and distinct `sessionId` values. Call `GET /api/sessions`. Assert that:
- The response contains exactly 2 sessions
- The session with the later `startUtc` appears first (index 0) in the returned array

---

### Task 3 — TRC-P3-009: Observer+FakeNode Integration Tests

**Task Definition:** `docs/TASK-DETAIL.md` §TRC-P3-009 (all 10 success conditions)  
**Design:** `docs/tracer_phase3_design.md` §8.2

#### What's Already There

Two test classes exist with deferred stubs:
- `tests/Tracer.Tests.Integration/ObserverFakeNodeEndToEndTests.cs` — 3 stubs (TRC-P3-009 SC2–SC4)
- `tests/Tracer.Tests.Integration/ObserverRotationIntegrationTests.cs` — 4 stubs (TRC-P3-009 SC6–SC9)

The `ObserverFixture` (in `src/Tracer.TestHarness/Observer/ObserverFixture.cs`) provides everything needed:
- `PushAsync(EventRecord)` / `PushAsync(IEnumerable<EventRecord>)` — inject events directly into DuckDB and SSE broadcast
- `ForceRotationAsync()` — rotate interval and refresh pool
- `Client` — `HttpClient` wired to the TestServer
- `Pool`, `Broadcaster`, `SseConnections`, `StateReporter` — direct service access

#### ObserverFixture SimulatedClock Support

The `ObserverRotationIntegrationTests` spec (SC5) requires a `SimulatedClock` to control time. The current `ObserverFixture` registers `SystemClock`. You **must** add optional clock injection to `ObserverFixtureOptions` and `ObserverFixture.CreateAsync`:

**`src/Tracer.TestHarness/Observer/ObserverFixture.cs`** — add to `ObserverFixtureOptions`:

```csharp
/// <summary>Override the IClock implementation. Defaults to SystemClock.</summary>
public Tracer.Core.Time.IClock? Clock { get; set; }
```

In `CreateAsync`, change the clock registration to:

```csharp
if (options.Clock is not null)
    builder.Services.AddSingleton<Tracer.Core.Time.IClock>(options.Clock);
else
    builder.Services.AddSingleton<Tracer.Core.Time.IClock, Tracer.Agent.Time.SystemClock>();
```

`SimulatedClock` is in `src/Tracer.Adapters.Mock/SimulatedClock.cs`.

#### ObserverFakeNodeEndToEndTests.cs

Replace the three deferred stubs with real implementations. The class uses `IAsyncLifetime` to create a shared `ObserverFixture` (created once per class via `InitializeAsync`, disposed in `DisposeAsync`). Push events via `_fixture.PushAsync(...)`.

**Test 1 — `GetSessions_ReturnsActiveSession`** (SC2):
- Push a `system.session_start` event with a known `sessionId` in its JSON payload and topic `"system.session_start"`
- Call `GET /api/sessions` and deserialize the JSON array
- Assert: exactly one session, `status == "Active"`, `sessionId` matches the pushed value

**Test 2 — `GetScenarioNotables_ReturnsNotablesFromScenario`** (SC3):
- Push a `system.session_start` event (same sessionId), then push at least one event with `NotableLabel != null` and the sessionId in its JSON payload
- Call `GET /api/scenario/notables?sessionId={id}` (spec route — after DT-013 fix above)
- Assert: response is a non-empty array; every item has a non-null `notableLabel`

**Test 3 — `GetScenarioPhases_ReturnsActivePhaseName`** (SC4):
- Push a `scenario.phase_started` event with `{ "sessionId": "...", "phaseName": "Alpha" }` in its JSON payload, topic `"scenario.phase_started"`, no matching `scenario.phase_ended`
- Call `GET /api/scenario/phases?sessionId={id}`
- Assert: response contains at least one `ScenarioPhaseDto` with `status == "Active"` and `phaseName == "Alpha"`

**Helper:** Build `EventRecord` instances the same way `ScenarioEndpointTests` does (see `tests/Tracer.Tests.Unit/WebApi/ScenarioEndpointTests.cs` for the `MakeNotable`, `MakePhaseStarted` pattern to copy or share from a base helper class).

#### ObserverRotationIntegrationTests.cs

Replace the four deferred stubs. This class also uses `IAsyncLifetime`. Use `SimulatedClock` injected via `ObserverFixtureOptions.Clock` (once you add that support above).

**Key note:** `ForceRotationAsync()` already exists on the fixture. After rotation, the `ReadOnlyConnectionPool` is refreshed so queries target the new interval. You do not need to advance the `SimulatedClock` to trigger rotation — call `ForceRotationAsync()` directly.

**Test 4 — `FirstInterval_FinalizedWithReady_AfterRotation`** (SC6):
- Push 100 events into the fixture (use `PushAsync` with a list)
- Call `await _fixture.ForceRotationAsync()`
- Read the manifest file from the closed interval directory (use `ManifestWriter.ReadAsync(path)`)
- Assert `manifest.Status` contains `"_ready"` (or check `FinalizationReason == ScheduledRotation`)
- The manifest file path is `Path.Combine(_fixture.DataRoot, "<first-interval-dir>", "manifest.json")`; find the closed interval dir as the one directory under DataRoot that is NOT the current one (or enumerate `Directory.GetDirectories(_fixture.DataRoot)` before and after rotation)

**Test 5 — `SecondInterval_QueriesReturnCurrentIntervalEvents`** (SC7):
- After the rotation from Test 4 (or do setup fresh), push 100 more events into the now-current interval
- Query `GET /api/sessions` (or a direct DuckDB query via the pool) — assert 100 results in the current interval and 0 events from the first interval's time range bleed into the second query (i.e., filter by publish time if needed)
- Better approach: push all 100 second-interval events with `publishWallclock` after the rotation boundary, then assert event count via the HTTP `GET /api/topology` endpoint: the `eventsPublished` counter equals exactly 100 for the second interval

**Test 6 — `Queries_DuringRotation_SucceedAfterBriefBlock`** (SC8):
- Start a concurrent `Task.Run` that calls `GET /api/sessions` in a loop for 2000ms
- Simultaneously call `ForceRotationAsync()`
- Assert the query task completes without throwing and all HTTP responses are valid (200 or empty result — not 500)

**Test 7 — `MultipleNodes_EventsFromAllNodesIngested`** (SC9):
- Push 50 events with `PublisherNode = new AgentId("node-alpha")` and 50 events with `PublisherNode = new AgentId("node-beta")` via two separate `PushAsync` calls
- Call `GET /api/topology` and deserialize to `TopologyDto`
- Assert: `nodes.Count == 2`, each node has `eventsPublished == 50`

---

### Task 4 — TRC-P3-010: Web API Query Round-Trip Integration Tests

**Task Definition:** `docs/TASK-DETAIL.md` §TRC-P3-010 (all 10 success conditions)  
**File:** `tests/Tracer.Tests.Integration/WebApiQueryRoundTripTests.cs`

#### IMPORTANT: Stub Method Names Are Wrong

The current stubs have different method names than the spec requires. You must **replace the entire class body** with the spec-compliant implementations. The old method names are irrelevant — implement using the names from the TRC-P3-010 success conditions (SC2–SC9).

```
New method names (from TASK-DETAIL.md §TRC-P3-010):
  GetSessions_AfterIngestion_ReturnsCorrectSessions
  GetSession_ById_ReturnsMatchingDto
  GetScenarioNotables_ReturnsOnlyNotableEvents_WithCorrectFields
  GetScenarioNotables_BeforeCursor_ReturnsSubset
  GetScenarioPhases_PairsStartAndEnd
  GetEvent_ById_ReturnsCorrectEventDto
  GetEvent_UnknownId_Returns404
  GetTopology_AfterIngestion_ReturnsNodeInfo
```

#### Class Structure

```csharp
public sealed class WebApiQueryRoundTripTests : IAsyncLifetime
{
    private ObserverFixture _fixture = null!;

    public async Task InitializeAsync()
        => _fixture = await ObserverFixture.CreateAsync();

    public async Task DisposeAsync()
        => await _fixture.DisposeAsync();
    
    // 8 test methods
}
```

Each test pushes events via `_fixture.PushAsync(...)` and then calls HTTP endpoints via `_fixture.Client` to assert the response matches the pushed data. Use the same `EventRecord` construction pattern from the unit tests. Read all 10 success conditions from TASK-DETAIL.md §TRC-P3-010 carefully — they define exactly what to push, what to call, and what to assert.

**Important details:**
- SC2 asserts descending ordering by `startUtc` — push two sessions with different times and verify index 0 is the later one
- SC5 (pagination cursor test): Push ten notables with ascending timestamps. Call with `limit=3&before={eventId-of-midpoint-event}`. Assert the response has exactly 3 events AND all events have `occurredAtUtc` strictly before the midpoint event's timestamp.
- SC6 (phase pairing): Push a paired `scenario.phase_started` + `scenario.phase_ended` pair (same sessionId, same phaseName in payload) and an unpaired `scenario.phase_started`. Assert one has `status == "Completed"` with a non-null `endedAtUtc`, and one has `status == "Active"` with null `endedAtUtc`.
- SC7 (event lookup): The event ID in the URL must be the 16-char uppercase hex form. Use `EventId.ToString()` (which formats as X16) to build the URL.
- SC8 (404): Use a valid 16-char hex value that does not correspond to any pushed event.

---

### Task 5 — TRC-P3-011: Live Streaming Integration Tests

**Task Definition:** `docs/TASK-DETAIL.md` §TRC-P3-011 (all 8 success conditions)  
**File:** `tests/Tracer.Tests.Integration/LiveStreamingTests.cs`

#### Current Stubs (3 stubs to implement + 3 new tests to add)

The existing stubs are: `PushNotableEvents_AppearOnStreamInOrder`, `ClientReconnect_ReceivesNewEventsAfterReconnect`, `SlowClient_DropsCountedButStreamRemainsAlive`.

New tests to add: `MultipleNodes_AllEventsAppearInUnifiedStream`, `SessionFilter_ExcludesEventsFromOtherSession`, `Heartbeat_ReceivedWithinConfiguredInterval`.

#### Class Structure

```csharp
public sealed class LiveStreamingTests : IAsyncLifetime
{
    private ObserverFixture _fixture = null!;

    public async Task InitializeAsync() => _fixture = await ObserverFixture.CreateAsync(
        sseOptions: new SseStreamingOptions 
        { 
            HeartbeatInterval = TimeSpan.FromSeconds(1),
            MaxConcurrentSseClients = 50,
            PerClientBufferSize = 20
        });

    public async Task DisposeAsync() => await _fixture.DisposeAsync();
}
```

#### Implementing SSE Tests Pattern

To connect to the SSE stream and read from it in tests:

```csharp
// 1. Open a streaming connection
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
using var request = new HttpRequestMessage(HttpMethod.Get, 
    $"/api/live/notables?sessionId={sessionId}");
request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/event-stream"));
using var response = await _fixture.Client.SendAsync(request, 
    HttpCompletionOption.ResponseHeadersRead, cts.Token);
using var stream = await response.Content.ReadAsStreamAsync();
using var reader = new StreamReader(stream);

// 2. Background reading task
var lines = new System.Collections.Concurrent.ConcurrentBag<string>();
var readTask = Task.Run(async () =>
{
    while (!cts.Token.IsCancellationRequested)
    {
        var line = await reader.ReadLineAsync(cts.Token).ConfigureAwait(false);
        if (line is null) break;
        if (line.Length > 0) lines.Add(line);
    }
}, cts.Token);

// 3. Push events and await
await Task.Delay(50); // brief pause to ensure reader is ready
await _fixture.PushAsync(events);

// 4. Wait for expected events or timeout
await Task.Delay(500);
cts.Cancel();
try { await readTask; } catch (OperationCanceledException) { }
```

#### Test Implementations

Read all success conditions from `docs/TASK-DETAIL.md` §TRC-P3-011 (SC2–SC7). Key implementation notes:

- **SC2 `PushNotableEvents_AppearOnStreamInOrder`**: Push 5 notables (all with `NotableLabel != null`) in order; collect `data:` lines; assert 5 `data:` lines received within 500ms; optionally deserialize and check order by `occurredAtUtc`.

- **SC3 `ClientReconnect_ReceivesNewEventsAfterReconnect`**: Open stream, push one event, see it appear, cancel + dispose stream. Open a new stream connection. Push a second event. Assert the second event appears in the new stream (the server does not throw/hang on the reconnect).

- **SC4 `SlowClient_DropsCountedButStreamRemainsAlive`**: Open a stream but do NOT read from it (don't start the background reader). Push 50 events (beyond the `PerClientBufferSize = 20`). Use `_fixture.SseConnections` to find the active connection and check `connection.DropCount > 0`. Then start reading — push one more event — assert it appears within 1s (stream is still alive).

- **SC5 `MultipleNodes_AllEventsAppearInUnifiedStream`**: Push 10 notables with `PublisherNode = "node-alpha"` and 10 with `PublisherNode = "node-beta"` concurrently via `Task.WhenAll`. Assert all 20 distinct `eventId` values appear in the stream within 1000ms.

- **SC6 `SessionFilter_ExcludesEventsFromOtherSession`**: Connect to `/api/live/notables?sessionId=session-A`. Push 3 notables for sessionId=A and 3 notables for sessionId=B (different `sessionId` in payload JSON). Assert the stream contains data lines only for session-A events (verify by deserializing and checking `payloadJson`).

- **SC7 `Heartbeat_ReceivedWithinConfiguredInterval`**: With `HeartbeatInterval = TimeSpan.FromSeconds(1)` in options, connect and do NOT push any events. Read lines; assert a `: keepalive` comment line is received within 1500ms.

---

## 🧪 Testing Requirements

**Mandatory test counts by the end of BATCH-08:**
- All currently-passing tests must continue to pass
- DT-013 corrective: net-zero (routes renamed; tests updated — same count, all pass)
- DT-014 corrective: `GetNotables_PaginationWithBeforeCursor` gets meaningful assertions
- DT-015 corrective: 1 new unit test in `SessionEndpointTests`
- TRC-P3-009: 7 integration tests converted from stubs to real tests (0 skipped)
- TRC-P3-010: 8 integration tests replacing old stubs (0 skipped)
- TRC-P3-011: 6 integration tests (3 stubs implemented + 3 new; 0 skipped)

**`dotnet test` must exit with 0 failures, 0 errors, 0 skipped tests for TRC-P3-009/010/011.**

---

## 📊 Report Requirements

File: `.dev/tracer/reports/BATCH-08-REPORT.md`

Include:
- **Test count summary:** total unit, total integration, before/after
- **Corrective items:** confirm DT-013/014/015 resolved with evidence
- **Per-task status:** TRC-P3-009 / P3-010 / P3-011 — done/partial/blocked
- **Developer Insights:**
  - Q1: What issues did you hit during implementation? How did you resolve them?
  - Q2: Did you spot any weak points in the existing fixtures or query services?
  - Q3: What design decisions did you make beyond the spec? Any alternatives considered?
  - Q4: What edge cases did you discover that weren't mentioned in the spec?
  - Q5: Are there any performance concerns or test reliability issues noticed?
- **Suggested commit message**

---

## 🎯 Success Criteria

This batch is DONE when:

- [x] `ScenarioEndpoints.cs` uses `/api/scenario/notables?sessionId=`, `/api/scenario/phases?sessionId=`, `/api/scenario/state?sessionId=` (query params, singular prefix)
- [x] All existing unit tests still pass with updated routes
- [x] `GetNotables_PaginationWithBeforeCursor` asserts subset correctness, not just 200 status
- [x] `ListSessions_OrderedByStartTimeDesc` test added to `SessionEndpointTests`
- [x] All 7 TRC-P3-009 integration tests have real implementations (no `Skip =`)
- [x] `WebApiQueryRoundTripTests` has 8 real tests with spec-aligned method names (no `Skip =`)
- [x] All 6 TRC-P3-011 live streaming tests have real implementations (no `Skip =`)
- [x] `dotnet test Tracer.sln --configuration Release` exits 0 failures, 0 skipped for integration tests

---

## ⚠️ Common Pitfalls

1. **DT-013 route fix affects all query tests** — fix it first; if skipped, every integration test calling scenario endpoints will 404.
2. **`WebApiQueryRoundTripTests` stub method names are wrong** — do not implement the stubs as-is; replace with the spec names from TASK-DETAIL.md §TRC-P3-010.
3. **SSE tests require `HttpCompletionOption.ResponseHeadersRead`** — without it, `SendAsync` will wait for the entire response before returning (never, for SSE).
4. **Rotation tests need `ForceRotationAsync()`** — do not try to advance a simulated clock to trigger `IntervalScheduler`; call the fixture method directly.
5. **`ObserverFixtureOptions.Clock` is new** — you must add this property to `ObserverFixtureOptions` and wire it in `CreateAsync` before `ObserverRotationIntegrationTests` can inject `SimulatedClock`.
6. **Phase pairing in `ScenarioQueryService`** — a `scenario.phase_ended` event must have a `phaseName` in its payload that matches the `phaseName` of the corresponding `scenario.phase_started`. Check `ScenarioQueryService.GetPhasesAsync` to understand the exact field names.
7. **JSON deserialization in tests** — use `System.Text.Json.JsonSerializer.Deserialize<JsonElement>` or typed DTOs. Match the casing that `[JsonPropertyName]` / camelCase policy produces.

---

## 📚 Reference Materials

- **Task Definitions:** `docs/TASK-DETAIL.md` — §TRC-P3-009, §TRC-P3-010, §TRC-P3-011
- **Phase 3 Design:** `docs/tracer_phase3_design.md` — §5 (SSE), §8.2 (integration tests)
- **BATCH-07 Review:** `.dev/tracer/reviews/BATCH-07-REVIEW.md` — corrective context
- **Existing unit test patterns:** `tests/Tracer.Tests.Unit/WebApi/ScenarioEndpointTests.cs`, `SessionEndpointTests.cs`
- **ObserverFixture:** `src/Tracer.TestHarness/Observer/ObserverFixture.cs`
- **ScenarioQueryService:** `src/Tracer.WebApi/Queries/ScenarioQueryService.cs`
- **ScenarioEndpoints (current):** `src/Tracer.WebApi/Endpoints/ScenarioEndpoints.cs`
