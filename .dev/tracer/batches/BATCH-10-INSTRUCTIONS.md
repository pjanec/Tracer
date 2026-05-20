# BATCH-10: SSE Camelcase Fix + Session Browser View

**Batch Number:** BATCH-10  
**Tasks:** Corrective (DT-021, DT-022), TRC-P3-007  
**Phase:** Phase 3 — Vue SPA: Live Connection + Session Browser  
**Estimated Effort:** 12–16 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-09 (Vue scaffold in `tracer-viewer/` complete, 224 backend tests green)

---

## 📋 Onboarding & Workflow

### Developer Instructions

This batch has two goals:
1. **Corrective tasks** — Fix two issues from the BATCH-09 review. DT-021 is **P1 blocking**: the SSE endpoint serializes JSON with PascalCase field names, but the TypeScript DTOs expect camelCase. The `useLiveNotables` composable in TRC-P3-007 will silently receive `undefined` for every field unless this is fixed first. DT-022 upgrades `@typescript-eslint` to a version that officially supports TypeScript 5.4. **Complete both correctives before starting TRC-P3-007.**
2. **TRC-P3-007** — Implement the Session Browser View: the `useLiveNotables` composable, `SessionBrowserView`, `SessionCard`, `LiveIndicator`, and `NotableEventsList` components, plus their Vitest spec files and the Playwright E2E stub.

### Required Reading (IN ORDER)

1. **Workflow:** `.github/skills/developer/SKILL.md`
2. **BATCH-09 Review:** `.dev/tracer/reviews/BATCH-09-REVIEW.md` — understand DT-021 and DT-022
3. **Task Definitions:** `docs/TASK-DETAIL.md` — TRC-P3-007 (all 11 success conditions)
4. **Phase 3 Design:** `docs/tracer_phase3_design.md` — §6.6 `useLiveSse` Composable, §6.7 Session Browser View, §6.11 `LiveIndicator`, §6.10 `NotableEventsList`
5. **Debt Tracker:** `.dev/tracer/DEBT-TRACKER.md` — DT-021, DT-022

### Source Code Locations

**Backend (DT-021 fix):**
- `src/Tracer.WebApi/Endpoints/SseEndpoints.cs` — SSE serializer options (line ~57–62)
- `tests/Tracer.Tests.Integration/LiveStreamingTests.cs` — update PascalCase `GetProperty("EventId")` to camelCase `GetProperty("eventId")` in `MultipleNodes_AllEventsAppearInUnifiedStream`

**Frontend scaffold (read before writing):**
- `tracer-viewer/src/api/tracerApiClient.ts` — existing typed DTOs (`NotableEventDto`, `SessionDto`, etc.)
- `tracer-viewer/src/stores/liveStore.ts` — `setConnected`, `onEvent`, `onReconnect` actions
- `tracer-viewer/src/stores/sessionStore.ts` — `load`, `refreshState`, `clear`
- `tracer-viewer/src/components/ErrorMessage.vue` — existing component (uses `message` prop + `retry` emit)
- `tracer-viewer/src/components/LoadingSpinner.vue` — existing component
- `tracer-viewer/src/views/SessionBrowserView.vue` — **stub only**; replace with full implementation
- `tracer-viewer/src/router/index.ts` — 3 routes already defined (`/`, `/sessions`, `/scenario/:sessionId`)
- `tracer-viewer/package.json` — package versions (upgrade `@typescript-eslint` here)
- `tracer-viewer/.eslintrc.cjs` — ESLint config (update parser/plugin version reference if needed)

**New files to create (TRC-P3-007):**
- `tracer-viewer/src/composables/useLiveSse.ts` — `useLiveNotables` composable
- `tracer-viewer/src/components/SessionCard.vue`
- `tracer-viewer/src/components/LiveIndicator.vue`
- `tracer-viewer/src/components/NotableEventsList.vue`
- `tracer-viewer/src/components/NotableEventCard.vue`
- `tracer-viewer/tests/unit/useLiveSse.spec.ts`
- `tracer-viewer/tests/unit/NotableEventsList.spec.ts`
- `tracer-viewer/tests/e2e/session-browser.spec.ts` ← Playwright E2E stub

### Run Tests

**Backend:**
```powershell
cd d:\Work\Tracer
dotnet test Tracer.sln --configuration Release
```

**Frontend:**
```powershell
cd d:\Work\Tracer\tracer-viewer
pnpm run build
pnpm run test:unit
pnpm run lint
```

### Report Submission

`.dev/tracer/reports/BATCH-10-REPORT.md`

If you have questions: `.dev/tracer/questions/BATCH-10-QUESTIONS.md`

---

## ⚡ MANDATORY: No Stopping

Complete every task in sequence. Fix compile errors and test failures immediately — do NOT write the report until `dotnet test` (0 failures) **and** all three frontend checks pass. The report is written only after all checks are green.

---

## 🔄 MANDATORY WORKFLOW

Execute in strict sequence:

1. **Corrective Task 0** (DT-021) — Fix SSE camelCase + update LiveStreamingTests → `dotnet test` 0 failures ✅
2. **Corrective Task 1** (DT-022) — Upgrade `@typescript-eslint` → `pnpm run lint` exit 0 ✅
3. **TRC-P3-007** — Implement `useLiveNotables`, components, and specs → all frontend checks pass ✅
4. Write report ✅

---

## ✅ Tasks

---

### Corrective Task 0 — Fix SSE Serialization to camelCase (DT-021, P1)

**This is a P1 blocker. The `useLiveNotables` composable in TRC-P3-007 will receive `undefined` on every field access if the SSE data is PascalCase.**

**Problem:** `SseEndpoints.cs` calls `JsonSerializer.Serialize(dto)` without options, producing `{"EventId":...,"TraceId":...,"OccurredAtUtc":...}`. The REST API uses `JsonNamingPolicy.CamelCase` via `builder.Services.AddControllers().AddJsonOptions(...)`, but SSE is raw-string serialization bypassing that middleware. The TypeScript `NotableEventDto` interface has camelCase fields, so all SSE data arrives with key mismatches.

#### Fix 1: `src/Tracer.WebApi/Endpoints/SseEndpoints.cs`

Add a private static field for the serializer options, and use it when serializing for SSE:

```csharp
private static readonly JsonSerializerOptions _sseJsonOptions = new()
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
};
```

Find the line that calls `JsonSerializer.Serialize(dto)` (in the SSE writer lambda, approximately line 57–62) and change it to:

```csharp
JsonSerializer.Serialize(dto, _sseJsonOptions)
```

The field should be declared at class scope (if `SseEndpoints` is a class) or at the top of the method as a static local (if it's a `static` method). Use whichever pattern is already in that file — do not add a class wrapper if the file uses top-level `static` methods.

After this fix, a backend-only `dotnet test` must still pass with 0 failures.

#### Fix 2: `tests/Tracer.Tests.Integration/LiveStreamingTests.cs`

In `MultipleNodes_AllEventsAppearInUnifiedStream`, the line that currently reads:

```csharp
.GetProperty("EventId")
```

must be changed to:

```csharp
.GetProperty("eventId")
```

(and any other property accesses in that test that currently use PascalCase must be updated to camelCase to match the new SSE output).

After this change, `dotnet test` must still exit with 0 failures.

---

### Corrective Task 1 — Upgrade @typescript-eslint (DT-022, P2)

**Problem:** `@typescript-eslint/eslint-plugin` v6 and `@typescript-eslint/parser` v6 support TypeScript `<5.4.0`; the project uses TypeScript `5.4.5`, which is outside the supported range. This can cause stale lint results or unexpected warnings in CI.

#### Fix: `tracer-viewer/package.json`

In `devDependencies`, change the `@typescript-eslint/*` package versions:

| Package | Old | New |
|---|---|---|
| `@typescript-eslint/eslint-plugin` | `^6.13.0` (or similar v6) | `^8.0.0` |
| `@typescript-eslint/parser` | `^6.13.0` (or similar v6) | `^8.0.0` |

Then run:

```powershell
cd d:\Work\Tracer\tracer-viewer
pnpm install
```

Verify lint still passes:

```powershell
pnpm run lint
```

If `.eslintrc.cjs` references any v6-specific parser options or extends that are renamed in v8, update them as well. For `@typescript-eslint` v8:
- `@typescript-eslint/eslint-plugin` and `@typescript-eslint/parser` are still the correct package names
- The `recommended` config extend path is `plugin:@typescript-eslint/recommended` (unchanged)
- `parserOptions.project` is optional — leave it unset unless errors appear about missing tsconfig

After installing, `pnpm run lint` must exit with code 0.

---

### TRC-P3-007 — Session Browser View

**Design references:**
- `docs/tracer_phase3_design.md` §6.6 — `useLiveSse` Composable (complete code provided in design)
- `docs/tracer_phase3_design.md` §6.7 — Session Browser View (complete code provided in design)
- `docs/tracer_phase3_design.md` §6.10 — `NotableEventsList` Component (complete code provided in design)
- `docs/tracer_phase3_design.md` §6.11 — `LiveIndicator` Component (complete code provided in design)
- `docs/TASK-DETAIL.md` — TRC-P3-007 success conditions SC1–SC11

**Important:** The `SessionBrowserView.vue` design in §6.7 imports from `@/api/useApi`. This composable does **not exist yet** in the scaffold — the scaffold has `@/api/tracerApiClient.ts` with an exported `api` singleton instead. Use the existing `api` singleton pattern (or create a minimal `useApi.ts` that re-exports it) so the import resolves. Do not restructure `tracerApiClient.ts`.

#### Step 1 — Create `tracer-viewer/src/composables/useLiveSse.ts`

Implement the `useLiveNotables` composable exactly as specified in `docs/tracer_phase3_design.md` §6.6. Key behaviors:
- Takes `sessionId: string` as argument
- Uses `fetchEventSource` from `@microsoft/fetch-event-source`
- `onopen`: if `response.ok` → `liveStore.setConnected(true)`, else throw
- `onmessage`: parse `ev.data` as `NotableEventDto`, prepend to `events.value`, cap at 200
- `onclose`: `liveStore.setConnected(false)`
- `onerror`: `liveStore.setConnected(false)` + `liveStore.onReconnect()` (do not rethrow — let `fetchEventSource` handle backoff)
- `onMounted`: call `connect()`
- `onUnmounted`: call `abortCtrl?.abort()`
- Returns `{ events }` where `events` is a `Ref<NotableEventDto[]>`

#### Step 2 — Create `tracer-viewer/src/api/useApi.ts`

Simple re-export so that `import { useApi } from '@/api/useApi'` works:

```typescript
import { api } from './tracerApiClient';
export function useApi() { return api; }
```

#### Step 3 — Replace `tracer-viewer/src/views/SessionBrowserView.vue`

Replace the stub with the full implementation from `docs/tracer_phase3_design.md` §6.7. Key behaviors:
- `loading`, `error`, `sessions` reactive refs
- `load()`: sets `loading = true`, calls `api.listSessions()`, handles error
- `openSession(s)`: calls `router.push({ name: 'scenario', params: { sessionId: s.sessionId } })`
- `onMounted(load)`
- Template: shows `LoadingSpinner` while loading, `ErrorMessage` (with `@retry="load"`) on error, empty-state text when `sessions.length === 0`, grid of `SessionCard` components otherwise
- CSS class `session-browser` on root element, `session-browser__list` on the grid

#### Step 4 — Create `tracer-viewer/src/components/SessionCard.vue`

```vue
<script setup lang="ts">
import type { SessionDto } from '@/api/tracerApiClient';

const props = defineProps<{ session: SessionDto }>();
</script>

<template>
  <article class="session-card">
    <header class="session-card__header">
      <span class="session-card__scenario">{{ session.scenarioId }}</span>
      <span class="session-card__status" :class="`session-card__status--${session.status.toLowerCase()}`">
        {{ session.status }}
      </span>
    </header>
    <div class="session-card__meta">
      <span class="session-card__label" v-if="session.label">{{ session.label }}</span>
      <span class="session-card__time">{{ formatTime(session.startUtc) }}</span>
    </div>
    <footer class="session-card__footer">
      <span>{{ session.eventCount.toLocaleString() }} events</span>
      <span>{{ session.participatingNodes.length }} node(s)</span>
    </footer>
  </article>
</template>
```

Add a `formatTime` utility (import from `@/utils/time` — create the file if it does not exist):

```typescript
// src/utils/time.ts
export function formatTime(iso: string): string {
  return new Date(iso).toLocaleString();
}
export function formatDuration(iso: string): string {
  // Parse ISO 8601 duration, return HH:MM:SS string
  // For Phase 3, a minimal implementation is acceptable:
  return iso; // pass-through if no duration parsing is needed yet
}
```

Add SCSS styling with the `.session-card` root class.

#### Step 5 — Create `tracer-viewer/src/components/LiveIndicator.vue`

Implement exactly as in `docs/tracer_phase3_design.md` §6.11:
- Reads `liveStore.connection.connected` and `liveStore.connection.lastEventAt`
- Stale = connected AND `lastEventAt` more than 30 seconds ago
- Status: `'live'` | `'stale'` | `'disconnected'`
- Root element class: `live-indicator live-indicator--{status}`
- Inner `span.live-indicator__dot` and `span.live-indicator__label` with text "Live" / "Quiet" / "Disconnected"

#### Step 6 — Create `tracer-viewer/src/components/NotableEventCard.vue`

Simple display card for a single `NotableEventDto`:

```vue
<script setup lang="ts">
import type { NotableEventDto } from '@/api/tracerApiClient';
const props = defineProps<{ event: NotableEventDto }>();
</script>

<template>
  <article class="notable-event-card">
    <span class="notable-event-card__label">{{ event.notableLabel }}</span>
    <span class="notable-event-card__type">{{ event.eventType }}</span>
    <span class="notable-event-card__time">{{ formatTime(event.occurredAtUtc) }}</span>
  </article>
</template>
```

#### Step 7 — Create `tracer-viewer/src/components/NotableEventsList.vue`

Implement exactly as in `docs/tracer_phase3_design.md` §6.10. Key behaviors:
- Props: `sessionId: string`, `liveEvents: NotableEventDto[]`
- `initialEvents` ref loaded via `api.getScenarioNotables(sessionId, 100)`
- `watch(() => props.sessionId, loadInitial, { immediate: true })`
- `allEvents` computed: live events first, deduplicated by `eventId`, then initial events
- Template: loading placeholder when `loading && allEvents.length === 0`, "No notable events yet." text when empty and not loading, `TransitionGroup` list of `NotableEventCard` items otherwise

#### Step 8 — Build and lint check

```powershell
cd d:\Work\Tracer\tracer-viewer
pnpm run build
```

Fix any TypeScript errors before writing the spec files.

#### Step 9 — Create `tracer-viewer/tests/unit/useLiveSse.spec.ts`

**File:** `tracer-viewer/tests/unit/useLiveSse.spec.ts`

The test names must match the success conditions exactly (SC8):

```typescript
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { setActivePinia, createPinia } from 'pinia';
import { useLiveNotables } from '@/composables/useLiveSse';

// Mock fetchEventSource
vi.mock('@microsoft/fetch-event-source', () => ({
  fetchEventSource: vi.fn(),
}));
```

The composable calls `fetchEventSource` with callbacks (`onopen`, `onmessage`, `onclose`, `onerror`). To test the callbacks without actually running `onMounted`, the easiest approach is to capture the options object passed to `fetchEventSource` in the mock, then invoke the callbacks manually in each test.

Use `withSetup` helper pattern (from `@vue/test-utils` via `createApp + app.use(pinia)`):

```typescript
import { createApp } from 'vue';

function withSetup<T>(composable: () => T): [T, () => void] {
  let result!: T;
  const app = createApp({ setup() { result = composable(); return () => {} } });
  app.use(createPinia());
  const instance = app.mount(document.createElement('div'));
  return [result, () => app.unmount()];
}
```

Five required test methods (use `describe` block with these names):
1. `Connect_SetsLiveStoreConnected` — invoke `onopen` mock with `{ ok: true }` response; assert `liveStore.connection.connected === true`
2. `Message_PrependsEventToList` — invoke `onmessage` with a serialized `NotableEventDto`; assert `events.value[0].eventId` equals the event's ID
3. `Message_CapsListAt200Events` — invoke `onmessage` 201 times; assert `events.value.length === 200`
4. `Close_SetsDisconnected` — invoke `onclose`; assert `liveStore.connection.connected === false`
5. `Error_IncrementsReconnectAttempts` — invoke `onerror`; assert `liveStore.connection.reconnectAttempts > 0`

**Important:** `fetchEventSource` is async and never resolves in tests (it runs indefinitely in production). Mock it to return a never-settling Promise but immediately call the provided callbacks. Example:

```typescript
import { fetchEventSource } from '@microsoft/fetch-event-source';

let capturedHandlers: any = {};
(fetchEventSource as ReturnType<typeof vi.fn>).mockImplementation(
  (_url: string, opts: any) => {
    capturedHandlers = opts;
    return new Promise(() => {}); // never resolves
  }
);
```

Then trigger lifecycle in tests using `flushPromises` from `@vue/test-utils`.

#### Step 10 — Create `tracer-viewer/tests/unit/NotableEventsList.spec.ts`

**File:** `tracer-viewer/tests/unit/NotableEventsList.spec.ts`

Three required test methods (SC9):

1. `MergesInitialAndLiveEvents_LiveFirst` — mount `NotableEventsList` with `initialEvents = [A, B]` and `liveEvents = [C, A]`; assert `allEvents` equals `[C, A, B]`
2. `DeduplicatesEventsByEventId` — mount with `liveEvents = [X]` and `initialEvents = [X]`; assert `allEvents.length === 1`
3. `ShowsEmptyState_WhenNoEvents` — mount with empty `liveEvents` and `initialEvents`; assert the text "No notable events yet." appears in the rendered output

Use `@vue/test-utils` `mount` with a `global.plugins` Pinia context. Mock `api.getScenarioNotables` via `vi.mock`:

```typescript
vi.mock('@/api/tracerApiClient', () => ({
  api: {
    getScenarioNotables: vi.fn().mockResolvedValue([]),
  },
}));
```

For tests that need `initialEvents`, update the mock's return value per-test using `vi.mocked(api.getScenarioNotables).mockResolvedValue(...)`.

#### Step 11 — Create Playwright E2E stub

**File:** `tracer-viewer/tests/e2e/session-browser.spec.ts`

Create the directory `tracer-viewer/tests/e2e/` and add:

```typescript
import { test, expect } from '@playwright/test';

// This test requires a live Observer + FakeNode instance.
// It is a stub that will be run as part of TRC-P3-013 (Playwright E2E Smoke Tests).
// The test body is intentionally complete — it will be skipped in CI until TRC-P3-013.

test.describe('Session Browser', () => {
  test('loads_and_shows_session_card', async ({ page }) => {
    test.skip(process.env.E2E !== 'true', 'Requires live Observer; set E2E=true to run');
    await page.goto('http://localhost:5300/sessions');
    await expect(page.locator('.session-card').first()).toBeVisible({ timeout: 10_000 });
  });
});
```

This file satisfies SC10 without requiring the live stack to be up during the unit test run.

#### Step 12 — Frontend validation

Run all three checks and confirm all pass:

```powershell
cd d:\Work\Tracer\tracer-viewer
pnpm run build      # must exit 0
pnpm run test:unit  # must exit 0 (all specs including new ones)
pnpm run lint       # must exit 0
```

#### Step 13 — Backend regression check

```powershell
cd d:\Work\Tracer
dotnet test Tracer.sln --configuration Release
```

All 224 tests (183 unit + 41 integration) must still pass.

---

## 📝 Report Format

Write `.dev/tracer/reports/BATCH-10-REPORT.md` with:

1. **Summary** — one paragraph: what was done, final test counts
2. **Corrective Task 0 (DT-021)** — files changed, line numbers, how verified
3. **Corrective Task 1 (DT-022)** — package versions before/after, pnpm install output summary
4. **TRC-P3-007** — list every new/modified file; for each spec file, list the test method names and whether they pass
5. **Test Results:**
   - `dotnet test`: total, passed, failed
   - `pnpm run build`: exit code
   - `pnpm run test:unit`: test count, pass/fail
   - `pnpm run lint`: exit code, warning count
6. **Suggested commit message** (verbatim, ready to copy-paste)
7. **Open questions / blockers** (if any)

---

## ⚠️ Known Gotchas

1. **`fetchEventSource` is not auto-mocked** — you must `vi.mock('@microsoft/fetch-event-source', ...)` explicitly; otherwise `useLiveSse.spec.ts` will attempt a real HTTP call.
2. **`liveStore.connection.lastEventAt`** is a `Date | null` in the current `liveStore.ts` stub — confirm the type before writing `LiveIndicator`; add `lastEventAt: Date | null` to the store if it is missing.
3. **`liveStore.onEvent()`** must update `lastEventAt` to `new Date()` — if the current store stub does not do this, add that to the `onEvent` action.
4. **`NotableEventDto.eventId`** — confirm the field name in `tracerApiClient.ts` is `eventId` (camelCase, no longer `eventId: string` from PascalCase). After DT-021 is fixed, the SSE data uses camelCase. The TypeScript DTO interface must match.
5. **`api.getScenarioNotables` signature** — check the current `tracerApiClient.ts` for the exact method signature. If it takes `(sessionId, limit)` or `(sessionId)`, adjust calls accordingly. Do not change the client; adjust the call.
6. **`SessionDto.participatingNodes`** — check that `tracerApiClient.ts` has `participatingNodes: string[]` on `SessionDto`. If missing, add it (it is returned by the backend `TopologyDto`-level aggregation via session DTO mapping).
7. **`@vue/test-utils` not yet in `package.json`** — the scaffold may not have it. Add `@vue/test-utils` to `devDependencies` if needed: `pnpm add -D @vue/test-utils`.
8. **Pinia in test context** — always call `setActivePinia(createPinia())` in `beforeEach` for composable tests; use `global.plugins: [createPinia()]` for component mount tests.
9. **TypeScript paths** — `@/` is configured in both `tsconfig.app.json` and `vite.config.ts`. Confirm the alias works in Vitest (Vitest uses the Vite config's `resolve.alias`).
