# BATCH-40 Review

**Batch:** BATCH-40 — Phase 7 Cross-View Navigation Pivots + Entity Picker  
**Tasks:** TRC-P7-018, TRC-P7-019  
**Reviewer:** Development Lead  
**Date:** 2026-05-22  
**Status:** ✅ APPROVED

---

## Issues Found

No issues found.

---

## Test Quality Assessment

**`EventInspector.spec.ts` (3 new tests)**
- Button absent by default (`showEntityHistoryPivot=false`) ✅
- Button present when prop enabled ✅  
- Click triggers `router.push` to entity-history route with correct entityId ✅

**`entityHistoryView.spec.ts` (4 new tests)**
- Timeline pivot: verifies `router.push` called with correct route/query ✅
- Causal tree pivot enabled/disabled: verifies button disabled when no trace (traceId='0') ✅
- Pivot absent when no event selected ✅

**`EntityPickerView.spec.ts` (7 tests)**
- `loadsEntityList_DisplaysItems` — mocks API, verifies entity IDs rendered ✅
- `showsLoadingState` / `showsEmptyState` / `showsErrorState` — UI states ✅
- `filterInput_FiltersDisplayedEntities` — types filter text, verifies filtered count ✅
- `clickEntityRow_NavigatesToEntityHistory` — verifies router.push with entityId ✅
- `topicsOverflow_HidesExtra` — topics list capped at visible count ✅

**Quality:** Navigation pivot tests verify actual router call arguments. Entity picker tests check concrete rendered item counts after filtering.

---

## Debt Items Added (P3 — no corrective action needed)

| ID | Priority | Description |
|----|----------|-------------|
| DT-035 | P3 | EntityPickerView: no @retry on ErrorMessage |
| DT-036 | P3 | SessionCard.spec.ts: missing RouterLinkStub |
| DT-037 | P3 | router.spec.ts: entity-picker route not covered |

---

## Verdict

**Status:** APPROVED. Cross-view navigation thoroughly tested with router call verification.

---

## 📝 Commit Message

```
feat(phase7): cross-view pivots + EntityPickerView (BATCH-40)

Completes TRC-P7-018, TRC-P7-019

- EventInspector: showEntityHistoryPivot prop; conditional entity-history button;
  getEntityId() duck-type guard for TraceNodeDto vs ApiEventDto
- CausalTreeView: enable entity-history pivot on EventInspector
- EntityHistoryView: timeline + causal-tree pivot buttons for selected event;
  causal-tree button disabled when traceId='0'
- router: entity-picker route at /v/entities/:sessionId
- EntityPickerView: entity list, client-side filter, loading/error/empty states,
  click-to-entity-history navigation
- SessionCard: Entities RouterLink in footer
- 14 new tests across 3 spec files; 244/244 passing; 0 TypeScript errors
```

**Next Batch:** BATCH-41
