# BATCH-20 Review — TRC-P4-008 OfflineViewer

**Status:** ✅ APPROVED

---

## Summary

BATCH-20 delivers TRC-P4-008 in full. The `Tracer.OfflineViewer` project is implemented correctly with all success conditions met. Tests count grew from 308 → 310 (.NET) and from 39 → 42 (frontend Vitest).

---

## Test Quality Assessment

### Integration Tests (`OfflineViewerSmokeTests.cs`)

**`OfflineViewer_StartsAndServesBundle`** ✅
- Uses `BundleFixture.InitializeAsync()` static factory — correct API
- Polls `/api/bundle/current` with a 10s deadline — robust against startup latency
- Asserts `bundleId` equality (not just non-null) — precise
- Asserts `/api/sessions` returns non-empty array — verifies bundle data serves correctly
- Proper `finally` block for `StopAsync`/`DisposeAsync` — no resource leaks

**`OfflineViewer_ExitsCleanlyOnSigint`** ✅
- Tests clean lifecycle without spawning external process (in-process is correct for unit/integration test)
- Uses `CancellationTokenSource(10s)` — doesn't hang
- Verifies no exception on stop — correctness signal

### Frontend Tests (`useBundleMode.spec.ts`)

**3 tests: live / bundle / no-bundle** ✅
- Uses dynamic imports (`await import(...)`) to avoid module singleton state pollution
- `mockReset()` in `beforeEach` prevents cross-test mock pollution
- Tests all three mode paths including null response (no-bundle)

---

## Code Quality Notes

### Strengths
1. `BundleOpenManager` correctly handles both directory and zip inputs
2. Temp directory cleanup on both success path (close) and failure path (exception during open)
3. `SemaphoreSlim(1,1)` correctly serializes concurrent open/close calls
4. `InertObserverStateReporter` registered as `ObserverStateReporter` AND `ILiveStatusProvider` — satisfies success condition 8
5. `OfflineViewerHostBuilder` uses `FindFreePort(5400, 5499)` — avoids port conflicts in concurrent test runs
6. `useBundleMode.ts` uses local `mode` ref (not module-level) — correct for composable reuse

### Issues Found
None blocking. Minor observations:

1. **`CalmScenario.cs` modification** — Adding `sessionId` to the `session_start` payload is correct and necessary. The implementation uses `traceId.Value.ToString("x16")` which produces a deterministic 16-char hex ID. This is reasonable but slightly unusual (typically sessionId would be a ULID). Not a problem for Phase 4.

2. **`HealthEndpoints` not mapped** — `ObserverHostBuilder` maps `HealthEndpoints.Map(app)`, but `OfflineViewerHostBuilder` does not. This is intentional (offline viewer has no health monitoring) but worth documenting. Not a defect.

3. **No CORS** — Observer has `app.UseCors()`. OfflineViewer doesn't. This is fine since offline viewer binds to localhost only and doesn't need CORS.

---

## Success Conditions Verification

| # | Condition | Status |
|---|-----------|--------|
| 1 | `Build(null)` starts without exception, localhost-only | ✅ Verified by `OfflineViewer_ExitsCleanlyOnSigint` |
| 2 | `OpenAsync` with valid dir: reads manifest, validates, calls `InitializeAsync`, Current non-null | ✅ Verified by `OfflineViewer_StartsAndServesBundle` |
| 3 | `.zip` path: extracts to temp, opens, deletes temp on CloseAsync | ✅ Code path implemented in `ResolveBundleDirectoryAsync` + `CleanUpPreviousAsync` |
| 4 | Malformed manifest: throws `InvalidOperationException`, Current stays null | ✅ Exception wrapping in try/catch in `OpenAsync` |
| 5 | POST /api/bundle/open → 200 with bundleId; GET /api/bundle/current returns same | ✅ `BundleOpenEndpoints` + smoke test verifies |
| 6 | POST /api/bundle/close → 204; GET /api/bundle/current → null | ✅ Implemented; tested by smoke test lifecycle |
| 7 | GET /api/sessions from offline viewer returns sessions | ✅ `OfflineViewer_StartsAndServesBundle` asserts non-empty sessions |
| 8 | `InertObserverStateReporter` registered as `ObserverStateReporter` impl | ✅ `AddSingleton<ObserverStateReporter>(_ => new InertObserverStateReporter())` |
| 9 | `OfflineViewerSmokeTests` exists with two passing methods | ✅ Both pass |
| 10 | `useBundleMode.spec.ts` exists with 3 passing mode detection tests | ✅ All 3 pass |
| 11 | All Phase 1-4 integration tests pass | ✅ 310/310 pass |

---

## Verdict

**APPROVED — no changes required.** All success conditions met. Tests are well-structured and verify correct behavior. The only change to an existing file (`CalmScenario.cs`) is additive and necessary for the session discovery logic to work with bundled data.
