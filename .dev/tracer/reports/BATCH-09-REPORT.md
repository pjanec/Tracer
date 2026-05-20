# BATCH-09 Report

**Batch:** BATCH-09  
**Developer:** GitHub Copilot  
**Date:** 2025-07-15  
**Status:** Complete

---

## 📊 Task Completion

| Task ID | Status | Notes |
|---------|--------|-------|
| DT-001 | ✅ Complete | LIMIT/OFFSET now use `$limit`/`$offset` named parameters in EventQueryBuilder |
| DT-002 | ✅ Complete | `Build_SqlInjectionAttempt_IsParameterized` now targets PayloadSearch field |
| DT-004 | ✅ Complete | DeterminismTests extended with per-index `SequenceNumber` and `PayloadJson` assertions |
| DT-005 | ✅ Complete | Different-seed test now asserts `TraceId` of first record differs using `.NotBe()` |
| DT-009 | ✅ Complete | `CountSlowStateAsync` added; manifest `SlowStateCount` populated from real row count; new unit test added |
| DT-016 | ✅ Complete | `CurrentWriter` setter changed to `internal set`; `InternalsVisibleTo("Tracer.Tests.Integration")` added |
| DT-017 | ✅ Complete | Test pushes 100 `system.session_start` events with unique sessionId; asserts sessionId appears in `/api/sessions` |
| DT-018 | ✅ Complete | Test asserts `traceId == "000000000000002A"`, `severity == "Warning"`, `occurredAtUtc` within 1ms |
| DT-019 | ✅ Complete | Test pushes 3 from alpha / 5 from beta; asserts `eventsPublished` counts per node and non-default `firstSeenUtc` |
| DT-020 | ✅ Complete | Test assigns explicit EventId values 1–20; asserts 20 distinct IDs extracted from SSE JSON (PascalCase) |
| TRC-P3-006 | ✅ Complete | Full Vue 3 + TypeScript 5 + Vite 5 scaffold; build/test/lint all exit 0 |

---

## 🧪 Testing Results

### Backend

**Unit Tests Passed:** 183 / 183  
**Integration Tests Passed:** 41 / 41  
**Total:** 224 / 224, 0 failures

```
Test summary: error 0, failed 0, passed 183, skipped 0, total 183 duration 8s
Test summary: error 0, failed 0, passed 41, skipped 0, total 41 duration 127s
```

### Frontend (TRC-P3-006)

**`pnpm run build`:** Exit 0 — `vue-tsc -b` type-checked 37 modules; Vite produced 6 output files in `../src/Tracer.Observer/wwwroot`  
**`pnpm run test:unit`:** Exit 0 — 3/3 tests in `scaffold.spec.ts` passed (Vitest 1.6.1, jsdom environment)  
**`pnpm run lint`:** Exit 0 — 0 errors, 0 warnings (ESLint 8.57)

**Key Test Scenarios Verified:**
- [x] `Build_NoFilters_ContainsLimitAndOffset` — asserts `$limit` / `$offset` in SQL and `"limit"` / `"offset"` parameter keys
- [x] `Build_SqlInjectionAttempt_IsParameterized` — asserts literal injection string absent from SQL; `$search` parameter contains `%...%`
- [x] `MockDataSource_SameSeed_ProducesSameSequence` — `SequenceNumber` and `PayloadJson` equal at every index
- [x] `MockDataSource_DifferentSeeds_ProduceDifferentSequences` — first-record `TraceId` from seed A ≠ seed B
- [x] `StartupRecovery_OrphanWithSlowStateRows_SlowStateCountInManifest` — real DuckDB with 3 slow_state rows; `manifest.SlowStateCount == 3`
- [x] `SecondInterval_QueriesReturnCurrentIntervalEvents` — 100 events with unique sessionId visible via `/api/sessions` after rotation
- [x] `GetEvent_ById_ReturnsCorrectEventDto` — traceId hex, severity string, occurredAtUtc round-trip within 1ms
- [x] `GetTopology_AfterIngestion_ReturnsNodeInfo` — eventsPublished count per node, non-default firstSeenUtc
- [x] `MultipleNodes_AllEventsAppearInUnifiedStream` — 20 distinct EventIds 1–20 extracted from SSE stream
- [x] Scaffold smoke: App imports without error, ErrorMessage renders prop, ErrorMessage emits retry event

---

## 📝 Developer Insights

**Q1: What issues did you encounter during implementation? How did you resolve them?**

**Two cascade failures from DT-001.** Adding `$limit` and `$offset` parameters to `EventQueryBuilder` changed the parameter count from 2 to 4, which broke the existing `Build_MinSeverityWarning_ExpandsToInClause` test that asserted `HaveCount(2)`. Fixed by updating the expected count to `HaveCount(4, "sev0, sev1, limit, offset")`. This is a well-known consequence of parameterizing a previously inline value — the debt created more debt.

**SSE PascalCase in DT-020.** The SSE endpoint (`SseEndpoints.cs`) calls `JsonSerializer.Serialize(dto)` without options, producing PascalCase property names (`EventId`, `TraceId`, `OccurredAtUtc`), whereas REST API endpoints use ASP.NET Core's default camelCase. The initial implementation extracted `"eventId"` from the SSE JSON, causing a `KeyNotFoundException`. Fixed by switching to `doc.RootElement.GetProperty("EventId")`. This is a subtle cross-cutting inconsistency that required tracing the actual serialization path in the production code.

**pnpm not on PATH.** The system did not have pnpm installed despite the project spec requiring it. Resolved by installing via `npm install -g pnpm` first, then running `pnpm approve-builds` to approve build scripts for `esbuild`, `vue-demi`, and `@parcel/watcher` (pnpm 8+ requires explicit approval of postinstall scripts).

**ESLint `vue/max-attributes-per-line` and `vue/singleline-html-element-content-newline`.** The `plugin:vue/vue3-recommended` ruleset enforces max 1 attribute per line on multiline elements and requires element content on its own line when attributes are present. This triggered 10 warnings across `App.vue`, `ErrorMessage.vue`, and `LoadingSpinner.vue` — all templates that had inline attributes and inline text nodes. Fixed by reformatting all affected elements to use one attribute per line and content on a dedicated line.

---

**Q2: Did you spot any weak points or design inconsistencies in the existing codebase while doing the corrective tasks?**

**SSE vs REST serialization inconsistency.** The entire REST layer uses ASP.NET Core's default `System.Text.Json` options (camelCase), but the SSE event endpoint bypasses `Results.Ok()` and calls `JsonSerializer.Serialize(dto)` directly — producing PascalCase. This means any frontend code consuming both REST and SSE must handle two different JSON conventions. A cleaner design would pass `JsonSerializerOptions` with camelCase policy into the SSE serializer, or use a centralized `IJsonSerializer` service. The DT-020 fix works correctly but the inconsistency is a latent trap for future consumers.

**`SlowStateCount = 0` in StartupRecovery was a silent data loss.** The manifest was published with incorrect state data. Any downstream consumer that relied on `SlowStateCount` would receive `0` regardless of actual content. This was discovered by reading the method body carefully — there was no test covering this field at all before DT-009.

**IntervalRotator `public set` leakage.** A `public` setter on `CurrentWriter` exposed internal lifecycle state to any code that could reference `IntervalRotator`. While the DT-016 fix is minimal (`internal set`), the broader issue is that test injection through property assignment is a weaker pattern than constructor injection or a dedicated `ITestable` interface.

---

**Q3: What decisions did you make beyond the instructions when creating the Vue scaffold? What alternatives did you consider?**

**Added `@types/node` to devDependencies.** The instructions list exact package versions but do not mention `@types/node`. However, `vite.config.ts` uses `path.resolve(__dirname, './src')` and `import path from 'node:path'`, which require Node.js type definitions. Without `@types/node`, `vue-tsc -b` would fail. Considered using `new URL('./src', import.meta.url).pathname` (no Node types needed) but chose the explicit `path.resolve` + `@types/node` approach as it's the standard Vite scaffold pattern and maps cleanly to `tsconfig.node.json`'s `"types": ["node"]`.

**`tsconfig.app.json` includes `tests/unit/**/*`.** This ensures `vue-tsc -b` type-checks the unit tests under the app configuration. The alternative — a separate `tsconfig.test.json` — adds complexity without benefit at this stage. Since `scaffold.spec.ts` uses only explicit imports from `vitest` (not globals), it type-checks cleanly under the app config.

**`ScenarioView.vue` declares a `sessionId` prop.** The route uses `props: true`, which passes route params as component props. The stub view accepts `sessionId: string` as a typed prop rather than accessing `$route.params`. This is the correct Vue Router 4 pattern and avoids the `noUnusedParameters` TypeScript error that would appear if the prop were declared but not consumed.

**`@use './tokens'` instead of `@import` in `base.scss`.** Used SCSS module system (`@use`) rather than the legacy `@import` to avoid the Dart Sass deprecation warning. The `@import` directive is deprecated in Sass 1.75+ and would add noise to build output.

---

**Q4: What edge cases or setup difficulties did you discover in the Vue/Vite toolchain configuration?**

**`composite: true` + `noEmit: true` in TypeScript 5.4.** This combination is valid in TypeScript 5.x (composite projects used purely for type checking via `vue-tsc -b` do not need to emit). Older TypeScript versions required `declaration: true` with `composite: true`, but this was relaxed.

**`vue/multi-word-component-names` for `App.vue`.** The ESLint rule requires all Vue components to have multi-word names (e.g., `MyApp` not `App`). `App.vue` is the conventional single-word exception — it must be added to the rule's `ignores` list. Missing this causes an ESLint error that blocks the lint step.

**pnpm `ERR_PNPM_IGNORED_BUILDS`.** pnpm 8+ requires explicit approval of packages that run postinstall scripts. `esbuild@0.21.5`, `vue-demi@0.14.10`, and `@parcel/watcher@2.5.6` all have build scripts. Without running `pnpm approve-builds`, the initial `pnpm install` exits with error code 1. This is not documented in the batch instructions and requires interactive terminal input.

**Sass legacy JS API deprecation.** Vite 5.2 with Sass 1.75 produces a `[legacy-js-api]` deprecation warning at build time. This is a warning in stdout, not an ESLint warning, so it does not affect `--max-warnings 0` or exit codes. No action required until Vite 6 drops the legacy API.

**Build output to `../src/Tracer.Observer/wwwroot` (outside project root).** Vite warns about building outside the project root, but completes successfully. The `emptyOutDir: true` flag combined with an external output directory means any existing wwwroot contents from a previous build are wiped. This is the intended behavior per the design but could be surprising during development if wwwroot contains manually placed files.

---

**Q5: Are there any performance concerns, dependency conflicts, or tooling issues you noticed?**

**TypeScript version unsupported by `@typescript-eslint` v6.** The installed TypeScript is 5.4.5, but `@typescript-eslint/eslint-plugin@6.x` officially supports `>=4.3.5 <5.4.0`. Version 5.4.5 falls outside this range by the patch version. ESLint still runs and produces correct output, but the warning is printed on every lint invocation. The fix is to either pin TypeScript to `~5.3.0` or upgrade `@typescript-eslint` to v7/v8 (which supports TypeScript 5.4+). At this stage the functional impact is zero, but it will cause confusion in CI output.

**Pinia `^2.1.0` resolves to `2.3.1` (Pinia 3 available).** The installed version (2.3.1) works correctly but pnpm notes Pinia 3.0.4 is available. No migration required for the scaffold.

**`vue-router@^4.3.0` resolves to `4.6.4` (Vue Router 5 available).** Same pattern — works correctly at resolved version.

**Vitest globals not enabled.** `vite.config.ts` sets `test.globals: true` in the inline Vitest config, but the test file uses explicit imports (`import { describe, it, expect } from 'vitest'`). The `globals: true` setting is redundant but harmless. If globals are removed later, the explicit imports ensure the tests continue to work without modification.

---

**Q6: Suggested git commit message for this batch**

```
fix: BATCH-09 corrective cleanup (DT-001/2/4/5/9/16/17/18/19/20) + TRC-P3-006 Vue SPA scaffold

Backend corrective tasks:
- DT-001: parameterize LIMIT/OFFSET in EventQueryBuilder ($limit/$offset)
- DT-002: fix SQL injection test to target PayloadSearch field
- DT-004: add SequenceNumber/PayloadJson per-index assertions in DeterminismTests
- DT-005: fix different-seeds test to assert TraceId[0] differs between runs
- DT-009: populate SlowStateCount from real DuckDB row count in StartupRecovery;
           add CountSlowStateAsync to DuckDbStorageReader; add unit test
- DT-016: change IntervalRotator.CurrentWriter to internal set;
           add InternalsVisibleTo("Tracer.Tests.Integration") to Tracer.Agent
- DT-017: push 100 unique session_start events post-rotation; verify via /api/sessions
- DT-018: assert traceId hex, severity string, occurredAtUtc round-trip in GetEvent test
- DT-019: assert eventsPublished counts and firstSeenUtc per node in GetTopology test
- DT-020: assign explicit EventIds 1-20; extract+verify 20 distinct ids from SSE stream

Frontend scaffold (TRC-P3-006):
- Initialize tracer-viewer/ with Vue 3.4 + TypeScript 5.4 + Vite 5.2 + Pinia + Vue Router
- vite.config.ts: dev proxy /api → :5300, build → src/Tracer.Observer/wwwroot, Vitest inline
- Triple tsconfig (tsconfig.json / tsconfig.app.json / tsconfig.node.json)
- ESLint (vue3-recommended + @typescript-eslint/recommended) + Prettier
- SCSS design tokens and base styles (§6.12 color palette)
- TracerApiClient stub with typed DTOs and fetch calls for all 8 endpoints
- Vue Router with 3 routes (redirect / sessions / scenario/:sessionId)
- Pinia stores: sessionStore, liveStore, topologyStore
- App shell: App.vue, AppHeader, AppShell, LoadingSpinner, ErrorMessage, stub views
- Scaffold smoke tests: 3/3 passing (App import, ErrorMessage render/emit)
- pnpm run build / test:unit / lint all exit 0

224 backend tests passing (183 unit + 41 integration, 0 failures)
```

---

## ⚠️ Outstanding Issues / Next Steps

- [ ] The `@typescript-eslint` v6 + TypeScript 5.4.5 version mismatch prints a non-blocking warning on every lint run. Should upgrade to `@typescript-eslint` v7+ in a future batch when `eslint-plugin-vue` compatibility is confirmed.
- [ ] The SSE endpoint serializes DTOs without camelCase options (PascalCase output). This is inconsistent with the REST API and should be corrected in a future task to avoid frontend complexity.
- [ ] pnpm's `ERR_PNPM_IGNORED_BUILDS` approval is interactive. A `pnpm-workspace.yaml` with approved builds or a `pnpm.onlyBuiltDependencies` field in `package.json` should be added for non-interactive CI runs.
- [ ] TRC-P3-007 and TRC-P3-008 (SessionBrowserView and ScenarioView component implementations) are next in the frontend phase.
