# BATCH-10 Report

**Batch:** BATCH-10  
**Tasks:** DT-021 (Corrective P1), DT-022 (Corrective P2), TRC-P3-007  
**Date:** 2026-05-21  
**Status:** COMPLETE

---

## 1. Summary

BATCH-10 delivered two corrective fixes and the full Session Browser View. DT-021 fixed the SSE serialization bug where `JsonSerializer.Serialize(dto)` bypassed the global CamelCase naming policy, producing PascalCase field names (`EventId`, `TraceId`, `OccurredAtUtc`) that mismatched the TypeScript `NotableEventDto` interface. DT-022 upgraded `@typescript-eslint` from v6 to v8, eliminating the TypeScript 5.4.5 version-range violation. TRC-P3-007 implemented the full Session Browser View: `useLiveNotables` composable, `useApi` shim, `SessionBrowserView`, `SessionCard`, `LiveIndicator`, `NotableEventCard`, `NotableEventsList`, `time.ts` utility, two Vitest spec files (8 new unit tests), and the Playwright E2E stub. The Vitest `exclude` setting was also added to prevent the Playwright spec from being picked up by Vitest. All 224 backend tests pass; all three frontend checks exit 0.

---

## 2. Corrective Task 0 — DT-021: Fix SSE Serialization to camelCase

**Problem:** `SseEndpoints.cs` called `JsonSerializer.Serialize(dto)` without options, producing PascalCase JSON field names. The REST API uses `CamelCase` via `AddJsonOptions`, but SSE uses raw serialization bypassing that middleware.

**Files changed:**

- `src/Tracer.WebApi/Endpoints/SseEndpoints.cs`
  - Lines 11–15: added `private static readonly JsonSerializerOptions _sseJsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };`
  - Line 68: changed `JsonSerializer.Serialize(dto)` → `JsonSerializer.Serialize(dto, _sseJsonOptions)`

- `tests/Tracer.Tests.Integration/LiveStreamingTests.cs`
  - Line 304: changed `GetProperty("EventId")` → `GetProperty("eventId")`

**Verification:** `dotnet test Tracer.sln --configuration Release` → 224 passed, 0 failed.

---

## 3. Corrective Task 1 — DT-022: Upgrade @typescript-eslint to v8

**File changed:** `tracer-viewer/package.json`

| Package | Before | After |
|---|---|---|
| `@typescript-eslint/eslint-plugin` | `6.21.0` | `8.59.4` |
| `@typescript-eslint/parser` | `6.21.0` | `8.59.4` |

No changes to `.eslintrc.cjs` were needed.

**Verification:** `pnpm install` succeeded; `npx eslint . --ext .vue,.ts,.tsx --max-warnings 0` → exit 0.

---

## 4. TRC-P3-007 — Session Browser View

### New/modified files

| File | Action |
|---|---|
| `tracer-viewer/src/utils/time.ts` | Created — `formatTime`, `formatDuration` utilities |
| `tracer-viewer/src/api/useApi.ts` | Created — re-exports `api` singleton via `useApi()` function |
| `tracer-viewer/src/composables/useLiveSse.ts` | Created — `useLiveNotables` composable with SSE lifecycle |
| `tracer-viewer/src/views/SessionBrowserView.vue` | Replaced stub — full implementation |
| `tracer-viewer/src/components/SessionCard.vue` | Created — session summary card |
| `tracer-viewer/src/components/LiveIndicator.vue` | Created — live/stale/disconnected dot indicator |
| `tracer-viewer/src/components/NotableEventCard.vue` | Created — single notable event display card |
| `tracer-viewer/src/components/NotableEventsList.vue` | Created — merged live+initial events with deduplication |
| `tracer-viewer/tests/unit/useLiveSse.spec.ts` | Created — 5 tests |
| `tracer-viewer/tests/unit/NotableEventsList.spec.ts` | Created — 3 tests |
| `tracer-viewer/tests/e2e/session-browser.spec.ts` | Created — Playwright stub (skipped unless `E2E=true`) |
| `tracer-viewer/vite.config.ts` | Updated — added `exclude: ['tests/e2e/**', 'node_modules/**']` to Vitest config to prevent Playwright spec from running under Vitest |

### Spec test methods

**`useLiveSse.spec.ts`** (5 tests, all pass):
1. `Connect_SetsLiveStoreConnected`
2. `Message_PrependsEventToList`
3. `Message_CapsListAt200Events`
4. `Close_SetsDisconnected`
5. `Error_IncrementsReconnectAttempts`

**`NotableEventsList.spec.ts`** (3 tests, all pass):
1. `MergesInitialAndLiveEvents_LiveFirst`
2. `DeduplicatesEventsByEventId`
3. `ShowsEmptyState_WhenNoEvents`

### Design decisions

- **`useApi` shim**: Created `src/api/useApi.ts` as a single-line re-export so `SessionBrowserView` and `NotableEventsList` import `useApi` per the design doc §6.7/§6.10 without restructuring `tracerApiClient.ts`.
- **`onerror` parameter omitted**: The `onerror` callback had no need to inspect the error (backoff handled by `fetchEventSource`), so declared as `() =>` without a parameter to satisfy `@typescript-eslint/no-unused-vars`.
- **`liveStore.ts` unchanged**: `lastEventAt: Date | null` was already present; `onEvent()` already sets it to `new Date()`.
- **`NotableEventCard` field**: Used `event.topic` (matching actual `NotableEventDto` DTO) instead of the draft's `event.eventType`.
- **Vitest exclude**: Added `exclude: ['tests/e2e/**']` to the Vitest config to prevent Playwright specs from being collected by Vitest.

---

## 5. Test Results

### dotnet test (backend)
```
Passed!  - Failed: 0, Passed: 41, Skipped: 0, Total: 41  (integration)
Passed!  - Failed: 0, Passed: 183, Skipped: 0, Total: 183  (unit)
```

### pnpm run build
```
✓ 50 modules transformed. Exit code: 0
```

### pnpm run test:unit (vitest run)
```
Test Files  3 passed (3)
Tests  11 passed (11)
Exit code: 0
```

### pnpm run lint
```
Exit code: 0 | Warnings: 0
```

---

## 6. Suggested Commit Message

```
fix(sse): use CamelCase JsonSerializerOptions for SSE events (DT-021)
fix(deps): upgrade @typescript-eslint to v8 for TS 5.4 support (DT-022)
feat(viewer): implement Session Browser View (TRC-P3-007)

DT-021:
- Add _sseJsonOptions with CamelCase policy to SseEndpoints
- Update LiveStreamingTests to use camelCase eventId property access

DT-022:
- Upgrade @typescript-eslint/eslint-plugin and @typescript-eslint/parser
  from 6.21.0 to 8.59.4

TRC-P3-007:
- Add src/utils/time.ts (formatTime, formatDuration)
- Add src/api/useApi.ts (useApi shim)
- Add src/composables/useLiveSse.ts (useLiveNotables composable)
- Replace SessionBrowserView.vue stub with full implementation
- Add SessionCard, LiveIndicator, NotableEventCard, NotableEventsList
- Add tests/unit/useLiveSse.spec.ts (5 tests)
- Add tests/unit/NotableEventsList.spec.ts (3 tests)
- Add tests/e2e/session-browser.spec.ts (Playwright stub, skipped unless E2E=true)
- Update vite.config.ts to exclude tests/e2e/ from Vitest discovery

Totals: 224 backend tests (183 unit + 41 integration), 11 frontend tests — 0 failures
Build: 50 modules | Lint: exit 0
```

---

## 7. Open Questions

None. All success conditions SC1–SC11 of TRC-P3-007 are satisfied.
