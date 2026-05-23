# BATCH-58 Review — SavedViews Export, Slow-State Bundle Fix, SharedMemory Drops, Health Metrics

**Date**: 2026-05-23  
**Batch**: BATCH-58  
**Sub-agent**: Default (Claude)  
**Reviewer**: Dev Lead

---

## Summary

All 4 tasks (B1, B5, I1, I4) implemented. 8 already-done tasks (E2, E5, F1, F2, F3, I2, I5, I8) checked off in tracker.

**Build**: 0 errors, 0 warnings  
**Tests**: 811 unit (↑10 from 801), 106 integration — all passing

---

## B1 — Export Saved Views in Aggregator ✅ APPROVED

### Production Code

**`AggregationStage.SavedViewsExported`** — correctly added after `AnnotationsExported`. Enum ordering matches the pipeline execution order.

**`SavedViewsExporter.cs`** — clean clone of `AnnotationsExporter.cs` pattern. Uses `SavedViewFilter { SessionId = sessionId, Limit = int.MaxValue }` correctly. Returns early on empty list (no file created). JSON options consistent with annotations (camelCase + enum string converter).

**`AggregationOrchestrator.cs`** — `ISavedViewStore? savedViewStore = null` as 4th optional parameter is correct (binary-compatible with existing callers). Step 7c correctly placed after 7b, fires `SavedViewsExported` stage.

**`Tracer.Aggregator.csproj`** — project reference to `Tracer.Storage.SavedViews` added at line 20.

### Tests

**`SavedViewsExporterTests.ExportAsync_WhenStoreHasViews_WritesJsonFile`** — asserts file exists, camelCase keys present, both record IDs in JSON, enum as string. Solid assertions. ✓

**`ExportAsync_WhenStoreIsEmpty_DoesNotCreateFile`** — correctly tests the early-return path. ✓

**`AggregationOrchestrator_WithSavedViewStore_FiresSavedViewsExportedStage`** — full integration test creating a real interval zip, running the full aggregation pipeline, and asserting the stage appears. Excellent depth. `FakeSavedViewStore` has correct session-ID filtering logic.

---

## B5 — Slow-State Bundle Fix ✅ APPROVED

### Production Code

**`PooledMultiIntervalConnection`** — `_slowStateAliases: IReadOnlyList<string>?` field added. Constructor extended. `BuildSlowStateUnionSql` uses `_slowStateAliases ?? _aliases` — clean fallback pattern.

**`BuildMemoryConnectionAsync`** — correctly detects the separate-file case: `!string.Equals(ssPath, EventsDbPath, OrdinalIgnoreCase)` plus `File.Exists(ssPath)`. Attaches slow_state.duckdb under `ss_{timestamp}` alias. Passes parallel `slowStateAliases` list to constructor.

**Existing construction sites** (`BuildCoordinatorAsync`, `BuildWorkerAsync`) — correctly pass `null` for `slowStateAliases`, preserving live-mode behaviour where `slow_state` is in the same file as `events`.

### Tests

**`BuildSlowStateUnionSql_WithSlowStateAliases_UsesSlowStateAliases`** — directly tests the SQL building logic. Asserts `ss_aaa.slow_state` present and `iv_aaa.slow_state` absent. ✓

**`BuildSlowStateUnionSql_WithNullSlowStateAliases_FallsBackToAliases`** — verifies backward-compatibility for live intervals. ✓

**`BuildMemoryConnectionAsync_WhenSlowStateFileExists_AttachesSeparately`** — real end-to-end test creating actual DuckDB files, building a full reader+tracker, acquiring a connection, and asserting SQL uses the `ss_` alias. High quality.

**Minor observation**: The `NotContain("FROM iv_")` assertion is technically weaker than intended since the actual alias is `FROM db_iv_...` not `FROM iv_`. However, the positive assertion `Contain("ss_")` is meaningful and catches the regression. Not a blocker.

---

## I1 — SharedMemory Drop Telemetry ✅ APPROVED

### Production Code

**`SharedMemoryTransport.cs`** — `private long _totalDropped;` field added. `Interlocked.Exchange(ref _totalDropped, reader.GetDroppedCount())` called after each batch (correctly placed OUTSIDE the per-record loop to avoid N individual Interlocked calls). `GetHealth()` uses `Interlocked.Read(ref _totalDropped)`. Thread-safe implementation.

### Tests

**`GetHealth_Initially_ReturnsTotalDroppedZero`** — single test confirming initial state. Appropriate given that `SharedMemoryReader` connects to a real OS shared memory segment and cannot be unit-tested without the actual infrastructure. No over-engineering.

---

## I4 — Expand /api/health Endpoint Metrics ✅ APPROVED

### Production Code

**`HealthEndpoints.cs`** — adds `SseConnectionManager?` and `UploadIntentDispatcher?` as optional `[FromServices]` parameters. Returns `sseConnectionsActive` and `intervalsAwaitingUpload` with null-safe `?? 0` fallback. Using directives added.

### Tests

**`GetHealth_WithAllServicesNull_ReturnsZeroMetrics`** — calls endpoint with only the default `WebApiFixture` (no `UploadIntentDispatcher`); asserts all 5 fields present with correct zero values. Good baseline test. ✓

**`GetHealth_WithSseManager_ReturnsActiveCount`** — registers 3 real `SseConnection` instances via `_fixture.SseConnections.TryRegister(new SseFilter())` and asserts `sseConnectionsActive == 3`. Uses actual `SseConnectionManager` from the DI container, no mocking. ✓

**`GetHealth_WithUploadDispatcher_ReturnsPendingCount`** — sophisticated test: creates a blocking `LambdaUploadService` that never completes, dispatches 5 times concurrently, waits via `SemaphoreSlim` for all 5 to reach the blocking point, then asserts `intervalsAwaitingUpload == 5`. Properly cleans up via `allowComplete.SetResult()`. This is excellent test design — it accurately tests concurrent dispatch behavior. ✓

---

## Verdict

**APPROVED** — all changes are correct, minimal, and well-tested. No rework required.

Test quality highlights:
- B1 test 3: full aggregation integration test is exemplary
- B5 test 3: real DuckDB files + full reader lifecycle is thorough
- I4 test 3: concurrent dispatch blocking test is high quality

One minor weakness (B5 `NotContain("FROM iv_")` assertion) documented but not blocking.
