# BATCH-06 Review

## Decision: CHANGES REQUIRED

## Summary

BATCH-06 successfully created `Tracer.Observer` and `Tracer.WebApi` assemblies with all core types, and 142 unit + 20 integration tests pass (6 stubs skipped). The `ReadOnlyConnectionPool`, `ObserverStateReporter`, `ObserverHostedService`, and `Tracer.WebApi` infrastructure are well-implemented and tested. However, `ObserverIngestionTests` contains severe test quality failures — 5 of 6 tests are hollow and do not exercise `ObserverIngestionPipeline` at all — and two other tests have missing assertions. These are P1 issues that must be corrected in BATCH-07 before integration test implementation can proceed.

---

## Task Review

### TRC-P3-001 — `Tracer.Observer` Assembly

#### Code Quality ✅
- `ReadOnlyConnectionPool` — correctly implements rotation-awareness, `PooledConnection.DisposeAsync` return-vs-dispose logic, `ObjectDisposedException` guard, and per-spec FIFO semantics. Code matches design §3.9 faithfully.
- `ObserverIngestionPipeline` — implementation matches design §3.7 exactly: `Task.WhenAll` multi-source fan-out, event-only broadcast to `LiveEventBroadcaster`, write failure caught → drop counter incremented → pipeline continues.
- `ObserverStateReporter` + `RollingCounter` — thread-safe, clock-injectable, bucket-based implementation is correct.
- `ObserverHostedService` — startup sequence (recovery → open → pool init → loops) and graceful shutdown (GracefulShutdown rotation with `CancellationToken.None`) are correct.
- `DataSourceComposition` — throws `InvalidOperationException` for unknown kind and empty sources, as specified.

#### P1 — ObserverIngestionTests: 5 of 6 tests do not test the pipeline ❌

This is the primary rejection reason. The test class is named `ObserverIngestionTests` and should test `ObserverIngestionPipeline.RunAsync`. Instead, every test (except `Cancellation_PropagatesCleanly`) either bypasses the pipeline entirely or has no meaningful assertion:

| Test | Problem |
|------|---------|
| `Records_WrittenToCurrentWriter` | Calls `_rotator.CurrentWriter.AppendEventAsync` directly; `ObserverIngestionPipeline` is constructed but `RunAsync` is never called. Asserts `IngestedTotal == 1` after manually calling `state.IncrementIngested()`. Tests nothing about the pipeline. |
| `Events_PublishedToLiveBroadcaster` | Creates a `TestBroadcaster` and calls `broadcaster.Publish(ev)` directly. Pipeline not involved. |
| `SlowState_WrittenButNotBroadcast` | Manually calls `_rotator.CurrentWriter.AppendStateAsync`. Pipeline not involved. |
| `FastState_WrittenViaAppendFastStateAsync` | Manually calls `AppendFastStateAsync`. **Has no assertion** — ends with `await Task.CompletedTask`. Passes unconditionally regardless of implementation correctness. |
| `WriteFailure_IncrementsDropCounter_PipelineContinues` | Calls `state.IncrementDropped()` directly. No pipeline, no fault injection, no write failure simulation. Completely detached from what it claims to test. |
| `Cancellation_PropagatesCleanly` | Uses `Array.Empty<NamedDataSource>()` — no sources → `Task.WhenAll([])` completes instantly regardless of cancellation token. Does not test that an in-progress enumeration is actually cancelled. |

**Correct approach:** Each test must create a fake `IDiagnosticDataSource` (implementing `ReadAsync` with `yield return`) that yields a known set of records, construct the pipeline with it, call `pipeline.RunAsync(ct)` to completion, and then assert on observable outputs (DuckDB count, broadcast count, state reporter counters). A write-failure test needs a fake writer that throws on the first record, then verifies the pipeline continues to process subsequent records.

#### P1 — Integration test stub method names do not match spec ❌

`ObserverFakeNodeEndToEndTests` and `ObserverRotationIntegrationTests` were created with stub method names that differ from those specified in TRC-P3-001 SC14/SC15 and TRC-P3-009. Method names must match exactly so BATCH-09 can implement the bodies.

**Required names per TRC-P3-001 SC14:**
- `GetSessions_ReturnsActiveSession`
- `GetScenarioNotables_ReturnsNotablesFromScenario`
- `GetScenarioPhases_ReturnsActivePhaseName`

**Actual names created:**
- `Observer_QueryApi_ReturnsIngestedEvents`
- `Observer_HealthEndpoint_Returns200_WhenLive`
- `Observer_ReceivesFakeNodeEvents_PersistsToStorage`

**Required names per TRC-P3-001 SC15:**
- `FirstInterval_FinalizedWithReady_AfterRotation`
- `SecondInterval_QueriesReturnCurrentIntervalEvents`
- `Queries_DuringRotation_SucceedAfterBriefBlock`

**Actual names created:**
- `Observer_RotatesInterval_WritesManifest`
- `Observer_ConnectionPool_RefreshesOnRotation`
- `Observer_RetentionDeletesOldIntervals`

All stubs must be renamed to match the spec.

#### P2 — `OnGracefulShutdown_FinalRotationHasGracefulReason` has no assertion ⚠️

The test body ends with `await Task.CompletedTask` — it verifies nothing about the rotation reason. It should read the manifest from disk after `StopAsync` and assert `FinalizationReason == GracefulShutdown`.

### TRC-P3-002 — `Tracer.WebApi` Project Setup ✅

- `ApiExceptionMiddleware` correctly returns 400 for `ArgumentException`, 500 for all others, no stack traces.
- `ProblemDetailsFactory` maps exceptions to status codes correctly.
- `GET /api/health` returns 200 + `{"status":"ok"}`.
- `GenerateTypeScriptClient` MSBuild target is Debug-only with `ContinueOnError="true"`.
- All stub endpoint/DTO classes compile cleanly.
- Health and ProblemDetails unit tests are correct and meaningful.

### ReadOnlyConnectionPoolTests ✅
All 6 tests use a real DuckDB file and make meaningful assertions. The `OnIntervalRotated_BorrowedConnectionDisposesOnReturn` test could additionally assert that the pool is at full capacity after the rotated-away connection is returned (verifying dispose-vs-return), but the current test is acceptable.

### ObserverStateReporterTests ✅
All 5 tests are correct. The rolling-counter window tests use `SimulatedClock` properly and verify actual bucket expiry.

### ObserverHostedServiceTests — 4 of 5 acceptable
`OnStart_RecoveryRunsBeforeIntervalOpen`, `OnStart_PoolInitializedAfterIntervalOpen`, `PoolRefreshFailure_Logged_HostNotCrashed`, and `OnStart_ServiceStartsWithoutException` are reasonable given the constraints of testing `BackgroundService`. `OnGracefulShutdown_FinalRotationHasGracefulReason` must be fixed (see P1 above).

---

## Debt Tracker Updates

| ID | Priority | Source | Description | Target |
|----|----------|--------|-------------|--------|
| DT-010 | P1 (→ corrective) | BATCH-06 | `ObserverIngestionTests`: 5 of 6 tests bypass the pipeline; none call `RunAsync` with a real fake data source | BATCH-07 corrective task |
| DT-011 | P1 (→ corrective) | BATCH-06 | Integration test stub method names don't match TRC-P3-001 SC14/SC15 spec | BATCH-07 corrective task |
| DT-012 | P2 | BATCH-06 | `OnGracefulShutdown_FinalRotationHasGracefulReason` has no assertion — `await Task.CompletedTask` end | BATCH-07 |
