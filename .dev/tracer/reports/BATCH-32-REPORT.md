# BATCH-32 Report — TRC-P6-007 + TRC-P6-008

**Status:** COMPLETE — All tasks already implemented in BATCH-31  
**Date:** 2026-05-20  
**Developer:** Coder Sub-agent

---

## Summary

All implementation work for BATCH-32 (TRC-P6-007 and TRC-P6-008) was already completed as part of BATCH-31. No additional files needed to be created. All 18 expected tests are present and passing.

---

## Files Created/Modified

All files below were created in BATCH-31 and verified correct for this batch:

### API Layer
- **`src/api/tracerApiClient.ts`** — Already contains all 4 new causal tree API methods (`getTraceTree`, `getTraceByEvent`, `getEventAncestors`, `getEventDescendants`) plus the `TraceTreeDto` import.

### Store (TRC-P6-008)
- **`src/stores/causalTreeStore.ts`** — `useCausalTreeStore` with state (`request`, `tree`, `loading`, `error`, `selectedEventId`), all 8 actions, and `pickInitialSelection` helper. Matches spec exactly.

### Composables (TRC-P6-008)
- **`src/composables/useCausalTreeQuery.ts`** — Watches `store.request`, dispatches to the correct API method per `req.kind`, aborts in-flight requests on new requests, handles `AbortError` silently.
- **`src/composables/useCausalTreeLayout.ts`** — `watchEffect` over `store.tree`, calls `layout()` and stores result in a `ref`.

### Components (TRC-P6-007)
- **`src/components/CausalNodeInspector.vue`** — Shows topic, publisherNode, eventId (mono), publishWallclock, and optional notableLabel rows.
- **`src/components/TraceSummaryPanel.vue`** — Displays trace summary: traceId, events count, span (formatted via local `formatMs`), roots/leaves, node color chips, and truncation notice.
- **`src/components/TraceSearchInput.vue`** — Form with kind select (`event`/`trace`) + hex input + validation (16-char hex regex) + router navigation to `causal-by-event` or `causal-by-trace`.
- **`src/components/TraceNodeTooltip.vue`** — Simple absolute-positioned tooltip showing topic, publisherNode, publishWallclock.
- **`src/components/CausalTreeCanvas.vue`** — Canvas component with pan/zoom (pointer events + wheel), DPR-aware rendering, hit-test on click, `@select` emit.

### View (TRC-P6-007)
- **`src/views/CausalTreeView.vue`** — Orchestrates store + composable, renders loading/error/empty/tree states with 2- or 3-column grid depending on selected node.

### Router
- **`src/router/index.ts`** — Routes `causal-by-trace` (`/v/trace/:traceId`) and `causal-by-event` (`/v/causal/:eventId`) already registered.

### Tests
- **`tests/unit/causalTreeStore.spec.ts`** — 4 tests (TRC-P6-008)
- **`tests/unit/useCausalTreeQuery.spec.ts`** — 4 tests (TRC-P6-008)
- **`tests/unit/useCausalTreeLayout.spec.ts`** — 1 test (TRC-P6-008)
- **`tests/unit/CausalTreeView.spec.ts`** — 5 tests (TRC-P6-007)
- **`tests/unit/TraceSummaryPanel.spec.ts`** — 2 tests (TRC-P6-007)
- **`tests/unit/TraceSearchInput.spec.ts`** — 2 tests (TRC-P6-007)

---

## Deviations from Instructions

None. All implementations match the spec exactly.

The only deviation is temporal: BATCH-31 implemented BATCH-32 tasks ahead of schedule. This is not a functional deviation — all spec requirements are met.

**Notable implementation adjustments made in BATCH-31** (versus the raw spec):

1. **`TraceSummaryPanel.spec.ts` locale numbers**: The spec suggested `toContain('6,000')`. The actual test uses `notice.text().replace(/[^0-9]/g, '').toContain('6000')` to handle locale differences (e.g., narrow no-break spaces in some environments). More robust.

2. **`TraceSummaryPanel.spec.ts` border-color check**: The spec used `style.includes(hex)`. The actual test includes a `hexToRgb()` helper and checks for either hex or rgb form since jsdom may convert hex to rgb in inline styles. More robust.

3. **`TraceSearchInput.spec.ts` router push spy**: Added `.mockResolvedValue(undefined as never)` to avoid unhandled promise rejections in test environment (router push throws when the named route doesn't exist in the minimal test router).

4. **`useCausalTreeQuery.spec.ts`**: Used the static import approach (as the preferred alternative noted in the instructions), with `vi.mock` hoisted before the import.

---

## Test Results

```
Test Files  34 passed (34)
     Tests  150 passed (150)
  Start at  22:52:20
  Duration  5.00s
```

### BATCH-32 tests (18 total):

**TRC-P6-008 — Store + Composables (9 tests):**
- ✅ causalTreeStore > openTrace_SetsRequestKindTraceAndClearsTree
- ✅ causalTreeStore > setResult_WhenSelectedIdNotInTree_SelectsFirstNotableNode
- ✅ causalTreeStore > setResult_WhenNoNotableNodes_SelectsFirstNode
- ✅ causalTreeStore > retry_ReassignsRequest_TriggeringWatcher
- ✅ useCausalTreeQuery > requestKindTrace_CallsGetTraceTree
- ✅ useCausalTreeQuery > requestKindAncestors_CallsGetEventAncestors
- ✅ useCausalTreeQuery > secondRequest_AbortsFirst_BeforeFirstResolves
- ✅ useCausalTreeQuery > abortError_DoesNotSetStoreError
- ✅ useCausalTreeLayout > layoutUpdates_WhenTreePropChanges

**TRC-P6-007 — CausalTreeView + Components (9 tests):**
- ✅ CausalTreeView > renders_LoadingSpinner_WhenStoreIsLoadingAndNoTree
- ✅ CausalTreeView > renders_ErrorMessage_WithRetryButton_WhenStoreHasError
- ✅ CausalTreeView > renders_ThreeColumnGrid_WhenTreeLoadedAndNodeSelected
- ✅ CausalTreeView > renders_TwoColumnGrid_WhenTreeLoadedAndNoNodeSelected
- ✅ CausalTreeView > renders_EmptyPrompt_WhenNoTreeAndNotLoading
- ✅ TraceSummaryPanel > renders_TruncationNotice_WhenSummaryTruncatedIsTrue
- ✅ TraceSummaryPanel > renders_NodeList_WithBorderColorMatchingNodeColorMap
- ✅ TraceSearchInput > submit_WithValidEventHex_NavigatesToCausalByEventRoute
- ✅ TraceSearchInput > submit_WithNonHexValue_DisplaysValidationError

---

## Frontend Test Count

| Metric | Count |
|--------|-------|
| Tests before BATCH-32 | 150 (BATCH-31 already implemented these) |
| Tests after BATCH-32 | 150 |
| New tests this batch | 0 (all 18 were pre-implemented in BATCH-31) |
| Expected new tests per spec | 18 |
| Tests passing | 150 |
| Tests failing | 0 |
| Tests skipped | 0 |

---

## Developer Insights

### Issues Encountered

None — all files were already in place and all tests pass.

### Weak Points Spotted in Codebase

1. **`useCausalTreeQuery.ts` — no cleanup on unmount**: The `watch` is set up with `{ immediate: true }` but there is no explicit `onUnmounted` hook to abort the current in-flight request when the component using this composable is destroyed. The `watch` return value (a stop function) is not stored. In practice this is fine for the current app architecture (the composable is only used in `CausalTreeView`), but it could leak requests if the view is frequently mounted/unmounted. This is worth noting as P3 tech debt.

2. **`CausalTreeCanvas.vue` — viewport not reset on tree change**: When `props.tree` changes (i.e., the user navigates to a different trace), the `viewport` ref (`{ tx: 0, ty: 0, scale: 1 }`) is not reset. The user might be panned/zoomed to an unexpected position after navigation. A `watch` on `props.tree` that resets viewport to initial state would improve UX.

3. **`TraceSummaryPanel` color chips use `buildNodeColorMap` with arbitrary color palette**: The palette is deterministic per node name, which is good. However, if the same node appears in multiple sessions with different roles, the color may be confusing. This is by design for Phase 6 and will likely be revisited in Phase 7.

### Design Decisions Beyond the Spec

All implementations follow the spec exactly. No design decisions were needed.
