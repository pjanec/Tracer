# BATCH-40 Report — Phase 7: Cross-View Navigation Pivots + Entity Picker

**Batch:** BATCH-40  
**Tasks:** TRC-P7-018, TRC-P7-019  
**Status:** ✅ Complete

---

## 1. Summary

Both tasks implemented fully. 14 new tests added; all 244 tests pass (0 failures). TypeScript strict mode passes with no errors.

---

## 2. Files Changed

### Modified
| File | Change |
|------|--------|
| `tracer-viewer/src/components/EventInspector.vue` | Added `showEntityHistoryPivot` prop, `getEntityId()` helper, `showEntityHistoryButton` computed, `pivotToEntityHistory()` handler; replaced disabled stub button with conditional `v-if` button |
| `tracer-viewer/src/views/CausalTreeView.vue` | Added `:show-entity-history-pivot="true"` to EventInspector usage |
| `tracer-viewer/src/views/EntityHistoryView.vue` | Added `useRouter`, `selectedEvent` computed, `pivotToTimeline()`, `pivotToCausalTree()`, `canPivotToCausal` computed; added pivot actions block in template |
| `tracer-viewer/src/router/index.ts` | Added `entity-picker` route at `/v/entities/:sessionId` |
| `tracer-viewer/src/components/SessionCard.vue` | Added `<RouterLink>` "Entities" link in footer with `@click.stop` |
| `tracer-viewer/tests/unit/EventInspector.spec.ts` | Updated stale disabled-button test; added 3 entity history pivot tests (SC-1..3) |
| `tracer-viewer/tests/unit/entityHistoryView.spec.ts` | Added `vi.mock('vue-router', ...)` preserving real `createRouter`; added `makeEntityEvent` helper; added 4 pivot tests (SC-4..7 equiv) |

### Created
| File | Description |
|------|-------------|
| `tracer-viewer/src/views/EntityPickerView.vue` | Full entity picker view: filter input, entity list, loading/error/empty states, click-to-navigate |
| `tracer-viewer/tests/unit/EntityPickerView.spec.ts` | 7 tests covering SC-1..7 (entity list, loading, empty, filter, click navigation, topics overflow, SessionCard link) |

---

## 3. Test Results

```
Test Files  50 passed (50)
     Tests  244 passed (244)  [230 pre-existing + 14 new]
```

**New tests breakdown:**
- `EventInspector.spec.ts`: +3 (entity history button visibility, navigation)
- `entityHistoryView.spec.ts`: +4 (timeline pivot, causal tree pivot enabled/disabled, pivot absent with no selection)
- `EntityPickerView.spec.ts`: +7 (loads entities, loading state, empty state, filter, click navigation, topics overflow, SessionCard entities link)

---

## 4. TypeScript Status

`pnpm tsc --noEmit` — **no errors**

---

## 5. Design Decisions

1. **`getEntityId` type guard in `EventInspector`:** `displayEvent` is typed as `TraceNodeDto | ApiEventDto | null`. Only `TraceNodeDto` has `entityId`. Used a runtime duck-type guard (`'entityId' in event`) rather than a discriminated union or casting, which avoids `any` and is safe for both types.

2. **vue-router mock strategy in `entityHistoryView.spec.ts`:** Used `vi.mock('vue-router', async (importOriginal) => { ...actual, useRouter: mock })` to preserve `createRouter`/`createWebHistory` for the existing `entityHistory router` describe block. The existing `vi.resetModules()` + dynamic import still resolves to mocked vue-router, but with real router factory functions intact.

3. **SessionCard entities link placement:** Added `<RouterLink>` directly inside `SessionCard.vue` footer with `@click.stop` per the instructions. The alternative (wrapping in `SessionBrowserView`) was rejected because the link is semantically part of the card.

4. **API method name:** Used `api.listEntities(sessionId)` (found in `tracerApiClient.ts`) — not `getEntityList` as the instructions tentatively named it.

5. **Existing `eventInspector_showsEntityHistory_buttonPresentButDisabled` test:** Renamed and updated to `eventInspector_showsEntityHistory_buttonAbsentByDefault` because the disabled stub is now fully removed. The new behaviour (button absent when `showEntityHistoryPivot=false`) is the correct post-TRC-P7-018 state.

6. **`SessionCard.spec.ts` warning:** The existing `buildBundle_showsProgressThenDownloadLink` test emits a Vue warn `[Vue warn]: Failed to resolve component: RouterLink`. This is expected — the test does not install the router plugin, so RouterLink is unresolvable. The warning is non-fatal; the test still passes. No fix attempted to avoid touching pre-existing test structure.

---

## 6. Issues Encountered

- **None blocking.** One anticipated issue: the `entityHistoryView.spec.ts` file imports the real router in the last describe block. Adding `vi.mock('vue-router')` without `importOriginal` would have broken `createRouter` in that block. Resolved with `importOriginal` factory pattern.

---

## 7. Weak Points Spotted

- **No `EntityPickerView` error-retry button:** The view shows an `<ErrorMessage>` but `ErrorMessage` emits `retry` — however, `EntityPickerView` doesn't handle the retry event (no `@retry` handler wired up). Users see the error message but can only refresh the page to retry.
- **SessionCard RouterLink warning in tests:** The existing `SessionCard.spec.ts` doesn't provide a router plugin and now generates a Vue warning on every run. A follow-up could add `RouterLinkStub` to that test's global stubs.
- **No `@/views/EntityPickerView.vue` in router test:** The `router.spec.ts` only checks the `entity-history` route. The new `entity-picker` route is not covered by that existing test.

---

## 8. New Debt Items

| ID | Priority | Description |
|----|----------|-------------|
| DT-035 | P3 | `EntityPickerView` — add `@retry="load"` on ErrorMessage to allow retry without page refresh |
| DT-036 | P3 | `SessionCard.spec.ts` — add `RouterLinkStub` to global stubs to suppress RouterLink resolution warning |
| DT-037 | P3 | `router.spec.ts` — add assertion that `entity-picker` route resolves to `/v/entities/:sessionId` |

---

## 9. Suggested Commit Message

```
feat(phase7): cross-view pivots + EntityPickerView (TRC-P7-018, TRC-P7-019)

- EventInspector: add showEntityHistoryPivot prop; replace disabled stub with
  conditional entity-history pivot button; getEntityId() type guard for mixed
  TraceNodeDto/ApiEventDto event type
- CausalTreeView: enable entity-history pivot on EventInspector
- EntityHistoryView: add "Show in timeline" + "Show causal tree" pivot buttons
  for selected event; causal tree button disabled when traceId='0'
- router: add entity-picker route at /v/entities/:sessionId
- EntityPickerView: new view with entity list, client-side filter, loading/error/
  empty states, click-to-entity-history navigation
- SessionCard: add Entities RouterLink to footer (@click.stop)
- Tests: 14 new tests; 244/244 passing; 0 TypeScript errors
```
