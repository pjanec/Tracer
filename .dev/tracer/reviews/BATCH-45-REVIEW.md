# BATCH-45 Review

**Batch:** BATCH-45  
**Reviewer:** Dev Lead  
**Date:** 2025-07-17  
**Status:** ✅ APPROVED

---

## Tasks Reviewed

| Task | Description | Verdict |
|------|-------------|---------|
| TRC-P8-013 | annotationStore.ts + useAnnotations.ts | ✅ Pass |
| TRC-P8-017 | personaStore.ts + usePersona.ts + PersonaSwitcher.vue + integrations | ✅ Pass |

---

## Build Verification

`pnpm build` — 0 TypeScript errors. (20+ pre-existing errors were fixed as part of this batch.)

---

## Test Verification

```
Test Files  56 passed (56)
     Tests  266 passed (266)
  Duration  29.39s
```

| Suite | Passed | Failed |
|-------|--------|--------|
| annotationStore.spec.ts | 4 | 0 |
| useAnnotations.spec.ts | 4 | 0 |
| personaStore.spec.ts | 4 | 0 |
| usePersona.spec.ts | 2 | 0 |
| PersonaSwitcher.spec.ts | 4 | 0 |
| SessionCard.spec.ts (additions) | 3 | 0 |
| AppHeader.spec.ts | 1 | 0 |
| **Total new** | **22** | **0** |

✅ 266/266 pass. 0 regressions.

---

## Code Quality Observations

**Strengths:**
- `annotationStore` correctly uses keyed map (`Record<string, AnnotationDto>`) for O(1) lookup — `byEventId/byEntityId/byTraceId` getters work without linear scan
- `load()` replaces the map entirely (prevents duplication on double-load — SC-7 validated)
- `useAnnotations` watches `sessionId` with `{ immediate: true }` — auto-loads on mount
- `author` read from `localStorage` at create-time (not reactively) — correct pattern
- `personaStore` uses synchronous `localStorage.getItem` in `state()` factory — initializes correctly before first render
- `vi.resetModules()` used correctly in persona store tests to ensure fresh module instantiation after `localStorage.setItem`
- `PersonaSwitcher` correctly uses `persona-switcher__btn--active` CSS class — matches spec
- `SessionCard` routing: `engineer` → `timeline`, all others → `scenario` — correct

**Pre-existing bug fixes (bonus work, accepted):**
- `BundlesView.vue`: `isLive.value` → `isLive` in template (auto-unwrapped computed ref)
- `EntityHistoryView.vue`: `@select-event` type mismatch fixed
- `useCausalTreeUrl.ts`: `LocationQuery` cast fixed
- 20+ test file fixes (missing `sessionId` on `TraceTreeDto`, `vi.fn` generic syntax)
- `TimelineAxis.vue` and `timelineHitTest.ts`: unused variables removed

---

## New Debt Items

None. The pre-existing `TimelineAxis.vue` ResizeObserver functional gap (never attached) is noted in the report; leaving as P3 debt item.

---

## Decision

**APPROVED** — 22/22 new tests, 266/266 total. Build clean. Both TRC-P8-013 and TRC-P8-017 implemented correctly. Pre-existing TypeScript errors cleaned up as a bonus.

Update TASK-TRACKER.md: mark TRC-P8-013 ✅ and TRC-P8-017 ✅.

---

## 📝 Commit Message

```
feat(viewer): annotation store, persona store, PersonaSwitcher (P8-013, P8-017)

- annotationStore: Pinia store for annotation CRUD (keyed by annotationId)
- useAnnotations: composable with load/create/update/remove + sessionId watch
- personaStore: Pinia store persisting persona to localStorage (engineer default)
- usePersona: thin composable exposing persona, setPersona, allPersonas
- PersonaSwitcher.vue: three-button toggle with persona-switcher__btn--active class
- AppHeader.vue: PersonaSwitcher integrated in right side of header
- SessionCard.vue: persona-aware routing (engineer → timeline, others → scenario)
- Fix 20+ pre-existing TypeScript build errors across source and test files
- 22 new tests, 266/266 pass
```
