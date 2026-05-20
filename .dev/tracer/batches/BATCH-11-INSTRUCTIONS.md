# BATCH-11: Scenario View

**Batch Number:** BATCH-11  
**Tasks:** TRC-P3-008  
**Phase:** Phase 3 — Vue SPA: Scenario View  
**Estimated Effort:** 10–14 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-10 (Session Browser View + useLiveNotables composable complete)

---

## 📋 Onboarding & Workflow

### Developer Instructions

This batch implements the Scenario View — the primary user-facing dashboard for a single session. It builds on the `useLiveNotables` composable and `NotableEventsList` component already delivered in TRC-P3-007, and introduces `ScenarioView.vue`, `ScenarioStatePanel.vue`, `ScenarioPhaseBanner.vue`, and `NotableEventsFeed.vue`, plus 3 Vitest spec files covering 15 test methods.

**Important distinction:** TRC-P3-008 introduces `NotableEventsFeed.vue`, which is a scenario-specific variant of `NotableEventsList.vue` (from TRC-P3-007). They are **not** the same component — `NotableEventsFeed` is used only by `ScenarioView` and will have its API-fetch + error behavior tested separately in TRC-P3-012.

### Required Reading (IN ORDER)

1. **Workflow:** `.github/skills/developer/SKILL.md`
2. **Task Definition:** `docs/TASK-DETAIL.md` — TRC-P3-008 (all 12 success conditions)
3. **Phase 3 Design:** `docs/tracer_phase3_design.md`:
   - §6.8 — Scenario View (complete source code)
   - §6.9 — ScenarioStatePanel Component (complete source code)
   - §6.10 — NotableEventsList Component (identical pattern to NotableEventsFeed)
4. **Existing code to read before writing:**
   - `tracer-viewer/src/composables/useLiveSse.ts` — `useLiveNotables` composable
   - `tracer-viewer/src/stores/sessionStore.ts` — `load`, `refreshState`, `current`, `state`, `loading`
   - `tracer-viewer/src/api/tracerApiClient.ts` — `ScenarioStateDto`, `ScenarioPhaseDto`, `SessionDto`
   - `tracer-viewer/src/components/NotableEventsList.vue` — dedup + merge pattern (replicate for `NotableEventsFeed`)
   - `tracer-viewer/src/views/ScenarioView.vue` — **stub only**; replace with full implementation

### Source Code Locations

**New files to create:**
- `tracer-viewer/src/components/ScenarioStatePanel.vue`
- `tracer-viewer/src/components/ScenarioPhaseBanner.vue`
- `tracer-viewer/src/components/NotableEventsFeed.vue`
- `tracer-viewer/tests/unit/ScenarioView.spec.ts`
- `tracer-viewer/tests/unit/ScenarioStatePanel.spec.ts`
- `tracer-viewer/tests/unit/ScenarioPhaseBanner.spec.ts`

**Files to replace:**
- `tracer-viewer/src/views/ScenarioView.vue` — replace stub with full implementation

### Run Tests

**Frontend:**
```powershell
cd d:\Work\Tracer\tracer-viewer
pnpm run build
pnpm run test:unit
pnpm run lint
```

**Backend (regression check — must still pass):**
```powershell
cd d:\Work\Tracer
dotnet test Tracer.sln --configuration Release
```

### Report Submission

`.dev/tracer/reports/BATCH-11-REPORT.md`

If you have questions: `.dev/tracer/questions/BATCH-11-QUESTIONS.md`

---

## ⚡ MANDATORY: No Stopping

Complete all steps. Fix TypeScript errors and test failures immediately. Write the report only after all checks pass (`build`, `test:unit`, `lint` exit 0; 224 backend tests pass).

---

## 🔄 MANDATORY WORKFLOW

Execute in strict sequence:

1. Read `ScenarioView.vue` stub and `sessionStore.ts` — understand current API
2. Read `ScenarioPhaseBanner.vue` requirements in `docs/TASK-DETAIL.md` SC6
3. Implement `ScenarioPhaseBanner.vue` ✅
4. Implement `ScenarioStatePanel.vue` ✅
5. Implement `NotableEventsFeed.vue` ✅
6. Replace `ScenarioView.vue` stub with full implementation ✅
7. `pnpm run build` — fix TypeScript errors ✅
8. Create `ScenarioPhaseBanner.spec.ts` (3 tests) ✅
9. Create `ScenarioStatePanel.spec.ts` (6 tests) ✅
10. Create `ScenarioView.spec.ts` (6 tests) ✅
11. `pnpm run test:unit` — fix test failures ✅
12. `pnpm run lint` — fix lint errors ✅
13. `dotnet test Tracer.sln --configuration Release` — 0 failures ✅
14. Write report ✅

---

## ✅ Tasks

---

### TRC-P3-008 — Scenario View

**Design references:**
- `docs/tracer_phase3_design.md` §6.8 — Scenario View (complete Vue code)
- `docs/tracer_phase3_design.md` §6.9 — ScenarioStatePanel (complete Vue code)
- `docs/tracer_phase3_design.md` §6.10 — NotableEventsList (identical merge pattern for NotableEventsFeed)
- `docs/TASK-DETAIL.md` — TRC-P3-008 SC1–SC12

#### Step 1 — Understand `sessionStore.ts`

Read `tracer-viewer/src/stores/sessionStore.ts` before writing `ScenarioView`. Confirm:
- `current: SessionDto | null`
- `state: ScenarioStateDto | null`
- `loading: boolean`
- `error: string | null`
- `load(sessionId: string): Promise<void>`
- `refreshState(sessionId?: string): Promise<void>` (or similar)

If `refreshState` does not exist yet in the store, add it: it calls `api.getScenarioState(sessionId)` and updates `this.state`. The current session's `sessionId` should be used if no argument is provided.

#### Step 2 — Implement `ScenarioPhaseBanner.vue`

**File:** `tracer-viewer/src/components/ScenarioPhaseBanner.vue`

Props: `session: SessionDto`

This component fetches phases from the API on mount and re-fetches when `session.sessionId` changes (using `watch`). It renders one row per `ScenarioPhaseDto` from `/api/scenario/phases?sessionId={id}`.

```vue
<script setup lang="ts">
import { ref, watch } from 'vue';
import { useApi } from '@/api/useApi';
import type { SessionDto, ScenarioPhaseDto } from '@/api/tracerApiClient';
import { formatTime } from '@/utils/time';

const props = defineProps<{ session: SessionDto }>();

const phases = ref<ScenarioPhaseDto[]>([]);

const loadPhases = async () => {
  const api = useApi();
  phases.value = await api.getScenarioPhases(props.session.sessionId);
};
watch(() => props.session.sessionId, loadPhases, { immediate: true });
</script>

<template>
  <section class="scenario-phase-banner">
    <div
      v-for="phase in phases"
      :key="phase.phaseName"
      class="scenario-phase-banner__row"
      :class="{ 'scenario-phase-banner__row--active': phase.status === 'Active' }"
    >
      <span class="scenario-phase-banner__name">{{ phase.phaseName }}</span>
      <span class="scenario-phase-banner__status">{{ phase.status }}</span>
      <span
        v-if="phase.endedAtUtc"
        class="scenario-phase-banner__end"
      >{{ formatTime(phase.endedAtUtc) }}</span>
    </div>
  </section>
</template>

<style lang="scss">
.scenario-phase-banner {
  background: var(--c-bg-surface);
  border-radius: 12px;
  padding: 1.5rem;
  display: flex;
  flex-direction: column;
  gap: 0.5rem;

  &__row {
    display: flex;
    align-items: center;
    gap: 1rem;
    padding: 0.5rem 0.75rem;
    border-radius: 6px;
    background: var(--c-bg-subtle);

    &--active {
      border-left: 3px solid var(--c-accent);
    }
  }

  &__name {
    flex: 1;
    font-weight: 500;
  }

  &__status {
    font-size: 0.8125rem;
    color: var(--c-text-muted);
  }

  &__end {
    font-size: 0.75rem;
    color: var(--c-text-muted);
    font-family: var(--font-mono, monospace);
  }
}
</style>
```

**Success conditions to verify:** SC6 — two phase rows rendered; Active phase has no `endedAtUtc` span; Completed phase shows formatted `endedAtUtc`.

#### Step 3 — Implement `ScenarioStatePanel.vue`

**File:** `tracer-viewer/src/components/ScenarioStatePanel.vue`

Implement exactly as in `docs/tracer_phase3_design.md` §6.9. Key requirements for the test:

- Props: `session: SessionDto`, `state: ScenarioStateDto | null`
- When `state.currentPhase` is set, the text is rendered in the "current phase" element
- When `state === null`, render "—" in both `currentPhase` and `elapsedDisplay` positions
- `session.status` value applies as `scenario-state-panel__value--{status.toLowerCase()}` on the status value element

The exact classes required by SC4 and SC5 are:
- Status value element must have: `class="scenario-state-panel__value scenario-state-panel__value--status"` plus `:class="\`scenario-state-panel__value--${statusLabel.toLowerCase()}\`"`
- So for `status = 'Active'`, the element will have class `scenario-state-panel__value--active`
- For `status = 'Completed'`, the element will have class `scenario-state-panel__value--completed`

Read the full source from `docs/tracer_phase3_design.md` §6.9 and implement it directly.

#### Step 4 — Implement `NotableEventsFeed.vue`

**File:** `tracer-viewer/src/components/NotableEventsFeed.vue`

Identical merge-dedup pattern to `NotableEventsList.vue` (§6.10 in the design doc), but:
- Component class name prefix is `notables-feed` instead of `notables-list`
- Same props: `sessionId: string`, `liveEvents: NotableEventDto[]`
- Same computed `allEvents`: live events first, dedup by `eventId`, then initial events
- Template states: loading placeholder when `loading && allEvents.length === 0`; "No notable events yet." when empty and not loading; `TransitionGroup` of `NotableEventCard` items

This is a distinct component from `NotableEventsList` — it will be placed differently in the layout (inside the `ScenarioView` grid) and its API-fetch + error behavior will be tested in TRC-P3-012.

#### Step 5 — Replace `ScenarioView.vue`

**File:** `tracer-viewer/src/views/ScenarioView.vue`

Replace the stub with the full implementation from `docs/tracer_phase3_design.md` §6.8. Key requirements:

1. **Props:** `defineProps<{ sessionId: string }>()`
2. **Session store:** `const sessionStore = useSessionStore()`
3. **On mount:** `onMounted(() => sessionStore.load(props.sessionId))`
4. **Prop change watcher:** `watch(() => props.sessionId, (sid) => sessionStore.load(sid))`
5. **Refresh timer:** 
   ```typescript
   let refreshTimer: number | null = null;
   onMounted(() => {
     refreshTimer = window.setInterval(() => sessionStore.refreshState(), 5000);
   });
   onUnmounted(() => {
     if (refreshTimer) window.clearInterval(refreshTimer);
   });
   ```
6. **Live events:** `const { events: liveEvents } = useLiveNotables(props.sessionId)`
7. **Template states:**
   - When `sessionStore.loading && !sessionStore.current`: show `LoadingSpinner` only
   - When `sessionStore.current` is set: show two-column grid with `ScenarioStatePanel`, `ScenarioPhaseBanner`, `NotableEventsFeed`, `LiveIndicator`
8. **Grid layout:** CSS `grid-template-areas: "state phases" / "state notables"` — state area spans two rows, phases and notables each one row

Important: `ScenarioView` imports `ScenarioPhaseBanner` (which fetches phases internally), `ScenarioStatePanel` (receiving `session` and `state` as props), `NotableEventsFeed` (receiving `sessionId` and `liveEvents`), and `LiveIndicator` (reads liveStore directly).

#### Step 6 — `pnpm run build`

Run the build. Fix any TypeScript compile errors before proceeding to tests.

#### Step 7 — Implement `ScenarioPhaseBanner.spec.ts`

**File:** `tracer-viewer/tests/unit/ScenarioPhaseBanner.spec.ts`

Three required test methods (SC11):

1. `RendersOneRowPerPhase` — mount with 2 `ScenarioPhaseDto` items in the mocked API; assert 2 `.scenario-phase-banner__row` elements are rendered
2. `ActivePhase_OmitsEndTime` — mount with one Active phase (`endedAtUtc` absent); assert no `.scenario-phase-banner__end` span is rendered
3. `CompletedPhase_ShowsFormattedEndTime` — mount with one Completed phase with `endedAtUtc = '2025-01-01T12:00:00Z'`; assert `.scenario-phase-banner__end` is rendered with non-empty text

Mock `api.getScenarioPhases` via `vi.mock('@/api/tracerApiClient', ...)`.

`SessionDto` stub needed for `session` prop:
```typescript
const stubSession: SessionDto = {
  sessionId: 'sess-1',
  scenarioId: 'CombatEngagement',
  startUtc: '2025-01-01T00:00:00Z',
  status: 'Active',
  participatingNodes: [],
  eventCount: 0,
};
```

#### Step 8 — Implement `ScenarioStatePanel.spec.ts`

**File:** `tracer-viewer/tests/unit/ScenarioStatePanel.spec.ts`

Six required test methods (SC10):

1. `ShowsCurrentPhase` — render with `state.currentPhase = 'engagement'`; assert text `engagement` is present in the phase display element (class `scenario-state-panel__value--phase`)
2. `ShowsElapsedTime` — render with `state.sessionElapsed = 'PT5M30S'`; assert elapsed text matches `'PT5M30S'` (pass-through since `formatDuration` is a stub)
3. `NullState_ShowsDashes` — render with `state = null`; assert `—` appears in the rendered output at least twice (once for current-phase, once for elapsed)
4. `StatusActive_AppliesActiveClass` — render with `session.status = 'Active'`; assert status value element has class `scenario-state-panel__value--active`
5. `StatusCompleted_AppliesCompletedClass` — render with `session.status = 'Completed'`; assert status value element has class `scenario-state-panel__value--completed`
6. `RendersAllParticipatingNodes` — render with `session.participatingNodes = ['alpha', 'beta', 'gamma']`; assert three `.scenario-state-panel__node` elements are rendered

#### Step 9 — Implement `ScenarioView.spec.ts`

**File:** `tracer-viewer/tests/unit/ScenarioView.spec.ts`

Six required test methods (SC9):

1. `Load_CalledWithSessionId_OnMount` — mount `ScenarioView` with `sessionId = 'abc'`; assert `sessionStore.load` was called once with `'abc'`
2. `Load_CalledAgain_OnSessionIdChange` — mount with `sessionId = 'abc'`; update prop to `'def'`; assert `sessionStore.load` was called again with `'def'`
3. `RefreshTimer_InvokesRefreshState_Every5s` — use Vitest fake timers; mount; advance by 5000 ms; assert `sessionStore.refreshState` was called at least once
4. `RefreshTimer_ClearedOnUnmount` — mount; unmount; advance by 5000 ms; assert `sessionStore.refreshState` is NOT called after unmount
5. `ShowsSpinner_WhileLoadingNoSession` — mount with `sessionStore.loading = true` and `sessionStore.current = null`; assert `LoadingSpinner` is rendered and the grid is not
6. `ShowsGrid_WhenSessionIsLoaded` — mount with `sessionStore.current = stubSession`; assert the scenario-view grid is rendered (e.g., `.scenario-view__grid` element exists)

**Setup for ScenarioView.spec.ts:**

`ScenarioView` uses `useLiveNotables` which calls `fetchEventSource`. You must mock it:

```typescript
vi.mock('@microsoft/fetch-event-source', () => ({
  fetchEventSource: vi.fn(() => new Promise(() => {})),
}));
```

`ScenarioView` also imports `ScenarioStatePanel`, `ScenarioPhaseBanner`, `NotableEventsFeed`, `LiveIndicator`. To isolate the view, use `global.stubs` to stub all child components:

```typescript
const wrapper = mount(ScenarioView, {
  global: {
    plugins: [pinia],
    stubs: {
      ScenarioStatePanel: true,
      ScenarioPhaseBanner: true,
      NotableEventsFeed: true,
      LiveIndicator: true,
      LoadingSpinner: true,
    },
  },
  props: { sessionId: 'abc' },
});
```

For timer tests (SC9.3 and SC9.4), use Vitest's fake timers:
```typescript
beforeEach(() => { vi.useFakeTimers(); });
afterEach(() => { vi.useRealTimers(); });
```

And in the test:
```typescript
await vi.advanceTimersByTimeAsync(5000);
expect(sessionStore.refreshState).toHaveBeenCalled();
```

For `sessionStore.load` and `sessionStore.refreshState` to be spied on, you need a custom `sessionStore` setup. Use `createPinia()` and then replace the store's actions with `vi.fn()`:

```typescript
const pinia = createPinia();
setActivePinia(pinia);
const sessionStore = useSessionStore();
vi.spyOn(sessionStore, 'load').mockResolvedValue();
vi.spyOn(sessionStore, 'refreshState').mockResolvedValue();
```

#### Step 10 — Final validation

```powershell
cd d:\Work\Tracer\tracer-viewer
pnpm run build      # exit 0
pnpm run test:unit  # exit 0, all tests pass (scaffold 3 + useLiveSse 5 + NotableEventsList 3 + ScenarioPhaseBanner 3 + ScenarioStatePanel 6 + ScenarioView 6 = 26 total)
pnpm run lint       # exit 0, 0 warnings

cd d:\Work\Tracer
dotnet test Tracer.sln --configuration Release   # 224 tests, 0 failures
```

---

## 📝 Report Format

Write `.dev/tracer/reports/BATCH-11-REPORT.md` with:

1. **Summary** — one paragraph: what was done, final test counts
2. **TRC-P3-008** — list every new/modified file; for each spec file, list the test method names and pass/fail
3. **Test Results:**
   - `dotnet test`: total, passed, failed
   - `pnpm run build`: exit code
   - `pnpm run test:unit`: test count, pass/fail breakdown
   - `pnpm run lint`: exit code, warning count
4. **Suggested commit message** (verbatim, ready to copy-paste)
5. **Open questions / blockers** (if any)
6. **Design decisions** — any deviations from the spec, and why

---

## ⚠️ Known Gotchas

1. **`sessionStore.refreshState` signature**: The current `sessionStore.ts` stub may have `refreshState()` taking no args. `ScenarioView` calls it as `sessionStore.refreshState()` with no arg. Ensure the method exists — add it if missing (it reads `this.current?.sessionId` internally, or accepts an optional `sessionId` arg). Do NOT change the interface expected by `ScenarioView.vue`.

2. **`ScenarioView` uses `useLiveNotables` internally**: When testing `ScenarioView`, mock `@microsoft/fetch-event-source` to prevent real HTTP calls. Without the mock, `onMounted` will throw.

3. **Child component stubs in ScenarioView.spec.ts**: Use `stubs: { ScenarioStatePanel: true, ... }` so the test doesn't fail because child components can't resolve their own dependencies (e.g., `ScenarioStatePanel` needs `sessionStore` via `ScenarioPhaseBanner` calling `useApi`).

4. **`ScenarioPhaseBanner` fetches on mount**: Its test must `await flushPromises()` after mount before asserting DOM.

5. **Timer test isolation**: Always call `vi.useRealTimers()` in `afterEach` — if fake timers leak into the next test, `flushPromises()` may hang.

6. **`window.setInterval` in ScenarioView**: Vitest's `vi.useFakeTimers()` stubs `window.setInterval` automatically. After `vi.advanceTimersByTimeAsync(5000)`, the interval callback should have fired.

7. **`LoadingSpinner` component stub**: When using `stubs: { LoadingSpinner: true }`, `wrapper.findComponent({ name: 'LoadingSpinner' })` works, or you can check `wrapper.html().includes('loadingspinner-stub')`. Choose a consistent assertion.

8. **CSS class assertions for `ScenarioStatePanel`**: The status value element has **two** classes: `scenario-state-panel__value--status` (static) and `scenario-state-panel__value--{status.toLowerCase()}` (dynamic). Your assertion should check for the dynamic class only, since the static class is always present.
