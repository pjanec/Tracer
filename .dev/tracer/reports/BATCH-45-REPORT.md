# BATCH-45 Report — Annotation Store + Persona Store + PersonaSwitcher

**Date:** 2025-07-17  
**Tasks:** TRC-P8-013 (Annotation Store + Composable), TRC-P8-017 (Persona Store + Composable + PersonaSwitcher)  
**Status:** ✅ All tasks completed. Build clean. 266/266 tests pass.

---

## 1. Files Created / Modified

### New Source Files

| File | Purpose |
|------|---------|
| `src/stores/annotationStore.ts` | Pinia store for annotation CRUD, keyed by `annotationId` |
| `src/composables/useAnnotations.ts` | Composable wrapping annotation store; loads on `sessionId` change; sets `author` from localStorage |
| `src/stores/personaStore.ts` | Pinia store persisting current persona (`engineer` / `scenario-author` / `operator`) to `localStorage` |
| `src/composables/usePersona.ts` | Composable wrapping persona store; exposes `persona`, `setPersona`, `allPersonas` |
| `src/components/PersonaSwitcher.vue` | Three-button toggle component; active button has `persona-switcher__btn--active` class |

### Modified Source Files

| File | Change |
|------|--------|
| `src/components/AppHeader.vue` | Added `<PersonaSwitcher>` in header; added `<script setup>` with import; added `.app-header__persona { margin-left: auto; }` style |
| `src/components/SessionCard.vue` | Added `useRouter` + `usePersonaStore`; click routes to `timeline` for `engineer`, `scenario` for all other personas |

### New Test Files

| File | Tests | Result |
|------|-------|--------|
| `tests/unit/annotationStore.spec.ts` | 4 | ✅ |
| `tests/unit/useAnnotations.spec.ts` | 4 | ✅ |
| `tests/unit/personaStore.spec.ts` | 4 | ✅ |
| `tests/unit/usePersona.spec.ts` | 2 | ✅ |
| `tests/unit/PersonaSwitcher.spec.ts` | 4 | ✅ |
| `tests/unit/AppHeader.spec.ts` | 1 | ✅ |

### Modified Test Files

| File | Change | New Tests |
|------|--------|-----------|
| `tests/unit/SessionCard.spec.ts` | Added router + persona routing tests | 3 |

**Total new tests: 22. Total suite: 266/266 passing.**

---

## 2. Issues Encountered

### Pre-existing TypeScript Build Errors (vue-tsc)

`pnpm build` revealed a large number of pre-existing TypeScript errors across both source and test files (masked by earlier failures). All were fixed as part of this batch:

#### Source File Fixes

| File | Error | Fix |
|------|-------|-----|
| `src/rendering/timelineHitTest.ts` | `canvasWidth`, `canvasHeight` class fields assigned but never read | Removed the two private fields; constructor now computes `cellW`/`cellH` directly from params |
| `src/components/TimelineAxis.vue` | `axisEl` ref declared but never used | Removed the unused `axisEl` declaration (ResizeObserver was never attached to it) |
| `src/views/BundlesView.vue` | Template called `isLive.value` but Vue auto-unwraps refs — `boolean` has no `.value` | Changed `isLive.value` → `isLive` in both template usages |
| `src/composables/useCausalTreeUrl.ts` | `watch` callback passed `query` (type `LocationQuery`) to `applyRouteToStore` which requires `Record<string, string | string[]>` | Added `as Record<string, string | string[]>` cast in the `watch` callback |
| `src/rendering/causalTreeLayout.ts` | `TraceEdgeDto` imported but unused | Removed from import |
| `src/views/EntityHistoryView.vue` | `@select-event` handler assigned `SlowStateSampleDto` to `string` field | Changed to `$event.traceId ?? null` |

#### Test File Fixes

| File | Error | Fix |
|------|-------|-----|
| `tests/unit/entityHistoryView.spec.ts` | Unused `defineComponent` import | Removed |
| `tests/unit/fastStateChartRenderer.spec.ts` | Unused `beforeEach` import | Removed |
| `tests/unit/useBundleMode.spec.ts` | Unused `ref` import | Removed |
| `tests/unit/fastStateDrillDown.spec.ts` | `api as typeof mockApi` unsafe cast | Changed to `api as unknown as typeof mockApi` |
| `tests/unit/slowStateChart.spec.ts` | `let getContextSpy: ReturnType<typeof vi.spyOn>` — too narrow | Changed to `{ mockRestore(): void } \| undefined` |
| `tests/unit/entityEventStrip.spec.ts` | Same `getContextSpy` type | Same fix |
| `tests/unit/useCausalTreeLayout.spec.ts` | `TraceTreeDto` missing `sessionId` | Added `sessionId: ''` to `makeTree` return and inline objects |
| `tests/unit/useCausalTreeQuery.spec.ts` | `TraceTreeDto` missing `sessionId` | Added `sessionId: ''` |
| `tests/unit/causalTreeStore.spec.ts` | `TraceTreeDto` missing `sessionId` | Added `sessionId: ''` |
| `tests/unit/causalTreeRenderer.spec.ts` | `TraceTreeDto` missing `sessionId`; unused `_x, _y` params | Added `sessionId: ''` in two places; renamed params |
| `tests/unit/causalTreeHitTest.spec.ts` | `TraceTreeDto` missing `sessionId` | Added `sessionId: ''` |
| `tests/unit/causalTreeLayout.spec.ts` | `TraceTreeDto` missing `sessionId` in `makeLinearChain` and 3 inline trees | Added `sessionId: ''` in 4 locations |
| `tests/unit/EntityPickerView.spec.ts` | Unused `mountView` function; `vi.fn<() => Promise<...>>()` wrong generic | Removed `mountView`; changed to `vi.fn<[], Promise<EntityListDto>>()` |
| `tests/unit/EventInspector.spec.ts` | `vi.fn<(id: string) => Promise<...>>()` wrong generic | Changed to `vi.fn<[id: string], Promise<EventDto \| null>>()` |
| `tests/unit/annotationStore.spec.ts` (new) | `beforeEach(() => setActivePinia(...))` returned `Pinia` — not `void` | Added `{}` braces |
| `tests/unit/SessionCard.spec.ts` (new) | `let pushSpy: ReturnType<typeof vi.spyOn>` — `MockInstance` covariance | Changed to `// eslint-disable-next-line @typescript-eslint/no-explicit-any` + `any` |
| `tests/unit/BundlesView.spec.ts` | Mock returned plain `{ value: true }` objects; after template fix, template saw truthy object instead of `false` | Changed mock to use `shallowRef()` from Vue for proper auto-unwrap |

---

## 3. Design Decisions

### Annotation Store (`annotationStore.ts`)
- Keyed by `annotationId` for O(1) lookup. Getters `byEventId`, `byEntityId`, `byTraceId` return filtered arrays.
- `load()` replaces the entire map (full refresh). `upsert()` / `remove()` do targeted updates for post-create/update/delete.

### `useAnnotations` Composable
- `author` is read from `localStorage('tracer:authorName') ?? 'anonymous'` at the time of `createAnnotation` / `updateAnnotation`. This avoids reactive coupling to localStorage.
- `watch(sessionId, ..., { immediate: true })` ensures annotations load on mount and on session change.

### Persona Store (`personaStore.ts`)
- Persists to `localStorage('tracer:persona')` via a `$subscribe` watcher pattern (single action `setPersona` writes through).
- `state()` reads localStorage synchronously so the default state is populated on store creation — important for SSR-safe pattern.
- `ALL_PERSONAS` exported as a const array to avoid duplication between store and components.

### `SessionCard.vue` Persona Routing
- Uses `usePersonaStore()` directly (not `usePersona()` composable) to avoid the extra layer for this simple use case.
- Route name `timeline` for `engineer`, `scenario` for all others — matches existing router configuration.

### `BundlesView.vue` Template Fix
- The pre-existing bug used `isLive.value` in the template, which worked at runtime but was semantically incorrect. Vue auto-unwraps `ComputedRef<boolean>` to `boolean` in templates. Changed to `isLive` throughout.

---

## 4. Weak Points Spotted in Codebase

1. **`TimelineAxis.vue` ResizeObserver never attached**: `ResizeObserver` is created in `onMounted` but `ro.observe(...)` is never called. The width will never update dynamically. This is a pre-existing functional gap.

2. **`vi.fn<FunctionType>()` pattern**: Several test files used `vi.fn<() => Promise<T>>()` (passing a function type) when Vitest expects `vi.fn<Args[], ReturnType>()` (parameter and return separately). Corrected for the files encountered.

3. **`TraceTreeDto.sessionId` not added to many tests**: When `sessionId` was added as a required field to `TraceTreeDto`, many test fixtures were not updated. These had to be fixed en masse.

4. **Mock composables returning plain objects instead of refs**: Tests that mock Vue composables returning `ComputedRef<T>` used plain `{ value: T }` objects. These are not Vue refs and don't auto-unwrap in templates. Only caught when the template code was corrected. Pattern to watch for in future test additions.

---

## 5. Suggested Git Commit Message

```
feat(viewer): add annotation store, persona store, and PersonaSwitcher (P8-013, P8-017)

- annotationStore: Pinia store for annotation CRUD keyed by annotationId
- useAnnotations: composable loading annotations on sessionId change
- personaStore: Pinia store persisting engineer/scenario-author/operator to localStorage
- usePersona: composable wrapping personaStore
- PersonaSwitcher.vue: three-button toggle component for persona selection
- AppHeader.vue: integrated PersonaSwitcher in header
- SessionCard.vue: persona-aware routing (engineer → timeline, others → scenario)

Also fix 20+ pre-existing TypeScript build errors across source and test files:
- BundlesView.vue: template used .value on auto-unwrapped ComputedRef<boolean>
- EntityHistoryView.vue: @select-event assigned SlowStateSampleDto to string field
- useCausalTreeUrl.ts: watch callback passed LocationQuery to Record<string, string|string[]>
- causalTreeLayout.ts: unused TraceEdgeDto import
- timelineHitTest.ts: unused private class fields
- TimelineAxis.vue: unused axisEl ref
- Multiple test files: missing sessionId on TraceTreeDto, vi.fn generic syntax,
  getContextSpy type annotations, unused imports, BundlesView mock using shallowRef
```
