# BATCH-47 Report

**Batch:** BATCH-47  
**Tasks:** TRC-P8-014 (SavedViewsView + SaveViewButton), TRC-P8-015 (BookmarkBar + useBookmarks), TRC-P8-016 (TriggerEvalView + TriggerEvalRow)  
**Completed:** 2026-05-22  
**Status:** ✅ All tasks complete, all tests passing

---

## Files Created / Modified

| File | Action |
|------|--------|
| `src/api/tracerApiClient.ts` | Modified — added SavedViewKind, SavedViewDto, CreateSavedViewDto, UpdateSavedViewDto, TriggerEvaluationDto, TriggerEvaluationListDto interfaces; added listSavedViews, createSavedView, deleteSavedView, recordSavedViewOpened, listTriggerEvaluations methods |
| `src/components/SaveViewButton.vue` | Created |
| `src/views/SavedViewsView.vue` | Created |
| `src/composables/useBookmarks.ts` | Created |
| `src/components/BookmarkBar.vue` | Created |
| `src/components/TriggerEvalRow.vue` | Created |
| `src/views/TriggerEvalView.vue` | Created |
| `src/router/index.ts` | Modified — added `/v/saved-views/:sessionId` and `/v/triggers/:sessionId` routes |
| `tests/unit/SaveViewButton.spec.ts` | Created — 7 tests |
| `tests/unit/SavedViewsView.spec.ts` | Created — 5 tests |
| `tests/unit/useBookmarks.spec.ts` | Created — 4 tests |
| `tests/unit/BookmarkBar.spec.ts` | Created — 4 tests |
| `tests/unit/TriggerEvalView.spec.ts` | Created — 7 tests |
| `tests/unit/TriggerEvalRow.spec.ts` | Created — 5 tests |

---

## Test Results

| Suite | Tests | Result |
|-------|-------|--------|
| SaveViewButton.spec.ts | 7/7 | ✅ |
| SavedViewsView.spec.ts | 5/5 | ✅ |
| useBookmarks.spec.ts | 4/4 | ✅ |
| BookmarkBar.spec.ts | 4/4 | ✅ |
| TriggerEvalView.spec.ts | 7/7 | ✅ |
| TriggerEvalRow.spec.ts | 5/5 | ✅ |
| **New Total** | **32/32** | ✅ |
| **Full Suite** | **319/319** | ✅ No regressions |

---

## Issues & Resolutions

### TS6133 Unused variable in BookmarkBar.spec.ts
- **Issue:** The `BookmarkBar_ReloadsOnPersonaChange` test mounted with `const wrapper = mount(...)` but `wrapper` was never read — vue-tsc flagged it.
- **Resolution:** Removed the `wrapper` variable assignment (used `mount(...)` without assigning the result).

### Pre-existing TS2322 errors (not introduced by this batch)
- `AnnotationEditor.spec.ts`, `AnnotationList.spec.ts`, `AnnotationMarker.spec.ts` all have `setActivePinia(createPinia())` in `beforeEach` which returns `Pinia` instead of `void`. This is a pre-existing type issue from earlier batches. Not introduced by BATCH-47.

---

## Design Decisions

1. **`SavedViewsView` persona filter** — Used a local `personaFilter` ref instead of reading from the persona store directly, so the view can show all personas at once when "All" is selected. The persona store is used as a default in `useBookmarks`.

2. **`TriggerEvalRow` table fragment** — Since `TriggerEvalRow` renders multiple `<tr>` elements (one main row + optional expansion row), it is used with `v-for` at the `TriggerEvalView` `<tbody>` level. Vue 3 supports multiple root elements in `<template>` for components used within `<table>`.

3. **`BookmarkBar` v-if** — Used `v-if` on the `<nav>` root element (not `v-show`) as required, ensuring no empty element is rendered when there are no bookmarks.

4. **Result pill classes** — Exactly `trigger-eval-view__pill--Fired` and `trigger-eval-view__pill--NotFired` using `:class` dynamic binding with template literal `` `trigger-eval-view__pill--${evaluation.result}` ``.

5. **`useBookmarks` composable** — The `buildAutoLabel` helper is a module-level function (not inside the composable) to avoid capturing the route reference at call time rather than at use time.

---

## Weak Points Observed

1. The `TriggerEvalRow_TriggerIdFilter_Refetches` test (test 5 of TriggerEvalRow) is actually testing `TriggerEvalView` behavior — it was specified this way in the instructions. This is a minor test organization inconsistency.

2. `SavedViewsView` re-fetches on persona select change via `@change` event. This means keyboard navigation in the select fires the reload correctly. However the filter is a local override of the persona store — views created under different personas won't show unless "All" is selected. This matches the design intent.

3. The 3 pre-existing `TS2322` errors in annotation test files (`setActivePinia` returning `Pinia` instead of `void`) should be tracked as tech debt for a future batch.

---

## Suggested Commit Message

```
feat(frontend): BATCH-47 — SavedViews, BookmarkBar, TriggerEvalView (P8-014/015/016)

- Add SavedViewDto, CreateSavedViewDto, TriggerEvaluationDto DTOs and API methods
  to tracerApiClient (listSavedViews, createSavedView, deleteSavedView,
  recordSavedViewOpened, listTriggerEvaluations)
- Implement SaveViewButton.vue (bookmark + save-view dialog with inline form)
- Implement SavedViewsView.vue (/v/saved-views/:sessionId, grouped by viewType,
  persona filter, open/delete actions)
- Implement useBookmarks composable (bookmarkCurrentUrl, listBookmarks, removeBookmark)
- Implement BookmarkBar.vue (chip list, v-if hide when empty, persona-reactive reload)
- Implement TriggerEvalRow.vue (table row, result pill, expandable inputs, nav buttons)
- Implement TriggerEvalView.vue (/v/triggers/:sessionId, filter by triggerId/result,
  loading/empty states)
- Add routes: saved-views, triggers
- 32 new unit tests — 319/319 total passing
```
