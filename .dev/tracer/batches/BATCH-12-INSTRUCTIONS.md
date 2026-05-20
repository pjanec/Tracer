# BATCH-12: Frontend Component Tests + Playwright E2E Smoke Tests

**Batch Number:** BATCH-12  
**Tasks:** TRC-P3-012, TRC-P3-013  
**Phase:** Phase 3 — Vue SPA: Final Tests  
**Estimated Effort:** 8–12 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-11 (Scenario View complete)

---

## 📋 Onboarding & Workflow

### Developer Instructions

This batch completes Phase 3 testing. TRC-P3-012 introduces the `useScenarioQuery` composable (with its 4-test spec), adds `SessionCard.spec.ts` (5 tests), and adds `NotableEventsFeed.spec.ts` (4 tests) — covering the API-fetch and error behavior not addressed by earlier merge-logic tests. TRC-P3-013 writes the full Playwright E2E smoke test suite (`scenario-view.spec.ts`) and configures `playwright.config.ts` with a `webServer` block.

Total new tests: 13 Vitest unit tests + 7 Playwright E2E tests.

### Required Reading (IN ORDER)

1. **Workflow:** `.github/skills/developer/SKILL.md`
2. **TRC-P3-012 Task Detail:** `docs/TASK-DETAIL.md` — TRC-P3-012 (all 7 success conditions)
3. **TRC-P3-013 Task Detail:** `docs/TASK-DETAIL.md` — TRC-P3-013 (all 9 success conditions)
4. **Existing composables to read before writing:**
   - `tracer-viewer/src/composables/useLiveSse.ts` — pattern for composable structure
   - `tracer-viewer/src/stores/sessionStore.ts` — pattern for `Promise.all` and reactive state
   - `tracer-viewer/src/api/tracerApiClient.ts` — `getScenarioNotables`, `getScenarioPhases`, `getScenarioState` signatures
5. **Existing test specs to understand the patterns:**
   - `tracer-viewer/tests/unit/useLiveSse.spec.ts` — composable test with `withSetup` helper
   - `tracer-viewer/tests/unit/ScenarioPhaseBanner.spec.ts` — API mock + `flushPromises` pattern
   - `tracer-viewer/tests/unit/NotableEventsList.spec.ts` — NotableEventsList merge tests (reference for NotableEventsFeed)
6. **Existing components to understand:**
   - `tracer-viewer/src/components/SessionCard.vue` — props and template classes
   - `tracer-viewer/src/components/NotableEventsFeed.vue` — exact class names for spec assertions

### Run Tests

**Frontend unit tests:**
```powershell
cd d:\Work\Tracer\tracer-viewer
pnpm run build
pnpm run test:unit
pnpm run lint
```

**Backend (regression — must still pass 224 tests):**
```powershell
cd d:\Work\Tracer
dotnet test Tracer.sln --configuration Release
```

**Note on Playwright (TRC-P3-013):** Playwright E2E tests require a live FakeNode+Observer server. All Playwright tests should use `test.skip(process.env['E2E'] !== 'true', ...)` so they are skipped in CI without the server. The `playwright.config.ts` `webServer` block is for future use when `E2E=true`.

### Report Submission

`.dev/tracer/reports/BATCH-12-REPORT.md`

Questions: `.dev/tracer/questions/BATCH-12-QUESTIONS.md`

---

## ⚡ MANDATORY: No Stopping

Complete all steps. Fix all lint/TypeScript/test failures. Write the report only after all checks pass.

---

## 🔄 MANDATORY WORKFLOW

Execute in strict sequence:

1. Read `SessionCard.vue` carefully — note exact CSS class names (required for spec assertions) ✅
2. Read `NotableEventsFeed.vue` carefully — note exact class names ✅
3. Implement `useScenarioQuery.ts` ✅
4. Create `useScenarioQuery.spec.ts` (4 tests) ✅
5. Create `SessionCard.spec.ts` (5 tests) ✅
6. Create `NotableEventsFeed.spec.ts` (4 tests) ✅
7. `pnpm run build` — fix TypeScript errors ✅
8. `pnpm run test:unit` — fix test failures ✅
9. `pnpm run lint` — fix lint errors ✅
10. Update `playwright.config.ts` — add `webServer` block ✅
11. Create `tracer-viewer/tests/e2e/scenario-view.spec.ts` (7 tests, all gated by E2E=true) ✅
12. `pnpm run build` ✅ `pnpm run test:unit` ✅ `pnpm run lint` ✅
13. `dotnet test Tracer.sln --configuration Release` — 0 failures ✅
14. Write report ✅

---

## ✅ Tasks

---

### TRC-P3-012 — Frontend Component Tests (Vitest)

**Design reference:** `docs/tracer_phase3_design.md` §8.3 — Frontend Unit Tests (Vitest)

#### Step 1 — Read `SessionCard.vue`

Before writing `SessionCard.spec.ts`, read `tracer-viewer/src/components/SessionCard.vue` to find the exact CSS classes used for:
- The root element
- The scenario ID display
- The status badge (and its variant class for Active/Completed)
- The event count display
- The node count display

The spec assertions must use these exact classes.

#### Step 2 — Read `NotableEventsFeed.vue`

Before writing `NotableEventsFeed.spec.ts`, read `tracer-viewer/src/components/NotableEventsFeed.vue` to find:
- The loading placeholder class/text
- The empty state class/text (`"No notable events yet."`)
- The items container class

#### Step 3 — Implement `useScenarioQuery.ts`

**File:** `tracer-viewer/src/composables/useScenarioQuery.ts`

```typescript
import { ref, watch } from 'vue';
import type { Ref } from 'vue';
import { useApi } from '@/api/useApi';
import type { NotableEventDto, ScenarioPhaseDto, ScenarioStateDto } from '@/api/tracerApiClient';

export function useScenarioQuery(sessionId: Ref<string>) {
  const notables = ref<NotableEventDto[]>([]);
  const phases = ref<ScenarioPhaseDto[]>([]);
  const state = ref<ScenarioStateDto | null>(null);
  const loading = ref(false);
  const error = ref<string | null>(null);

  const load = async () => {
    loading.value = true;
    error.value = null;
    try {
      const api = useApi();
      const [n, p, s] = await Promise.all([
        api.getScenarioNotables(sessionId.value, 100),
        api.getScenarioPhases(sessionId.value),
        api.getScenarioState(sessionId.value),
      ]);
      notables.value = n;
      phases.value = p;
      state.value = s;
    } catch (err: unknown) {
      error.value = err instanceof Error ? err.message : 'Failed to load scenario';
    } finally {
      loading.value = false;
    }
  };

  watch(sessionId, load, { immediate: true });

  return { notables, phases, state, loading, error, load };
}
```

Key requirements:
- SC1: `loading = true` before `Promise.all`, `false` in `finally`
- SC1: `error` set on catch, cleared on each call
- SC2 (`ReactiveSessionId_ReloadsOnChange`): `watch(sessionId, load, { immediate: true })`

#### Step 4 — Create `useScenarioQuery.spec.ts`

**File:** `tracer-viewer/tests/unit/useScenarioQuery.spec.ts`

Four required test methods (SC2):

1. **`Load_SetsLoadingTrueThenFalse`** — verify loading is `true` during the API call and `false` after. Use a delayed mock:
   ```typescript
   let resolveFn: () => void;
   const promise = new Promise<NotableEventDto[]>(res => { resolveFn = () => res([]); });
   vi.mocked(api.getScenarioNotables).mockReturnValueOnce(promise);
   // don't await yet — check loading is true
   // then resolve and check loading is false
   ```
   
   Since `Promise.all` only resolves after all three, you may need to delay all three simultaneously. A simpler approach: start the load, check `loading.value` before awaiting, then flush.

2. **`Load_PopulatesNotablesPhasesAndState`** — mock all three API methods; call `load()`; assert `notables.value`, `phases.value`, and `state.value` are populated.

3. **`Load_OnApiError_SetsErrorRefAndClearsLoading`** — mock `getScenarioNotables` to reject; call `load()`; assert `error.value` is non-null and `loading.value === false`.

4. **`ReactiveSessionId_ReloadsOnChange`** — use `withSetup`-style mounting with a reactive `ref` for `sessionId`; change `sessionId.value` to a new value; assert `load` was called again (verify by checking `getScenarioNotables` call count).

**Setup:** Use the same `withSetup` helper pattern from `useLiveSse.spec.ts`. Mock `@/api/tracerApiClient`:

```typescript
vi.mock('@/api/tracerApiClient', () => ({
  api: {
    getScenarioNotables: vi.fn().mockResolvedValue([]),
    getScenarioPhases: vi.fn().mockResolvedValue([]),
    getScenarioState: vi.fn().mockResolvedValue({
      currentPhase: 'opening',
      sessionElapsed: 'PT0S',
      totalEvents: 0,
      totalNotables: 0,
    }),
  },
}));
```

#### Step 5 — Create `SessionCard.spec.ts`

**File:** `tracer-viewer/tests/unit/SessionCard.spec.ts`

Five required test methods (SC3):

1. `RendersScenarioId` — mount with a `SessionDto`; assert `session.scenarioId` text is present
2. `RendersFormattedStartUtc` — mount with `startUtc = '2025-01-01T12:00:00Z'`; assert some time text is rendered (from `formatTime`)
3. `RendersStatusBadge` — mount; assert the status badge element is rendered and its text matches `session.status`
4. `RendersEventCount` — mount with `eventCount = 42`; assert `42` appears in the rendered output
5. `RendersNodeCount` — mount with `participatingNodes: ['alpha', 'beta']`; assert node count text (or node elements) reflect 2

**Important:** Read `SessionCard.vue` to find the actual CSS classes and data-binding approach before writing assertions.

#### Step 6 — Create `NotableEventsFeed.spec.ts`

**File:** `tracer-viewer/tests/unit/NotableEventsFeed.spec.ts`

Four required test methods (SC4):

1. **`OnMount_CallsGetScenarioNotables_ViaApi`** — mount; assert `api.getScenarioNotables` was called once with the correct `sessionId`
2. **`ApiError_LoadingSetFalse_ListRemainsEmpty`** — mock `getScenarioNotables` to reject; mount; `flushPromises()`; assert `allEvents` is empty (via finding `.notables-feed__empty` or `.notables-feed__loading` is gone) and the loading placeholder is not shown
3. **`InitialLoad_PopulatesInitialEvents`** — mock with 2 events; mount; `flushPromises()`; assert 2 `.notable-event-card` elements
4. **`LiveAndInitial_MergedInCorrectOrder`** — mock initial response `[eventB]`; mount with `liveEvents = [eventC, eventB]` (B is duplicate); assert `allEvents = [eventC, eventB]` — live first, dedup; 2 cards total

**Mock setup:**
```typescript
vi.mock('@/api/tracerApiClient', () => ({
  api: {
    getScenarioNotables: vi.fn().mockResolvedValue([]),
  },
}));
```

For `ApiError_LoadingSetFalse_ListRemainsEmpty`, mock as:
```typescript
vi.mocked(api.getScenarioNotables).mockRejectedValueOnce(new Error('Network error'));
```

After `flushPromises()`, verify the empty state text "No notable events yet." is shown (because `allEvents` is empty and `loading` is false).

---

### TRC-P3-013 — Playwright E2E Smoke Tests

**Design reference:** `docs/tracer_phase3_design.md` §8.4 — E2E Tests (Playwright)

#### Step 7 — Update `playwright.config.ts`

Add a `webServer` block that polls `/api/health`. The block should be conditional so CI without a server doesn't hang:

```typescript
import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: './tests/e2e',
  use: {
    baseURL: 'http://localhost:5300',
  },
  webServer: process.env['E2E'] === 'true' ? {
    command: 'echo "Server expected to be already running on :5300"',
    url: 'http://localhost:5300/api/health',
    reuseExistingServer: true,
    timeout: 30_000,
  } : undefined,
});
```

This satisfies SC8: `playwright.config.ts` declares a `webServer` block that polls `http://localhost:5300/api/health`.

#### Step 8 — Create `scenario-view.spec.ts`

**File:** `tracer-viewer/tests/e2e/scenario-view.spec.ts`

All 7 tests must be gated by `test.skip(process.env['E2E'] !== 'true', 'E2E tests require a live server')`.

```typescript
import { test, expect } from '@playwright/test';

test.describe('Scenario View E2E', () => {
  test.skip(process.env['E2E'] !== 'true', 'E2E tests require a live server (set E2E=true)');

  test('NavigatesToSessionBrowser_OnRootLoad', async ({ page }) => {
    await page.goto('http://localhost:5300/');
    await page.waitForURL(/\/sessions/, { timeout: 3000 });
  });

  test('SessionCard_Visible_Within10s', async ({ page }) => {
    await page.goto('http://localhost:5300/sessions');
    await expect(page.locator('.session-card').first()).toBeVisible({ timeout: 10_000 });
  });

  test('ClickSessionCard_OpensScenarioView', async ({ page }) => {
    await page.goto('http://localhost:5300/sessions');
    await page.locator('.session-card').first().click();
    await page.waitForURL(/\/scenario\//, { timeout: 3000 });
    await expect(page.locator('.scenario-state-panel')).toBeVisible({ timeout: 3000 });
  });

  test('LiveIndicator_TurnsGreen_Within5s', async ({ page }) => {
    await page.goto('http://localhost:5300/sessions');
    await page.locator('.session-card').first().click();
    await page.waitForURL(/\/scenario\//, { timeout: 3000 });
    await expect(page.locator('.live-indicator--live')).toBeVisible({ timeout: 5000 });
  });

  test('NotableEvents_AppearWithin500ms_OfLiveIndicator', async ({ page }) => {
    await page.goto('http://localhost:5300/sessions');
    await page.locator('.session-card').first().click();
    await page.waitForURL(/\/scenario\//, { timeout: 3000 });
    await page.locator('.live-indicator--live').waitFor({ timeout: 5000 });
    await expect(page.locator('.notable-event-card').first()).toBeVisible({ timeout: 500 });
  });

  test('PageLoad_Cold_Under2s', async ({ page }) => {
    await page.context().clearCookies();
    await page.goto('http://localhost:5300/sessions');
    const timing = await page.evaluate(() => {
      const t = performance.timing;
      return t.domContentLoadedEventEnd - t.navigationStart;
    });
    expect(timing).toBeLessThan(2000);
  });
});
```

Note: The `test.skip` at the describe level skips all tests in the block when `E2E !== 'true'`. This is the correct pattern since `test.skip()` at describe level acts as `test.describe.skip()` when called with a boolean.

Actually, in Playwright, `test.skip(condition, reason)` inside `test.describe` skips only if called before the `test()` blocks using `test.beforeEach(() => test.skip(condition))` or by calling `test.skip()` directly in a `test.describe` with `condition`. The correct approach is to gate each test individually:

```typescript
test('NavigatesToSessionBrowser_OnRootLoad', async ({ page }) => {
  test.skip(process.env['E2E'] !== 'true', 'E2E tests require a live server');
  // ...
});
```

Or use `test.describe.skip()`:
```typescript
test.describe.skip(process.env['E2E'] !== 'true' ? 'Skipped in non-E2E' : 'Scenario View E2E', () => {
  // ...
});
```

For simplicity, use the `test.skip()` at the top of each individual test (same pattern as `session-browser.spec.ts`):

```typescript
const skip = process.env['E2E'] !== 'true';

test('NavigatesToSessionBrowser_OnRootLoad', async ({ page }) => {
  test.skip(skip, 'E2E tests require a live server (set E2E=true)');
  ...
});
```

This is the cleanest approach matching the existing `session-browser.spec.ts` pattern.

---

## 📝 Report Format

Write `.dev/tracer/reports/BATCH-12-REPORT.md` with:

1. **Summary** — one paragraph: what was done, final test counts
2. **TRC-P3-012** — list every new/modified file; for each spec file, list the test method names and pass/fail
3. **TRC-P3-013** — describe `playwright.config.ts` changes and `scenario-view.spec.ts` contents (with note that all tests are E2E-gated)
4. **Test Results:**
   - `dotnet test`: total, passed, failed
   - `pnpm run build`: exit code, module count
   - `pnpm run test:unit`: test file count, test count, pass/fail
   - `pnpm run lint`: exit code, warning count
5. **Suggested commit message** (verbatim)
6. **Open questions / blockers** (if any)

---

## ⚠️ Known Gotchas

1. **`useScenarioQuery` takes a `Ref<string>` not a plain `string`**: The watch on `sessionId` requires a reactive ref to detect changes. Tests must pass `ref('session-1')` not `'session-1'`.

2. **`Load_SetsLoadingTrueThenFalse` is tricky**: Since `loading` is set to `true` at the start of the async `load()` function and back to `false` in `finally`, you cannot observe `loading = true` after `await flushPromises()`. To test this, start the load without awaiting, then check `loading.value`:
   ```typescript
   const loadPromise = result.load();
   // At this point, before await, loading should be true
   expect(result.loading.value).toBe(true);
   await flushPromises();
   await loadPromise;
   expect(result.loading.value).toBe(false);
   ```

3. **`NotableEventsFeed` API error handling**: The component doesn't expose `loading` as a prop — check the DOM state. After an API error, `loading.value = false` (in `finally`) and `initialEvents.value = []` (not set). The component renders "No notable events yet." (empty state). Check for that text.

4. **`SessionCard.vue` may not have a status badge class**: Read the component carefully. If the status text is rendered in an element like `<span class="session-card__status session-card__status--active">Active</span>`, the spec can use `.session-card__status`.

5. **`test.skip` in Playwright**: Call `test.skip(condition, reason)` inside each `test()` callback body, not at the top level of the describe. This is the pattern in the existing `session-browser.spec.ts`.

6. **`playwright.config.ts` `webServer` with conditional**: The `webServer: undefined` branch (when E2E is not set) tells Playwright not to start any server. All Playwright tests are skipped anyway via `test.skip`, so this is safe.

7. **Frontend test count**: After BATCH-12, `pnpm run test:unit` should show: scaffold 3 + useLiveSse 5 + NotableEventsList 3 + ScenarioPhaseBanner 3 + ScenarioStatePanel 6 + ScenarioView 6 + useScenarioQuery 4 + SessionCard 5 + NotableEventsFeed 4 = **39 total**.
