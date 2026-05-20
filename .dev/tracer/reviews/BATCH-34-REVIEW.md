# BATCH-34 Review — TRC-P6-011 & TRC-P6-012 Gap Tests

**Date:** 2025-07  
**Reviewer:** Dev Lead  
**Batch:** BATCH-34 (gap-fill tests for Phase 6 completion)

---

## Summary

BATCH-34 successfully closes all remaining test gaps for TRC-P6-011 (backend unit & integration) and TRC-P6-012 (frontend). All Phase 6 tasks are now complete.

The sub-agent did implement all changes but did not rebuild before reporting the test counts (354 unit / 161 frontend), so the preliminary report showed stale counts of 351/157. A rebuild confirmed actual counts.

One defect was introduced in `CausalTreeRoundTripTests.cs`: the test was assigned collection `"CausalTreeRoundTrip"` instead of `"TimelineRoundTrip"`, causing a parallel port-binding race condition with `TimelineRoundTripTests`. Both use `OfflineViewerHostBuilder.Build()` which calls `FindFreePort(5400, 5499)` — a TOCTOU window allowed both to pick port 5400 simultaneously. Fixed by changing collection to `"TimelineRoundTrip"`.

---

## Test Counts (Final Phase 6)

| Suite | Before BATCH-34 | After BATCH-34 |
|---|---|---|
| Backend unit | 351 | 354 (+3) |
| Backend integration | 79 | 80 (+1) |
| Frontend Vitest | 157 | 161 (+4) |
| Frontend E2E | existing | +3 new spec file |

---

## Files Modified / Created

| File | Change |
|---|---|
| `tests/Tracer.Tests.Unit/WebApi/TraceQueryServiceTests.cs` | +2 tests: `ConvergentDag`, `CrossIntervalTrace` |
| `tests/Tracer.Tests.Unit/WebApi/TraceWalkerTests.cs` | +1 test: `100Children_AllReturnedInSingleBfsBatch` |
| `tests/Tracer.Tests.Integration/CausalTreeRoundTripTests.cs` | NEW — 1 round-trip test (live+bundle) |
| `tracer-viewer/tests/unit/causalTreeLayout.spec.ts` | +1 test: `CycleDefense_ReturnsWithoutHanging` |
| `tracer-viewer/tests/unit/causalTreeHitTest.spec.ts` | +1 test: `ClickAtRadiusMinusOne_StillReturnsNode` |
| `tracer-viewer/tests/unit/useCausalTreeQuery.spec.ts` | +2 tests: `requestKindEvent`, `requestKindDescendants` |
| `tracer-viewer/tests/e2e/causal-tree-view.spec.ts` | NEW — 3 Playwright E2E smoke tests |

---

## Test Quality Assessment

### Backend Unit Tests

**TraceQueryServiceTests — ConvergentDag:** Tests a 3-event trace where two events are roots (A, B) and one (C) is a child of A. Verifies 3 nodes, 1 edge (A→C), 2 roots, 2 leaves. The test comment accurately documents the single-parent-pointer limitation — a true DAG convergence (two parents per child) is architecturally impossible with the current schema. The test validates the multi-root trace topology. ✓

**TraceQueryServiceTests — CrossIntervalTrace:** Pushes 5 events, calls `ForceRotationAsync()`, pushes 5 more on the same trace ID. Asserts all 10 nodes and 9 edges returned, including the cross-interval edge. This is the most critical Phase 6 test — it validates that `LiveMultiIntervalReader`'s multi-file UNION query stitches the trace correctly across interval boundaries. ✓

**TraceWalkerTests — 100Children:** Pushes 100 direct children of one root, calls `WalkDescendantsAsync(maxDepth=1, maxNodes=200)`, asserts count=100 and performance <1s. The performance bound indirectly validates that FetchChildrenAsync uses a batched IN-clause (100 individual queries would be noticeably slower). ✓

### Integration Test

**CausalTreeRoundTripTests — LiveAndBundleResponses_AreStructurallyIdentical:** Full round-trip: build 10-event star trace (1 root → 9 children), force rotation, copy to NAS, build bundle, start OfflineViewer, query `/api/traces/{id}/tree` on both live and bundle. Assert identical node IDs, edges, root IDs, leaf IDs. This is the gold standard integration test for Phase 6. ✓

Port conflict fix: `[Collection("TimelineRoundTrip")]` serializes with `TimelineRoundTripTests`. ✓

### Frontend Unit Tests

**causalTreeLayout — CycleDefense:** Constructs a two-node cycle (A→B→A), calls `layout()`, asserts completion within 1000ms and exactly 2 nodes with unique keys. Directly tests the `visiting` set in `computeLayer()`. ✓

**causalTreeHitTest — ClickAtRadiusMinusOne:** Queries at exactly `radius - 1` from node center (inside boundary). Tests the `d2 < bestDist` condition (strict less-than with `bestDist = radius * radius`). The test correctly verifies inclusive boundary: at `radius - 1` offset, `d2 = (radius-1)^2 < radius^2 = bestDist`. ✓

**useCausalTreeQuery — requestKindEvent / requestKindDescendants:** These were the two missing request-kind tests. Both mock the API via `vi.mock`, set `store.request`, flush promises, and assert the correct API method called with correct parameters. Matches the pattern of the existing `requestKindTrace` and `requestKindAncestors` tests. ✓

### E2E Playwright Tests

Three smoke tests added:
1. Canvas and `.trace-summary` visible after navigation to `/v/causal/{eventId}`
2. `TraceSearchInput` accepts hex ID and navigates to new URL
3. Invalid hex input shows `.trace-search__error`

Tests use known test event IDs (`0000000000000001`) which assume a dev server with seeded data. These are correctly marked as E2E-only (not run in Vitest). ✓

---

## Phase 6 Completion Status

All 12 TRC-P6 tasks are complete. TASK-TRACKER.md updated with `[x]` for all TRC-P6-001 through TRC-P6-012.

**Commit:** `tests: BATCH-34 gap tests (TRC-P6-011 & TRC-P6-012, 354 unit / 161 frontend / +1 integration)`
