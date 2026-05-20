# BATCH-11 Report

**Batch:** BATCH-11  
**Tasks:** TRC-P3-008  
**Date:** 2026-05-21  
**Status:** COMPLETE

---

## 1. Summary

BATCH-11 delivered the full Scenario View for TRC-P3-008. Four new components were created (`ScenarioPhaseBanner`, `ScenarioStatePanel`, `NotableEventsFeed`, and the replacement `ScenarioView`), along with three Vitest spec files covering 15 new test methods. The design-doc reference `PhaseTimeline` was replaced by `ScenarioPhaseBanner` and `NotableEventsList` was replaced by `NotableEventsFeed` per the task detail. One `vue/singleline-html-element-content-newline` lint issue in `ScenarioStatePanel.vue` was fixed by expanding inline `<div>` content to multiline. One test assertion was corrected (`loadingspinner-stub` → `loading-spinner-stub`, matching Vue's kebab-case stub rendering). All 224 backend tests pass; 26 frontend tests pass; build and lint both exit 0.

---

## 2. TRC-P3-008 — Scenario View

### New/modified files

| File | Action |
|---|---|
| `tracer-viewer/src/views/ScenarioView.vue` | Replaced stub — full implementation |
| `tracer-viewer/src/components/ScenarioStatePanel.vue` | Created |
| `tracer-viewer/src/components/ScenarioPhaseBanner.vue` | Created |
| `tracer-viewer/src/components/NotableEventsFeed.vue` | Created |
| `tracer-viewer/tests/unit/ScenarioView.spec.ts` | Created — 6 tests |
| `tracer-viewer/tests/unit/ScenarioStatePanel.spec.ts` | Created — 6 tests |
| `tracer-viewer/tests/unit/ScenarioPhaseBanner.spec.ts` | Created — 3 tests |

### Spec test methods

**`ScenarioView.spec.ts`** (6 tests, all pass):
1. `Load_CalledWithSessionId_OnMount`
2. `Load_CalledAgain_OnSessionIdChange`
3. `RefreshTimer_InvokesRefreshState_Every5s`
4. `RefreshTimer_ClearedOnUnmount`
5. `ShowsSpinner_WhileLoadingNoSession`
6. `ShowsGrid_WhenSessionIsLoaded`

**`ScenarioStatePanel.spec.ts`** (6 tests, all pass):
1. `ShowsCurrentPhase`
2. `ShowsElapsedTime`
3. `NullState_ShowsDashes`
4. `StatusActive_AppliesActiveClass`
5. `StatusCompleted_AppliesCompletedClass`
6. `RendersAllParticipatingNodes`

**`ScenarioPhaseBanner.spec.ts`** (3 tests, all pass):
1. `RendersOneRowPerPhase`
2. `ActivePhase_OmitsEndTime`
3. `CompletedPhase_ShowsFormattedEndTime`

### Design decisions

- **`ScenarioPhaseBanner` instead of `PhaseTimeline`**: Design §6.8 references `PhaseTimeline.vue` (not yet created), but TRC-P3-008 task detail specifies `ScenarioPhaseBanner` as the component for phase display. `ScenarioPhaseBanner` fetches phases internally via `getScenarioPhases(session.sessionId)` on mount and on `session.sessionId` change.
- **`NotableEventsFeed` instead of `NotableEventsList`**: The `NotableEventsList` component was delivered in BATCH-10 (TRC-P3-007) and is used by `SessionBrowserView`. `NotableEventsFeed` is the scenario-specific variant, identical in merge/dedup logic but with a different CSS class prefix (`notables-feed` vs `notables-list`), so its API-fetch and error behavior can be tested independently in TRC-P3-012.
- **`sessionStore.refreshState` already exists**: The store already had `refreshState()` with no args, reading `this.current.sessionId` — no changes to `sessionStore.ts` were needed.
- **Stub rendering in Vue Test Utils**: Vue Test Utils renders component stubs in kebab-case (`loading-spinner-stub`, `live-indicator-stub`), not PascalCase. The `ShowsSpinner` test was corrected accordingly.
- **`vue/singleline-html-element-content-newline`**: The ESLint rule requires that content inside block elements not appear on the same line as the tag. All inline `<div>Content</div>` patterns in `ScenarioStatePanel.vue` were expanded to multiline.

---

## 3. Test Results

### dotnet test (backend)
```
Passed!  - Failed: 0, Passed: 41, Skipped: 0, Total: 41  (integration)
Passed!  - Failed: 0, Passed: 183, Skipped: 0, Total: 183  (unit)
```

### pnpm run build
```
✓ 72 modules transformed. Exit code: 0
```

### pnpm run test:unit (vitest run)
```
Test Files  6 passed (6)
Tests  26 passed (26)
Exit code: 0
```
(scaffold 3 + useLiveSse 5 + NotableEventsList 3 + ScenarioPhaseBanner 3 + ScenarioStatePanel 6 + ScenarioView 6 = 26)

### pnpm run lint
```
Exit code: 0 | Warnings: 0
```

---

## 4. Suggested Commit Message

```
feat(viewer): implement Scenario View (TRC-P3-008)

- Add ScenarioPhaseBanner.vue (phase list with active/completed rows)
- Add ScenarioStatePanel.vue (status, elapsed, current phase, nodes)
- Add NotableEventsFeed.vue (scenario-specific live+initial merge feed)
- Replace ScenarioView.vue stub with full implementation:
  - sessionStore.load on mount + prop watch
  - 5s refreshState interval, cleared on unmount
  - useLiveNotables SSE connection
  - Loading/grid states
- Add tests/unit/ScenarioPhaseBanner.spec.ts (3 tests)
- Add tests/unit/ScenarioStatePanel.spec.ts (6 tests)
- Add tests/unit/ScenarioView.spec.ts (6 tests)

Totals: 224 backend tests (183 unit + 41 integration), 26 frontend tests — 0 failures
Build: 72 modules | Lint: exit 0
```

---

## 5. Open Questions

None. All 12 success conditions SC1–SC12 of TRC-P3-008 are satisfied.
