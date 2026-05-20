# BATCH-10 Review

**Batch:** BATCH-10  
**Reviewer:** Dev Lead  
**Review Date:** 2026-05-21  
**Report:** `.dev/tracer/reports/BATCH-10-REPORT.md`  
**Status:** ✅ APPROVED

---

## Summary

All three BATCH-10 tasks delivered cleanly. The P1 SSE serialization bug is correctly fixed, the @typescript-eslint upgrade resolves the version range mismatch, and TRC-P3-007 delivers a complete, well-tested Session Browser View. All 11 success conditions (SC1–SC11) are met. No new debt items.

---

## Corrective Task 0 — DT-021: SSE CamelCase Fix ✅

**Quality: GOOD**

The fix is minimal and correct: one private static field added to `SseEndpoints` + one call-site change. The `_sseJsonOptions` field uses `PropertyNamingPolicy = JsonNamingPolicy.CamelCase`, exactly matching the REST API's middleware behavior. The integration test update to `GetProperty("eventId")` correctly reflects the now-consistent casing. No test regressions — all 41 integration tests pass including the updated `MultipleNodes_AllEventsAppearInUnifiedStream`.

**No new debt items.**

---

## Corrective Task 1 — DT-022: @typescript-eslint v8 ✅

**Quality: GOOD**

Upgraded from 6.21.0 to 8.59.4 cleanly. No `.eslintrc.cjs` changes were needed — the `plugin:@typescript-eslint/recommended` extend path is stable across v6→v8. Lint exits 0 with 0 warnings. **DT-022 resolved.**

---

## TRC-P3-007 — Session Browser View ✅

**Quality: GOOD**

### useLiveSse.ts

The composable correctly models the SSE connection lifecycle:
- `onopen` → `setConnected(true)` ✅
- `onmessage` → parse + prepend + cap at 200 + `onEvent()` ✅
- `onclose` → `setConnected(false)` ✅
- `onerror` → `setConnected(false)` + `onReconnect()`, no rethrow (lets fetchEventSource handle backoff) ✅
- `onUnmounted` → `abortCtrl?.abort()` ✅

### SessionBrowserView.vue

All four states handled: loading spinner, error with retry, empty state with the exact text from SC1, and session card grid. The `openSession` function calls `router.push({ name: 'scenario', params: { sessionId: s.sessionId } })` exactly per SC2. The `@retry="load"` binding on `ErrorMessage` satisfies SC3. ✅

### SessionCard.vue

Renders `scenarioId`, formatted `startUtc`, `status` badge with class suffix, `eventCount`, and `participatingNodes.length`. SC4 ✅

### LiveIndicator.vue

Three states (live/stale/disconnected) mapped correctly to CSS classes `live-indicator--live`, `live-indicator--stale`, `live-indicator--disconnected` and text labels "Live"/"Quiet"/"Disconnected". SC6 ✅

### NotableEventsList.vue

Live-first merge with deduplication by `eventId` via `Set`. Loading placeholder and empty state text present. `TransitionGroup` for enter animations. SC7 ✅

### useLiveSse.spec.ts (5 tests)

All five required test method names (SC8) are present:
1. `Connect_SetsLiveStoreConnected` — verifies store state after `onopen`
2. `Message_PrependsEventToList` — verifies first element of events ref
3. `Message_CapsListAt200Events` — sends 201 messages, asserts length = 200
4. `Close_SetsDisconnected` — verifies `connected = false` after `onclose`
5. `Error_IncrementsReconnectAttempts` — verifies `reconnectAttempts` increases

The `withSetup` helper pattern correctly mounts/unmounts the composable with a proper Pinia instance. The `fetchEventSource` mock captures handlers synchronously, allowing direct invocation in tests. ✅

### NotableEventsList.spec.ts (3 tests)

All three required test method names (SC9) are present:
1. `MergesInitialAndLiveEvents_LiveFirst` — asserts exact order [C, A, B]
2. `DeduplicatesEventsByEventId` — single item when X appears in both live and initial
3. `ShowsEmptyState_WhenNoEvents` — asserts "No notable events yet." text

Mock correctly intercepts the `api` singleton imported by `useApi.ts`. ✅

### Playwright E2E stub (SC10)

File exists at `tests/e2e/session-browser.spec.ts` with `loads_and_shows_session_card` navigating to `http://localhost:5300/sessions` and asserting `.session-card` visible within 10s. Correctly gated by `E2E=true` env var. ✅

### Vitest exclude fix

The `tests/e2e/` directory is correctly excluded from Vitest via `exclude: ['tests/e2e/**', 'node_modules/**']` in `vite.config.ts`. Without this, Playwright's `test.describe()` would throw in the Vitest context. ✅

---

## Observations / Minor Items

None requiring debt tracker entries. All design decisions were sound:
- `useApi.ts` shim avoids restructuring `tracerApiClient.ts` while matching the design doc import pattern
- `onerror: () =>` (without parameter) is correct TypeScript — function parameter arity can be reduced
- Using `void result;` to suppress unused-variable lint on destructured values that exist only for type inference is idiomatic

---

## Decision

**APPROVED — proceed to commit and BATCH-11.**

BATCH-11 will implement TRC-P3-008 (Scenario View) followed by TRC-P3-012 (Frontend Component Tests).
