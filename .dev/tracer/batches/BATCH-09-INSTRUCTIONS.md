# BATCH-09: Backend Corrective Cleanup + Vue SPA Scaffold

**Batch Number:** BATCH-09  
**Tasks:** Corrective (DT-001, DT-002, DT-004, DT-005, DT-009, DT-016, DT-017, DT-018, DT-019, DT-020), TRC-P3-006  
**Phase:** Phase 3 — TracerObserver, Web API, Vue SPA  
**Estimated Effort:** 14–18 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-08 (all backend observer/API/SSE work complete)

---

## 📋 Onboarding & Workflow

### Developer Instructions

This batch has two distinct goals executed in strict sequence:

1. **Backend corrective cleanup** — Ten accumulated debt items targeting test quality (weak or wrong assertions), a production code bug, and a code-visibility issue. **All must pass before moving to the Vue work.**
2. **TRC-P3-006 Vue SPA Scaffold** — Create the `tracer-viewer/` frontend project from scratch. This is pure frontend work (TypeScript/Vue/Vite) and has no interaction with the C# backend beyond building successfully.

### Required Reading (IN ORDER)

1. **Workflow:** `.github/skills/developer/SKILL.md`
2. **Previous Review:** `.dev/tracer/reviews/BATCH-08-REVIEW.md` — understand exactly what each debt item requires
3. **Debt Tracker:** `.dev/tracer/DEBT-TRACKER.md` — DT-001, DT-002, DT-004, DT-005, DT-009, DT-016, DT-017, DT-018, DT-019, DT-020
4. **Task Definitions:** `docs/TASK-DETAIL.md` — TRC-P3-006 (all success conditions)
5. **Phase 3 Design:** `docs/tracer_phase3_design.md` — §6.1 (Project Setup), §6.2 (vite.config.ts), §6.3 (App Shell and Routing), §6.4 (Generated API Client), §6.5 (Stores), §6.12 (Color Tokens)

### Source Code Locations

**Backend files (corrective tasks):**
- `src/Tracer.Storage.DuckDB/Queries/EventQueryBuilder.cs` — DT-001
- `tests/Tracer.Tests.Unit/Storage/QueryBuilderTests.cs` — DT-001, DT-002
- `tests/Tracer.Tests.Unit/Mock/DeterminismTests.cs` — DT-004, DT-005
- `src/Tracer.Agent/Lifecycle/StartupRecoveryService.cs` — DT-009
- `src/Tracer.Agent/Lifecycle/IntervalRotator.cs` — DT-016
- `tests/Tracer.Tests.Integration/ObserverRotationIntegrationTests.cs` — DT-017
- `tests/Tracer.Tests.Integration/WebApiQueryRoundTripTests.cs` — DT-018, DT-019
- `tests/Tracer.Tests.Integration/LiveStreamingTests.cs` — DT-020

**Frontend (TRC-P3-006):**
- New directory: `tracer-viewer/` at the repository root (`d:\Work\Tracer\tracer-viewer\`)

### Run Backend Tests

```powershell
cd d:\Work\Tracer
dotnet test Tracer.sln --configuration Release
```

### Run Frontend Tests (after TRC-P3-006)

```powershell
cd d:\Work\Tracer\tracer-viewer
pnpm run build
pnpm run test:unit
pnpm run lint
```

### Report Submission

`.dev/tracer/reports/BATCH-09-REPORT.md`

If you have questions: `.dev/tracer/questions/BATCH-09-QUESTIONS.md`

---

## ⚡ MANDATORY: No Stopping

Complete every task in sequence. Fix compile errors and test failures before moving to the next task. **Do NOT write the report until both `dotnet test` and the Vue project commands all exit with code 0.** Do not ask for permission to run tests, fix root causes, or proceed to the next step. The report is written only after everything passes.

---

## 🔄 MANDATORY WORKFLOW

Execute in strict sequence:

1. **DT-001** — Parameterize LIMIT/OFFSET in EventQueryBuilder → tests pass ✅
2. **DT-002** — Fix SQL injection test to target PayloadSearch → tests pass ✅
3. **DT-004** — Add missing DeterminismTests assertions → tests pass ✅
4. **DT-005** — Fix MockDataSource_DifferentSeeds assertion → tests pass ✅
5. **DT-009** — Fix StartupRecoveryService SlowStateCount → tests pass ✅
6. **DT-016** — Restrict IntervalRotator.CurrentWriter setter to internal → tests pass ✅
7. **DT-017** — Strengthen SecondInterval_QueriesReturnCurrentIntervalEvents → tests pass ✅
8. **DT-018** — Add field assertions to GetEvent_ById_ReturnsCorrectEventDto → tests pass ✅
9. **DT-019** — Add eventsPublished assertion to GetTopology_AfterIngestion_ReturnsNodeInfo → tests pass ✅
10. **DT-020** — Extract and verify distinct eventIds in MultipleNodes_AllEventsAppearInUnifiedStream → tests pass ✅
11. **TRC-P3-006** — Vue SPA Scaffold → `pnpm run build`, `pnpm run test:unit`, `pnpm run lint` all pass ✅
12. **Write report** ✅

---

## ✅ Corrective Tasks

---

### Corrective Task 1 — Parameterize LIMIT/OFFSET in EventQueryBuilder (DT-001, P2)

**File:** `src/Tracer.Storage.DuckDB/Queries/EventQueryBuilder.cs`

`EventQueryBuilder.Build(EventQuery)` currently embeds `LIMIT` and `OFFSET` as inline integer literals in the SQL string (e.g. `... LIMIT 100 OFFSET 0`). Per the TRC-P1-006 specification and DuckDB's parameterized query support, these must be `$limit` and `$offset` named parameters like all other filter values.

**Change required:**

1. Replace the inline `LIMIT {query.Limit} OFFSET {query.Offset}` (or equivalent string interpolation) with `LIMIT $limit OFFSET $offset` in the SQL string.
2. Add parameter entries `("limit", query.Limit)` and `("offset", query.Offset)` to the returned parameter list.
3. `BuildCount` does NOT use LIMIT/OFFSET — verify it remains unchanged.

**Test to update:** `tests/Tracer.Tests.Unit/Storage/QueryBuilderTests.cs` — `Build_NoFilters_ContainsLimitAndOffset` must assert that both `$limit` and `$offset` appear in the SQL as named parameters (not inline integers) AND that the returned parameter dictionary contains entries keyed `"limit"` and `"offset"`.

After the fix, run `dotnet test Tracer.sln --configuration Release` and confirm all tests pass.

---

### Corrective Task 2 — Fix SQL Injection Test to Target PayloadSearch (DT-002, P2)

**File:** `tests/Tracer.Tests.Unit/Storage/QueryBuilderTests.cs`

The test `Build_SqlInjectionAttempt_IsParameterized` currently passes `OwningPlayerId = "'; DROP TABLE events; --"` and verifies parameterization for that field. This misses the actual dangerous code path: the `PayloadSearch` filter generates a `LIKE` clause with `%...%` wrapping, and `%` and `_` must be escaped to `\%` and `\_`. The test should verify the PayloadSearch path.

**Change required:**

Replace `Build_SqlInjectionAttempt_IsParameterized` so it:
1. Sets `PayloadSearch = "'; DROP TABLE events; --"` (injection attempt via PayloadSearch)
2. Asserts the SQL string does NOT contain the literal `'; DROP TABLE events; --`
3. Asserts the `$search` parameter value IS `%'; DROP TABLE events; --%` (wrapped in `%...%`)

If the existing `Build_PayloadSearch_EscapesLikeSpecialChars` test also needs adjustment to pass cleanly, fix it too. Keep both tests — they test different aspects.

Run all tests and confirm they pass.

---

### Corrective Task 3 — Add Missing DeterminismTests Assertions (DT-004, P2)

**File:** `tests/Tracer.Tests.Unit/Mock/DeterminismTests.cs`

The `DeterminismTests` class (specifically the same-seed determinism test) is missing `SequenceNumber` and `PayloadJson` comparisons between the two runs. The test verifies that the same seed produces the same output, but doesn't check these two fields.

**Change required:**

In the same-seed determinism test (likely named `MockDataSource_SameSeed_ProducesSameSequence` or similar), extend the record comparison to assert:
1. Both sequences have equal `SequenceNumber` at each position
2. Both sequences have equal `PayloadJson` at each position (in addition to whatever fields are already compared)

The comparison should be for ALL records, not just a subset sample.

Run all tests and confirm they pass.

---

### Corrective Task 4 — Fix MockDataSource_DifferentSeeds Assertion (DT-005, P2)

**File:** `tests/Tracer.Tests.Unit/Mock/DeterminismTests.cs`

`MockDataSource_DifferentSeeds_ProduceDifferentSequences` currently checks that "fewer than all" records match between the two sequences. This is weak — it could pass if all but one record happen to match by coincidence. The correct assertion is to compare the first record's `TraceId` value between the two sequences and assert they differ.

**Change required:**

In `MockDataSource_DifferentSeeds_ProduceDifferentSequences`:
1. Collect the first record from each sequence (the `EventRecord` at index 0)
2. Assert `firstRecordSeed1.TraceId != firstRecordSeed2.TraceId`

This is a tight assertion that directly proves the seeds produce different pseudorandom sequences from the very first record.

Run all tests and confirm they pass.

---

### Corrective Task 5 — Fix StartupRecoveryService SlowStateCount (DT-009, P2)

**File:** `src/Tracer.Agent/Lifecycle/StartupRecoveryService.cs`

`TryFinalizeAsync` opens the `slow_state.duckdb` file to finalize an interval's slow-state data, but the manifest is written with `SlowStateCount = 0` regardless of how many rows are actually present in the file.

**Change required:**

In `TryFinalizeAsync` (or whichever method reads `slow_state.duckdb`):
1. After opening the slow-state DuckDB file, execute `SELECT COUNT(*) FROM slow_state` (or equivalent)
2. Use the actual count when constructing the manifest entry instead of hardcoding `0`
3. If the file doesn't exist or the table doesn't exist, log a warning and use `0` (don't throw)

This is a production code change. Also add or update the relevant test in the unit or integration tests that validates this count is set correctly. Look for an existing test like `TryFinalizeAsync_*` in `tests/Tracer.Tests.Unit/Agent/` — if none exists, add one that creates a real DuckDB slow_state file with known row count, calls `TryFinalizeAsync`, reads the resulting manifest, and asserts `SlowStateCount` matches the actual count.

Run all tests and confirm they pass.

---

### Corrective Task 6 — Restrict IntervalRotator.CurrentWriter to Internal Setter (DT-016, P3)

**File:** `src/Tracer.Agent/Lifecycle/IntervalRotator.cs`

`CurrentWriter` has a public setter that was added for test injection. This leaks internal lifecycle state to all consumers. The setter should be `internal set` and the test project should access it via `InternalsVisibleTo`.

**Change required:**

1. Change `public IDiagnosticStorageWriter? CurrentWriter { get; set; }` to `public IDiagnosticStorageWriter? CurrentWriter { get; internal set; }`
2. Verify `[assembly: InternalsVisibleTo("Tracer.Tests.Unit")]` and `[assembly: InternalsVisibleTo("Tracer.Tests.Integration")]` are present in `Tracer.Agent`'s assembly attributes (add to `AssemblyInfo.cs` or a new file if not already present)
3. Verify all tests that use `CurrentWriter` as a setter are in `Tracer.Tests.Unit` or `Tracer.Tests.Integration` (they will continue to work via `InternalsVisibleTo`)

Run all tests and confirm they pass.

---

### Corrective Task 7 — Strengthen SecondInterval_QueriesReturnCurrentIntervalEvents (DT-017, P2)

**File:** `tests/Tracer.Tests.Integration/ObserverRotationIntegrationTests.cs`

The current `SecondInterval_QueriesReturnCurrentIntervalEvents` test asserts `ingestedTotal > 0` via `GET /api/live/status`. This is trivially true from interval 1's events. The test must prove that the read-only connection pool was refreshed to target the **new** interval's DuckDB file.

**Change required:**

Rewrite `SecondInterval_QueriesReturnCurrentIntervalEvents` as follows:
1. After rotation (interval 2 is active), push exactly 100 events with `Topic = "system.session_start"` and a **unique** `sessionId` specific to interval 2 (e.g. `"session-interval2-{guid}"`) — use your fixture's `PushAsync` to inject these events
2. Wait briefly (e.g., 100ms) for ingestion to complete
3. Call `GET /api/sessions` and deserialize the array of session DTOs
4. Assert that at least one session with `sessionId` matching your interval-2 session ID is present in the response

This proves the pool is targeting the interval-2 DuckDB file (which contains the `system.session_start` event with the unique session ID). Interval 1 had no such session, so its file cannot produce this result.

Run all tests and confirm they pass.

---

### Corrective Task 8 — Add Missing Field Assertions to GetEvent_ById (DT-018, P2)

**File:** `tests/Tracer.Tests.Integration/WebApiQueryRoundTripTests.cs`

`GetEvent_ById_ReturnsCorrectEventDto` only asserts `eventId` non-empty and `topic = "combat.hit"`. Per TRC-P3-010 SC7, the test must also verify `traceId`, `severity`, and `occurredAtUtc` match the pushed event.

**Change required:**

In `GetEvent_ById_ReturnsCorrectEventDto`:
1. When constructing the pushed `EventRecord`, assign known explicit values: `TraceId = new TraceId(42)`, `Severity = Severity.Warning`, and a known `PublishWallclock`
2. After calling `GET /api/events/{eventId}` and deserializing the `EventDto`, assert:
   - `dto.TraceId == "000000000000002A"` (16-char uppercase hex of 42)
   - `dto.Severity == "Warning"` (or however the DTO serializes it)
   - `Math.Abs((dto.OccurredAtUtc - knownPublishTime).TotalMilliseconds) < 1` (round-trip within 1ms)

Run all tests and confirm they pass.

---

### Corrective Task 9 — Add eventsPublished Assertion to GetTopology (DT-019, P2)

**File:** `tests/Tracer.Tests.Integration/WebApiQueryRoundTripTests.cs`

`GetTopology_AfterIngestion_ReturnsNodeInfo` asserts both node IDs appear in the topology but does not check `eventsPublished` or `firstSeenUtc` per TRC-P3-010 SC9.

**Change required:**

In `GetTopology_AfterIngestion_ReturnsNodeInfo`:
1. Push exactly `N` events from node A and exactly `M` events from node B (where N and M are distinct known constants, e.g. `N=3, M=5`)
2. After calling `GET /api/topology` and deserializing the `TopologyDto`, for each `NodeInfoDto` in `nodes`:
   - Assert `eventsPublished` matches the expected count for that node
   - Assert `firstSeenUtc` is a non-default `DateTimeOffset` (non-zero)

Run all tests and confirm they pass.

---

### Corrective Task 10 — Verify Distinct EventIds in MultipleNodes SSE Test (DT-020, P3)

**File:** `tests/Tracer.Tests.Integration/LiveStreamingTests.cs`

`MultipleNodes_AllEventsAppearInUnifiedStream` asserts `lines.Count == 20` but does not verify the events are the 20 distinct events that were pushed. The spec says "verified by `eventId`".

**Change required:**

In `MultipleNodes_AllEventsAppearInUnifiedStream`:
1. When constructing the 20 events (10 per node), assign explicit `EventId` values to each (e.g., using a loop with `new EventId((ulong)(i + 1))`)
2. After collecting all 20 `data:` lines, deserialize each JSON payload and extract the `eventId` field
3. Assert that the set of extracted `eventId` values has exactly 20 distinct elements matching the 20 assigned IDs

Run all tests and confirm they pass.

---

## ✅ Feature Task: TRC-P3-006 — Vue SPA Scaffold

**Design:** `docs/tracer_phase3_design.md` §6.1–§6.5, §6.12  
**Task Definition:** `docs/TASK-DETAIL.md` — TRC-P3-006 (all 11 success conditions)

### Context

This is the first frontend task. You will create the `tracer-viewer/` directory at the repository root and scaffold a complete Vue 3 / TypeScript / Vite project. No components from TRC-P3-007 or TRC-P3-008 are required yet — only the structural files needed for the scaffold and stores to compile, build, lint, and pass a smoke test.

### Deliverables

Read `docs/tracer_phase3_design.md` §6.1 through §6.5 and §6.12 carefully. These sections fully specify the required structure. Below is a precise task list — do not skip items.

**Step 1 — Initialize the project directory**

Create `d:\Work\Tracer\tracer-viewer\` and initialize it. Use `pnpm` as the package manager. The `package.json` must declare the following exact versions (or compatible minors as specified in §6.1):

Runtime deps: `vue@^3.4.0`, `vue-router@^4.3.0`, `pinia@^2.1.0`, `@microsoft/fetch-event-source@^2.0.1`  
Dev deps: `vite@^5.2.0`, `typescript@~5.4.0`, `@vitejs/plugin-vue@^5.0.4`, `vue-tsc@^2.0.0`, `vitest@^1.5.0`, `@vue/test-utils@^2.4.0`, `@playwright/test@^1.44.0`, `eslint@^8.57.0`, `eslint-plugin-vue@^9.24.0`, `@typescript-eslint/eslint-plugin@^6.13.0`, `@typescript-eslint/parser@^6.13.0`, `prettier@^3.2.5`, `sass@^1.75.0`, `jsdom@^24.0.3`

The `scripts` section in `package.json` must include:
```json
{
  "dev": "vite",
  "build": "vue-tsc -b && vite build",
  "preview": "vite preview",
  "test:unit": "vitest run",
  "test:e2e": "playwright test",
  "lint": "eslint . --ext .vue,.ts,.tsx --max-warnings 0"
}
```

**Step 2 — Configuration files**

Create the following exactly as specified in §6.2 and the project layout in §2:

- `tracer-viewer/vite.config.ts` — dev server on port 5173, `/api/*` proxy to `http://localhost:5300` with `changeOrigin: true`, build output to `../src/Tracer.Observer/wwwroot` (relative to `tracer-viewer/`), Vitest config inline or in `vitest.config.ts`
- `tracer-viewer/tsconfig.json`, `tsconfig.app.json`, `tsconfig.node.json` — standard Vue 3 + TypeScript 5 triple-config setup
- `tracer-viewer/.eslintrc.cjs` — extends `@typescript-eslint/recommended` + `plugin:vue/vue3-recommended`; rules: `@typescript-eslint/no-explicit-any: warn`
- `tracer-viewer/.prettierrc` — standard single-quote, semi-colon, 100-char line width config
- `tracer-viewer/playwright.config.ts` — `testDir: './tests/e2e'`, baseURL `http://localhost:5300`, no `webServer` block yet (it will be added in TRC-P3-013)
- `tracer-viewer/index.html` — standard Vite entry point loading `src/main.ts`

**Step 3 — SCSS design tokens**

Create exactly:
- `tracer-viewer/src/styles/tokens.scss` — defines all CSS custom properties from §6.12: `--c-bg`, `--c-bg-surface`, `--c-bg-subtle`, `--c-text`, `--c-text-muted`, `--c-accent`, `--c-success`, `--c-warning`, `--c-danger`, `--font-sans`, `--font-mono` with the exact values given in §6.12
- `tracer-viewer/src/styles/base.scss` — imports `tokens.scss`, sets `*, *::before, *::after { box-sizing: border-box; }`, applies `font-family: var(--font-sans)` to `body`

**Step 4 — TypeScript API client stub**

Create `tracer-viewer/src/api/tracerApiClient.ts` as a hand-authored stub. It must export:
- All DTO interfaces matching the backend DTOs: `SessionDto`, `ScenarioPhaseDto`, `NotableEventDto`, `EventDto`, `ScenarioStateDto`, `TopologyDto`, `NodeInfoDto`, `LiveStatusDto`
- A `TracerApiClient` class with the following async methods (stub implementations that call `fetch` against the correct paths):
  - `listSessions(from?: string, to?: string): Promise<SessionDto[]>` → `GET /api/sessions`
  - `getSession(sessionId: string): Promise<SessionDto | null>` → `GET /api/sessions/{sessionId}` (returns null on 404)
  - `getScenarioPhases(sessionId: string): Promise<ScenarioPhaseDto[]>` → `GET /api/scenario/phases?sessionId=...`
  - `getScenarioNotables(sessionId: string, limit?: number, before?: string): Promise<NotableEventDto[]>` → `GET /api/scenario/notables?sessionId=...`
  - `getScenarioState(sessionId: string): Promise<ScenarioStateDto>` → `GET /api/scenario/state?sessionId=...`
  - `getEvent(eventId: string): Promise<EventDto | null>` → `GET /api/events/{eventId}` (returns null on 404)
  - `getTopology(): Promise<TopologyDto>` → `GET /api/topology`
  - `getLiveStatus(): Promise<LiveStatusDto>` → `GET /api/live/status`
- An exported `api` singleton: `export const api = new TracerApiClient()`

All DTO field names must use camelCase to match ASP.NET Core's default JSON serialization (System.Text.Json produces camelCase by default).

**Step 5 — Router**

Create `tracer-viewer/src/router/index.ts` with exactly three routes as specified in §6.3 / TRC-P3-006 SC4:
- `"/"` → redirect to `"/sessions"`
- `"/sessions"` → lazy-loads `SessionBrowserView.vue` (stub file — see Step 7)
- `"/scenario/:sessionId"` → lazy-loads `ScenarioView.vue` (stub file), with `props: true`

**Step 6 — Pinia stores**

Create:

`tracer-viewer/src/stores/sessionStore.ts` — per §6.5 / TRC-P3-006 SC5:
- State: `current: SessionDto | null`, `state: ScenarioStateDto | null`, `loading: boolean`, `error: string | null`
- `load(sessionId: string)`: sets `loading = true`, calls `api.getSession(sessionId)` and `api.getScenarioState(sessionId)` concurrently via `Promise.all`, sets `current` and `state`, sets `loading = false`; on error sets `error` and `loading = false`
- `refreshState()`: updates `state` for the current session (calls `api.getScenarioState(current.sessionId)` if `current` is set)
- `clear()`: resets all fields to initial values

`tracer-viewer/src/stores/liveStore.ts` — per §6.5 / TRC-P3-006 SC6:
- State: `connection: { connected: boolean, lastEventAt: Date | null, reconnectAttempts: number }`
- `setConnected(value: boolean)`: sets `connected`; when `true` resets `reconnectAttempts` to 0
- `onEvent()`: updates `lastEventAt = new Date()`
- `onReconnect()`: increments `reconnectAttempts`

`tracer-viewer/src/stores/topologyStore.ts` — minimal:
- State: `topology: TopologyDto | null`, `loading: boolean`
- `load()`: calls `api.getTopology()`, sets `topology`

**Step 7 — App shell and stub view files**

Create `tracer-viewer/src/main.ts` — standard Vue app bootstrap: `createApp(App).use(router).use(createPinia()).mount('#app')`

Create `tracer-viewer/src/App.vue` — `<RouterView />` wrapped in a fade CSS transition, and `<AppHeader />` component; imports `./styles/base.scss`

Create `tracer-viewer/src/components/AppHeader.vue` — simple header bar with app title "Tracer"

Create `tracer-viewer/src/components/AppShell.vue` — layout wrapper with `<slot />`

Create `tracer-viewer/src/components/LoadingSpinner.vue` — animated spinner element with CSS

Create `tracer-viewer/src/components/ErrorMessage.vue` — per TRC-P3-006 SC8: accepts `message: string` prop, renders the message text, and has a retry button that emits `retry` event when clicked

Create **stub** views (content added in TRC-P3-007/008):
- `tracer-viewer/src/views/SessionBrowserView.vue` — template: `<div class="session-browser-view"><h1>Sessions</h1></div>`
- `tracer-viewer/src/views/ScenarioView.vue` — template: `<div class="scenario-view"><h1>Scenario</h1></div>`

**Step 8 — Smoke test**

Create `tracer-viewer/tests/unit/scaffold.spec.ts`:
```typescript
import { describe, it, expect } from 'vitest'
import { mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import ErrorMessage from '../../src/components/ErrorMessage.vue'

describe('Scaffold smoke test', () => {
  it('imports App without error', async () => {
    const { default: App } = await import('../../src/App.vue')
    expect(App).toBeDefined()
  })

  it('ErrorMessage renders message prop', () => {
    const wrapper = mount(ErrorMessage, {
      props: { message: 'Something went wrong' },
      global: { plugins: [createPinia()] }
    })
    expect(wrapper.text()).toContain('Something went wrong')
  })

  it('ErrorMessage emits retry on button click', async () => {
    const wrapper = mount(ErrorMessage, {
      props: { message: 'err' },
      global: { plugins: [createPinia()] }
    })
    await wrapper.find('button').trigger('click')
    expect(wrapper.emitted('retry')).toBeTruthy()
  })
})
```

**Step 9 — Verify build and tests**

After creating all files:

```powershell
cd d:\Work\Tracer\tracer-viewer
pnpm install
pnpm run build      # must exit 0; artifacts in src/Tracer.Observer/wwwroot
pnpm run test:unit  # must exit 0
pnpm run lint       # must exit 0 (0 warnings, 0 errors)
```

Fix any TypeScript, ESLint, or Vitest errors before writing the report. `pnpm run build` invokes `vue-tsc -b` first — TypeScript type errors will fail the build.

Also run the backend tests to confirm no regression:
```powershell
cd d:\Work\Tracer
dotnet test Tracer.sln --configuration Release
```

---

## 🧪 Testing Requirements

### Backend Corrective Tests

After all 10 corrective tasks:
- `dotnet test Tracer.sln --configuration Release` exits with **0 failures, 0 skipped**
- Test counts should not decrease from BATCH-08 baseline (182 unit + 41 integration)
- DT-009 requires at least one new or updated test validating `SlowStateCount` in the manifest

### Frontend Scaffold Tests

After TRC-P3-006:
- `pnpm run test:unit` exits with **0 failures**; the scaffold smoke tests pass
- `pnpm run build` exits with **0** (vue-tsc type check + Vite build succeeds)
- `pnpm run lint` exits with **0** (no warnings, no errors on any `.vue` or `.ts` file)

---

## ⚠️ Quality Standards

**❗ TEST QUALITY EXPECTATIONS**

- **NOT ACCEPTABLE for corrective tasks:** simply changing the assertion message. The assertion must test the actual behavior described in the debt item.
- **REQUIRED:** Every corrective test must be a real behavioral assertion — wrong values should cause failure, correct values should pass.

**❗ FRONTEND QUALITY EXPECTATIONS**

- All `.vue` and `.ts` files must be lint-clean. No `any` types unless marked `// eslint-disable-next-line`.
- The `TracerApiClient` stub must have real `fetch` calls (not empty stubs that return `undefined`) so the stores can be imported and called in tests.
- The router must use `createRouter(createWebHistory())` (not hash mode).
- Stores must use `defineStore` from Pinia (not the options API form).

---

## 📊 Developer Insights

**Q1:** What issues did you encounter during implementation? How did you resolve them?

**Q2:** Did you spot any weak points or design inconsistencies in the existing codebase while doing the corrective tasks?

**Q3:** What decisions did you make beyond the instructions when creating the Vue scaffold? What alternatives did you consider?

**Q4:** What edge cases or setup difficulties did you discover in the Vue/Vite toolchain configuration?

**Q5:** Are there any performance concerns, dependency conflicts, or tooling issues you noticed?

**Q6:** Suggested git commit message for this batch.

---

## 🎯 Success Criteria

- [ ] All 10 corrective debt items resolved (DT-001, DT-002, DT-004, DT-005, DT-009, DT-016, DT-017, DT-018, DT-019, DT-020)
- [ ] `dotnet test Tracer.sln --configuration Release` exits 0, ≥ 182 unit + 41 integration tests
- [ ] `tracer-viewer/` directory exists with all required files
- [ ] `pnpm run build` exits 0 (vue-tsc + Vite)
- [ ] `pnpm run test:unit` exits 0 (scaffold smoke tests pass)
- [ ] `pnpm run lint` exits 0 (zero warnings)
- [ ] Report submitted at `.dev/tracer/reports/BATCH-09-REPORT.md`

---

## 📚 Reference Materials

- **Task Definition:** `docs/TASK-DETAIL.md` — TRC-P3-006
- **Phase 3 Design (frontend sections):** `docs/tracer_phase3_design.md` — §6.1 through §6.5, §6.12
- **Debt Tracker:** `.dev/tracer/DEBT-TRACKER.md`
- **Previous Review:** `.dev/tracer/reviews/BATCH-08-REVIEW.md`
- **Developer Workflow:** `.github/skills/developer/SKILL.md`
