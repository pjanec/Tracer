# BATCH-12 Review

**Batch:** BATCH-12  
**Tasks:** TRC-P3-012, TRC-P3-013  
**Reviewer:** Dev Lead  
**Decision:** APPROVED ✅

---

## Review Summary

BATCH-12 successfully closes Phase 3. Both tasks are complete with thorough test coverage. The implementation is clean, the build and lint pass, and 224 backend tests remain green.

---

## TRC-P3-012 — Frontend Component Tests

### `useScenarioQuery.ts` composable

**Quality: PASS**

- Correctly accepts `Ref<string>` parameter, uses `watch` with `immediate: true` for reactive reload.
- `Promise.all` aggregates all three API calls efficiently.
- Proper `try/catch/finally` — `loading.value = false` is guaranteed.
- `error.value` provides string diagnostic for upstream use.
- No over-engineering — thin wrapper over API with reactive state.

### `useScenarioQuery.spec.ts` (4 tests)

**Quality: PASS**

- `Load_SetsLoadingTrueThenFalse` — correctly tests the in-flight state by injecting a pending promise before resolving. Robust pattern.
- `Load_PopulatesNotablesPhasesAndState` — all three reactive refs are asserted.
- `Load_OnApiError_SetsErrorRefAndClearsLoading` — both error and loading state asserted after rejection.
- `ReactiveSessionId_ReloadsOnChange` — verified call count increases after `sessionId.value` change.
- `withSetup` helper correctly provides Vue app context + Pinia without leaking.

### `SessionCard.spec.ts` (5 tests)

**Quality: PASS**

- All CSS class assertions match the actual component template (`.session-card__scenario`, `.session-card__status`, `.session-card__time`, footer content).
- No API mocking required — component is purely presentational. Correct decision.
- `makeSession` factory avoids duplication.
- `RendersFormattedStartUtc` sensibly checks text length > 0 (locale-agnostic).

### `NotableEventsFeed.spec.ts` (4 tests)

**Quality: PASS**

- `OnMount_CallsGetScenarioNotables_ViaApi` — asserts API called with correct sessionId and page size (100). Good.
- `ApiError_LoadingSetFalse_ListRemainsEmpty` — previously caused unhandled rejection. Bug was caught and fixed in the same batch (catch block added to `NotableEventsFeed.vue`). Demonstrates that the test was actually testing something real.
- `InitialLoad_PopulatesInitialEvents` — asserts card count matches loaded events.
- `LiveAndInitial_MergedInCorrectOrder` — the most complex test. Asserts dedup (B appears once) and order (live first). Solid.

### Bug Fix: `NotableEventsFeed.vue`

**Fix: CORRECT**

Adding a bare `catch {}` is the right decision. The component's contract (TRC-P3-008 SC8) does not mandate an error display — it degrades gracefully to "No notable events yet." The fix is minimal and not over-engineered.

---

## TRC-P3-013 — Playwright E2E Smoke Tests

### `playwright.config.ts` update

**Quality: PASS**

- Conditional `webServer` block using `process.env['E2E'] === 'true'` is idiomatic.
- `reuseExistingServer: true` is correct — tests rely on a running stack, not a managed process.
- `timeout: 30_000` is reasonable for health check polling.
- Using `/api/health` endpoint for readiness probe — correct.
- `webServer: undefined` when E2E is not set — clean, no footgun.

### `scenario-view.spec.ts` (6 tests)

**Quality: PASS**

- All tests gated by `test.skip(skip, '...')` — safe to run in unit CI without a live server.
- Navigation tests (`NavigatesToSessionBrowser_OnRootLoad`, `ClickSessionCard_OpensScenarioView`) use `waitForURL` with `regex` — correct for SPA routing.
- Timeout values are well-calibrated: 10s for session cards, 5s for live indicator, 500ms for first notable event (tight but realistic for a connected stream).
- `PageLoad_Cold_Under2s` uses `performance.timing` — practical cold-load measurement.
- CSS selectors match the component CSS classes from the actual implementation.

---

## Acceptance Criteria Check

### TRC-P3-012
- [x] SC1: `useScenarioQuery.spec.ts` with 4 tests: Load_SetsLoadingTrueThenFalse, Load_PopulatesNotablesPhasesAndState, Load_OnApiError_SetsErrorRefAndClearsLoading, ReactiveSessionId_ReloadsOnChange ✅
- [x] SC2: `SessionCard.spec.ts` with 5 tests ✅
- [x] SC3: `NotableEventsFeed.spec.ts` with 4 tests ✅
- [x] SC4–SC6: All existing tests still pass (39 total, 0 failed) ✅
- [x] SC7: `pnpm run lint` exit 0 ✅

### TRC-P3-013
- [x] SC1: `playwright.config.ts` has conditional webServer block ✅
- [x] SC2: `scenario-view.spec.ts` created in `tests/e2e/` ✅
- [x] SC3–SC6: Tests are E2E-gated by `E2E=true`, compile and exist ✅
- [x] SC7: Tests use CSS selectors from real components ✅
- [x] SC8: `/api/health` used as readiness probe ✅
- [x] SC9: `pnpm run lint` exit 0 ✅

---

## Decision: **APPROVED**

All tasks are complete. No outstanding issues. Phase 3 is fully closed. Proceed with commit and Phase 4 planning.
