# BATCH-37 Review

**Batch:** BATCH-37 — Phase 7 Entity History: Data Layer, URL State, View Scaffold  
**Tasks:** DT-027 fix, TRC-P7-015, TRC-P7-016, TRC-P7-010  
**Reviewer:** Development Lead  
**Date:** 2026-05-22  
**Status:** ✅ APPROVED

---

## Issues Found

No issues found.

---

## Test Quality Assessment

**`useEntityHistoryQuery.spec.ts` (7 tests)**
- `sequentialThenParallel_SummaryBeforeEvents` — records actual call order and verifies summary is first ✅
- `parallelFetch_AllThreeInFlight` — uses deferred promises + in-flight counter to verify `maxInFlight == 3` ✅
- `switchingEntity_CancelsPriorFetch` — attaches abort listener, verifies `firstAborted === true` ✅
- `errorHandling_SetsStoreError` — verifies `store.error == 'Network error'` and `loading == false` ✅
- All tests verify actual behavior, not just that code compiles. ✅

**`useEntityHistoryUrl.spec.ts` (7 tests)**
- URL→store sync: verifies actual store field values ✅
- Debounced store→URL: verifies `router.replace` called with correct query keys ✅
- Round-trip test: verifies all params preserved across full cycle ✅

**Quality:** Solid. Abort cancellation test pattern is particularly thorough.

---

## Debt Items Added

| ID | Priority | Description |
|----|----------|-------------|
| DT-030 | P3 | `retry()` has no effect without entity change — see BATCH-37 report |
| DT-031 | P3 | `setSummary` uses time equality as "not-set" sentinel — fragile |

---

## Verdict

**Status:** APPROVED. All Phase 7 data layer foundations are solid.

---

## 📝 Commit Message

```
feat(phase7): entity history data layer, URL state, view scaffold (BATCH-37)

Completes TRC-P7-010, TRC-P7-015, TRC-P7-016; fixes DT-027

- Fix DT-027: add FastStateFileLocator.LocateFilesBySafeTopicName to avoid
  double-encoding when resolving Parquet paths; EntityFastStateService updated
- Add entity DTOs and API methods to tracerApiClient (12 DTOs, 8 methods)
- entityHistoryStore: Pinia store with setEntity/setSummary/setTimeRange/retry
- useEntityHistoryQuery: sequential summary → parallel events/slowState/topics;
  AbortController cancellation on entity switch
- useEntityHistoryUrl: bidirectional URL↔store sync with 250ms debounce
- EntityHistoryView: loading/error/panel layout; /v/entity/:entityId route
- Stub components for BATCH-38: EntitySummaryStrip, EntityLifecycleRibbon,
  SlowStateChart, EntityEventStrip, FastStateDrillDown
- 22 frontend unit tests; 183/183 pass; 0 TypeScript errors; 0 C# warnings

Tests: 22 new frontend tests (useEntityHistoryQuery x7, useEntityHistoryUrl x7,
       entityHistoryView x8); backend 39/39 entity tests still passing
```

**Next Batch:** BATCH-38
