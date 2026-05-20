# BATCH-26 Report — TRC-P5-009 + TRC-P5-010

## Summary

All 7 tasks completed successfully.

---

## Changes Made

### Task 1 — Integrate `useTimelineUrl` into `TimelineView.vue`
- Added `import { useTimelineUrl } from '@/composables/useTimelineUrl'`
- Added `useTimelineUrl()` call in `<script setup>` after store initialization
- Satisfies TRC-P5-009 SC1: "is called in TimelineView.vue setup"

### Task 2 — Enhance `setFollowLive` to snap to live edge (`timelineStore.ts`)
- When `v === true`: computes `spanMs = viewportSpanMs`, sets `to = new Date(Date.now())`, `from = new Date(Date.now() - spanMs)`
- When `v === false`: preserves current viewport, only flips `followLive: false`
- Satisfies TRC-P5-010 SC7: viewport.to within 5s of Date.now() after enabling follow

### Task 3 — Conditional button label in `TimelineToolbar.vue`
- Changed static `>Follow</button>` to `>{{ store.viewport.followLive ? 'Following live' : 'Follow' }}</button>`
- Satisfies TRC-P5-010 SC7: button label changes to "Following live" when active

### Task 4 — Replace `useTimelineUrl.spec.ts` (5 tests, renamed + expanded)
Renamed tests (exact names from spec):
- `urlParams_restoreStoreStateOnMount` → `urlParams_AppliedToStoreOnMount`
- `storeChange_updatesUrl_debounced` → `storeChange_UpdatesUrlDebounced`
- `multipleTopicValues_encodedAsRepeatedParams` + `selectEvent_addsSelectParam` + `followLive_addsFollowTrueParam` + `routerReplace_notPush_preventsHistoryChurn` → collapsed to 3 tests:
  - `multipleFilterValues_EncodedAsRepeatedParams` (Part A: encode + Part B: decode via `vi.resetModules()`)
  - `selectedEvent_RoundTripsViaUrl` (Part A: encode + Part B: decode via `vi.resetModules()`)
  - `panGesture_UsesReplaceNotPush`

Net change: 6 → 5 tests (−1)

Round-trip Part B pattern used (correct pinia re-creation after resetModules):
```typescript
vi.resetModules();
const { createPinia: freshCreatePinia, setActivePinia: freshSetActivePinia } = await import('pinia');
const freshPinia = freshCreatePinia();
freshSetActivePinia(freshPinia);
const { useTimelineStore: freshStore } = await import('../../src/stores/timelineStore');
const { useTimelineUrl: freshUrl }     = await import('../../src/composables/useTimelineUrl');
```

### Task 5 — Replace `useTimelineLiveStream.spec.ts` (6 tests)
Renamed:
- `onMessage_callsAppendLiveEvent` → `receivedEvent_AppendedToStoreInListMode` (added `totalMatching` assertion)
- `filterChange_reconnects` → `filterChange_ReconnectsStream`
- `unmount_abortsConnection` kept as-is

Added 3 new tests:
- `followMode_ViewportSlidesOnNewEvent` — verifies viewport slides when `followLive=true` and event arrives beyond `viewport.to`
- `panGesture_DisablesFollow` — verifies `panBy()` sets `followLive=false` and viewport does NOT slide after
- `aggregateMode_LiveEventsDoNotAppend` — verifies `appendLiveEvent` is no-op in aggregate mode

Net change: 3 → 6 tests (+3)

### Task 6 — New test in `TimelineToolbar.spec.ts`
Added `followToggle_EnablesFollowAndSnapsToLiveEdge`:
- Sets up a live session with a 10-min viewport span
- Clicks the Follow button, asserts `followLive=true`
- Asserts `viewport.to` is within 5s of `Date.now()`
- Asserts span is preserved (10 min ±1s)
- Asserts button text changes to "Following live"

Net change: 2 → 3 tests (+1)

### Task 7 — Append 2 E2E tests to `timeline-view.spec.ts`
- `shareableUrl_SameViewOnReload` — applies topic filter, captures URL, reloads, verifies params preserved
- `autoFollow_KeepsLiveEdgeVisible` — enables follow, checks URL has `follow=true`, pan-click disables follow

(E2E tests not executed — require running dev server.)

---

## Verification Results

### Vitest
```
Test Files  25 passed (25)
     Tests  109 passed (109)
  Duration  3.86s
```

Final breakdown:
- `useTimelineUrl.spec.ts`: 5 tests ✓
- `useTimelineLiveStream.spec.ts`: 6 tests ✓
- `TimelineToolbar.spec.ts`: 3 tests ✓
- All other 22 spec files: unchanged and passing ✓

### TypeScript
`npx tsc --noEmit` → **0 errors**

---

## Issues Encountered

### `vi.resetModules()` + Pinia active instance
The batch instructions' Part B code called `setActivePinia(createPinia())` BEFORE `vi.resetModules()`. This would leave the fresh store without a correct active pinia because the new module import of `timelineStore` would use a different pinia instance. Fixed by using the correct pattern: call `vi.resetModules()` first, then re-import pinia, create and set a fresh pinia, then import the store and composable.

### `onUnmounted` Vue warnings (pre-existing)
Several tests emit `[Vue warn]: onUnmounted is called when there is no active component instance` — this is a pre-existing condition from BATCH-25 (composables called outside a component setup context). These are warnings only, not errors, and all tests pass.

---

## Developer Insights

**Issues encountered:** The `vi.resetModules()` + pinia re-creation ordering was the main complexity. Following the CRITICAL pattern from the batch instructions (re-import pinia after resetModules) resolved this cleanly.

**Weak points spotted:** The Vue warnings about `onUnmounted` being called outside a component instance are consistently present for composable unit tests. A test helper wrapping composable calls in a minimal component could suppress these, but that's a pre-existing issue not in scope for this batch.

**Design decisions:** Used the `vi.resetModules()` approach (5 tests) rather than the split approach (7 tests) to hit the exact 109 target count specified in the instructions. The round-trip tests correctly cover both encode and decode directions within a single `it()` block.
