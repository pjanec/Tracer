# BATCH-09 Review

## Decision: APPROVED with P1 Corrective for BATCH-10

## Summary

BATCH-09 successfully delivers all 10 corrective debt items (DT-001 through DT-020) and the complete Vue SPA scaffold (TRC-P3-006). Backend tests remain at 183 unit + 41 integration = 224 total, all passing. The Vue project builds cleanly, passes its scaffold smoke tests, and lints with zero warnings. Quality of the corrective fixes is high — assertions are tight and behavioral. One P1 issue was discovered during review: the SSE endpoint serializes DTO properties with PascalCase naming while the REST API uses camelCase, creating a contract mismatch that will break TRC-P3-007's SSE parsing.

---

## Backend Corrective Tasks Review

### DT-001 — LIMIT/OFFSET Parameterization ✅

`EventQueryBuilder.Build` now appends `LIMIT $limit OFFSET $offset` to the SQL and adds `DuckDBParameter("limit", query.Limit)` and `DuckDBParameter("offset", query.Offset)` to the parameter list. The associated test `Build_NoFilters_ContainsLimitAndOffset` was updated to assert both parameters are present in the returned parameter collection. Clean implementation.

**Note:** Adding these two parameters changed the total parameter count in `Build_MinSeverityWarning_ExpandsToInClause` (which previously expected 2 parameters). The developer correctly updated the expected count to 4. This is expected cascading behavior from parameterizing previously-inline values.

### DT-002 — SQL Injection Test Fix ✅

`Build_SqlInjectionAttempt_IsParameterized` now passes the injection attempt via `PayloadSearch` (not `OwningPlayerId`). The test correctly asserts (a) the literal injection string is absent from the SQL, and (b) the `$search` parameter value wraps it in `%...%`. The dangerous LIKE escape-special-char code path is now verified.

### DT-004 — DeterminismTests Assertions ✅

The same-seed determinism test now compares `SequenceNumber` and `PayloadJson` at every index across the full sequence. Per-index comparison (not a sample) is the correct approach.

### DT-005 — DifferentSeeds First-Record Assertion ✅

`MockDataSource_DifferentSeeds_ProduceDifferentSequences` now asserts `firstRecordSeed1.TraceId != firstRecordSeed2.TraceId` using FluentAssertions `.NotBe()`. This is a tight, direct proof that the seeds produce different PRNG sequences.

### DT-009 — StartupRecoveryService SlowStateCount ✅

`CountSlowStateAsync` added to `DuckDbStorageReader` (concrete class). The method runs `SELECT COUNT(*) FROM slow_state`, returns 0 on any exception. `TryFinalizeAsync` now calls `reader.CountSlowStateAsync(ct)` and uses the result when building the manifest. A new unit test `StartupRecovery_OrphanWithSlowStateRows_SlowStateCountInManifest` creates a real DuckDB file with 3 slow_state rows, calls `RecoverAsync`, reads the manifest, and asserts `SlowStateCount == 3`. This is a behavioral test that would have caught the original bug.

**Observation:** `CountSlowStateAsync` was added to the concrete `DuckDbStorageReader` class only (not to `IDiagnosticStorageReader`). This is appropriate since the recovery path already uses the concrete type, but it means the method is not in the interface contract. Document as a design note — not a debt item.

### DT-016 — IntervalRotator.CurrentWriter Setter Visibility ✅

`CurrentWriter` setter changed to `internal set`. Both `InternalsVisibleTo("Tracer.Tests.Unit")` and `InternalsVisibleTo("Tracer.Tests.Integration")` are declared in `Tracer.Agent.csproj` via `<AssemblyAttribute>` — verified in `obj/Release/.../Tracer.Agent.AssemblyInfo.cs`. All test usages of the setter continue to compile.

### DT-017 — SecondInterval_QueriesReturnCurrentIntervalEvents ✅

Rewritten correctly: after rotation, 100 events with `Topic = "system.session_start"` and a GUID-unique `sessionId` are pushed, then `GET /api/sessions` asserts the unique sessionId appears in the response. This proves the `ReadOnlyConnectionPool` has refreshed to point at the new interval's DuckDB (interval 1 had no session_start with this ID). Strong behavioral proof.

### DT-018 — GetEvent_ById Field Assertions ✅

`GetEvent_ById_ReturnsCorrectEventDto` now constructs the pushed `EventRecord` with `TraceId = new TraceId(42)`, `Severity = Severity.Warning`, and a known `PublishWallclock`. Post-fetch assertions verify:
- `dto.TraceId == "000000000000002A"` (16-char uppercase hex of 42)  
- `dto.Severity == "Warning"`  
- `occurredAtUtc` round-trips within 1ms  

All three match the spec (TRC-P3-010 SC7). ✅

### DT-019 — GetTopology eventsPublished and firstSeenUtc ✅

Test pushes 3 events from `node-alpha` and 5 from `node-beta` (distinct constants). Asserts `eventsPublished = 3` for alpha and `eventsPublished = 5` for beta, plus a non-default `firstSeenUtc` for each. Correct.

### DT-020 — MultipleNodes SSE Distinct EventIds ✅

Assigns explicit `EventId((ulong)(i + 1))` values 1–20 (alpha gets 1–10, beta gets 11–20). After collecting 20 `data:` lines, extracts `doc.RootElement.GetProperty("EventId").GetString()` and asserts 20 distinct values in a HashSet.

**Important observation:** The developer correctly identified that the SSE endpoint serializes with PascalCase (`EventId`, not `eventId`) because `SseEndpoints.cs` calls `JsonSerializer.Serialize(dto)` without options. The test was written to match what the SSE actually produces (PascalCase). This is consistent test behavior, but the underlying SSE convention mismatch is a **P1 production bug** — see below.

---

## TRC-P3-006 — Vue SPA Scaffold Review

### Project Structure ✅

All 25 required files are present. The `tracer-viewer/` structure matches the Phase 3 design §2 layout. `pnpm-workspace.yaml` was added (necessary for pnpm workspace support).

### Build Configuration ✅

`vite.config.ts`:
- Dev server port 5173 ✅
- `/api/*` proxy to `http://localhost:5300` with `changeOrigin: true` ✅  
- Build output to `../src/Tracer.Observer/wwwroot` ✅
- Vitest inline config with `environment: 'jsdom'` ✅
- Rollup `manualChunks` (good optimization, not required) ✅

TypeScript triple-config (`tsconfig.json`, `tsconfig.app.json`, `tsconfig.node.json`) follows the standard Vue 3 + TypeScript 5 pattern. The addition of `@types/node` to support `path.resolve` in `vite.config.ts` is a necessary and correct decision.

### API Client Stub ✅

`tracerApiClient.ts` defines all 8 DTO interfaces with camelCase field names matching ASP.NET Core's default JSON serialization. All 8 methods (`listSessions`, `getSession`, `getScenarioPhases`, `getScenarioNotables`, `getScenarioState`, `getEvent`, `getTopology`, `getLiveStatus`) are implemented with real `fetch` calls and correct paths. The `api` singleton is exported.

**Note on camelCase convention:** The TypeScript DTOs correctly use camelCase (e.g., `eventId`, `traceId`, `occurredAtUtc`). This matches the REST API's JSON output. However, this creates a contract mismatch with the SSE stream (see P1 issue below).

### Stores ✅

`sessionStore.ts` — state fields, `load`, `refreshState`, `clear` all implement TRC-P3-006 SC5 correctly.  
`liveStore.ts` — `setConnected(true)` resets `reconnectAttempts` to 0; `onEvent` updates `lastEventAt`; `onReconnect` increments. ✅  
`topologyStore.ts` — minimal and correct.

### Components ✅

`ErrorMessage.vue` accepts `message: string` prop and emits `retry` on button click — verified by smoke test. ✅  
`LoadingSpinner.vue`, `AppHeader.vue`, `AppShell.vue` — all structural shell components. ✅  
Router uses `createWebHistory()` (not hash mode). ✅  
Stub views correctly accept `sessionId` prop (preventing TypeScript unused-variable warnings). ✅

### SCSS ✅

`tokens.scss` defines all 11 CSS custom properties from §6.12 with correct values.  
`base.scss` uses `@use './tokens'` (correct modern Sass syntax, avoids legacy `@import` deprecation). ✅

### Smoke Tests ✅

Three tests pass: `App` imports without error; `ErrorMessage` renders `message` prop; `ErrorMessage` emits `retry` on button click. Behavioral, not structural.

### ESLint ✅

The `vue/multi-word-component-names` rule was correctly suppressed for `App.vue` (this is the standard convention). Template reformatting for `vue/max-attributes-per-line` and `vue/singleline-html-element-content-newline` was done correctly.

---

## P1 Issue: SSE Serialization Produces PascalCase (New, must fix in BATCH-10)

### The Problem

`SseEndpoints.cs` (line 59) serializes `NotableEventDto` with `JsonSerializer.Serialize(dto)` — without JSON options. The default `System.Text.Json` behavior without options produces **PascalCase** property names (`EventId`, `TraceId`, `OccurredAtUtc`, `NotableLabel`, etc.).

All REST endpoints in this project use ASP.NET Core's default JSON middleware, which applies **camelCase** naming. The frontend `tracerApiClient.ts` (TRC-P3-006) correctly defines TypeScript DTO interfaces with camelCase field names (`eventId`, `traceId`, `occurredAtUtc`).

### Impact on TRC-P3-007

The `useLiveNotables` composable (TRC-P3-007) will parse SSE `data:` JSON as `NotableEventDto`. When it accesses `event.eventId`, `event.traceId`, `event.occurredAtUtc` — these will be `undefined` because the SSE JSON contains `EventId`, `TraceId`, `OccurredAtUtc`. The notable events list will render with all fields blank. This blocks TRC-P3-007 from being correctly implemented without this fix.

### Required Fix

In `src/Tracer.WebApi/Endpoints/SseEndpoints.cs`, define a shared `JsonSerializerOptions` instance with camelCase:

```csharp
private static readonly JsonSerializerOptions _sseJsonOptions = new()
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
};
```

Replace:
```csharp
var json = JsonSerializer.Serialize(dto);
```
With:
```csharp
var json = JsonSerializer.Serialize(dto, _sseJsonOptions);
```

The DT-020 test in `LiveStreamingTests.cs` currently uses `GetProperty("EventId")` (PascalCase) to extract the event ID. After this fix, it must be updated to `GetProperty("eventId")` (camelCase). This is a test update that must happen alongside the production fix.

Tracked as **DT-021 (P1)** — target BATCH-10 Corrective Task 0.

---

## P2 Issue: @typescript-eslint v6 TypeScript Version Mismatch

`@typescript-eslint/eslint-plugin@^6.13.0` officially supports `>=4.3.5 <5.4.0`; the installed TypeScript is `5.4.5`. ESLint prints a warning on every run. The fix is to upgrade `@typescript-eslint/eslint-plugin` and `@typescript-eslint/parser` to `^7.0.0` or `^8.0.0`, which both support TypeScript 5.4+. Track as **DT-022 (P2)** — target BATCH-10 alongside the frontend view tasks.

---

## Debt Tracker Updates

**New entries to add:**
- DT-021 (P1): SSE endpoint serializes PascalCase; REST uses camelCase; breaks TRC-P3-007 `useLiveNotables` parsing. Fix: add `JsonNamingPolicy.CamelCase` options to `SseEndpoints.cs` serializer call; update DT-020 test to use camelCase `eventId`.
- DT-022 (P2): `@typescript-eslint` v6 does not officially support TypeScript 5.4.5; upgrade to v7/v8 in `tracer-viewer/package.json`.

**Mark resolved:**
- DT-001 ✅
- DT-002 ✅
- DT-004 ✅
- DT-005 ✅
- DT-009 ✅
- DT-016 ✅
- DT-017 ✅
- DT-018 ✅
- DT-019 ✅
- DT-020 ✅ (test uses PascalCase correctly matching current SSE behavior — will need update in BATCH-10 alongside DT-021)
