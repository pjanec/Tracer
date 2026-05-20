# BATCH-24 Report

**Batch**: BATCH-24  
**Tasks**: TRC-P5-004 (Timeline Canvas Renderer), TRC-P5-005 (TimelineView Vue Components)  
**Status**: COMPLETED

---

## Tasks Completed

### TRC-P5-004 — Timeline Canvas Renderer

All rendering infrastructure implemented as pure TypeScript (no Vue/DOM):

| File | Description |
|------|-------------|
| `src/types/timeline.ts` | Shared TS interfaces: `TimeRange`, `TimelineFilter`, `EventDto`, `EventListDto`, `EventAggregateBucketGroupDto`, `EventAggregateBucketDto`, `EventAggregateDto` |
| `src/rendering/colorScheme.ts` | Deterministic per-node colours via djb2 hash → HSL; `SEVERITY_COLORS` palette |
| `src/rendering/timelineLayout.ts` | Coordinate math: `chooseBucketDuration`, `msToPixel`, `pixelToMs`, `swimlaneY` |
| `src/rendering/timelineHitTest.ts` | 64×16 uniform grid spatial index; `HitIndex` class with `findMarkerAt`/`findBucketAt` |
| `src/rendering/timelineRenderer.ts` | Pure Canvas2D draw logic; `render(ctx, input): TimelineRenderOutput` supporting list and aggregate modes |
| `src/rendering/timelineAggregator.ts` | Client-side bucket merging: `appendEventToAggregate` for live SSE updates |

### TRC-P5-005 — TimelineView Vue Components

All Vue components, stores, composables, API extensions, and routes implemented:

| File | Description |
|------|-------------|
| `src/api/tracerApiClient.ts` | Extended with `listEvents`, `aggregateEvents`, `listBundles`, `buildBundle` |
| `src/stores/timelineStore.ts` | Pinia store: viewport state, `panBy`, `zoomBy`, `setFollowLive`, `viewportSpanMs` getter |
| `src/composables/useCanvasRenderer.ts` | Stub composable returning `{ hitIndex }` (full wiring deferred to TRC-P5-006) |
| `src/components/TimelineCanvas.vue` | `<canvas>` with pointer-capture drag-to-pan and click hit-testing; emits `markerClick` |
| `src/components/DensityIndicator.vue` | Shows "Showing N of M events" (list) or "Buckets of Xs" (aggregate) |
| `src/components/TimelineToolbar.vue` | Zoom preset buttons (5m/1h/full), follow toggle, contains `DensityIndicator` |
| `src/components/TimelineAxis.vue` | SVG tick row, 5–12 ticks, format adapts to span (ms/s/min/h) |
| `src/components/Swimlane.vue` | Coloured swatch + node name label |
| `src/views/TimelineView.vue` | CSS Grid layout: FilterPanel placeholder + toolbar + canvas + axis |
| `src/views/BundlesView.vue` | Lists bundles with download links and build-bundle button |
| `src/router/index.ts` | Added `/v/timeline/:sessionId` and `/bundles` routes |

---

## Test Results

### Frontend (Vitest)

```
Test Files  18 passed (18)
     Tests  74 passed (74)
  Duration  2.92s
```

Tests added this batch:

| File | Count |
|------|-------|
| `tests/unit/colorScheme.spec.ts` | 2 |
| `tests/unit/timelineLayout.spec.ts` | 9 |
| `tests/unit/timelineHitTest.spec.ts` | 7 |
| `tests/unit/timelineRenderer.spec.ts` | 6 |
| `tests/unit/TimelineCanvas.spec.ts` | 1 |
| `tests/unit/TimelineToolbar.spec.ts` | 2 |
| `tests/unit/DensityIndicator.spec.ts` | 2 |
| `tests/unit/BundlesView.spec.ts` | 3 |
| **Total new** | **32** |

E2E tests written (Playwright, not included in Vitest run): `tests/e2e/timeline-view.spec.ts` — 6 tests.

### TypeScript

```
npx tsc --noEmit  →  exit 0 (no errors)
```

### Backend (.NET)

```
dotnet test tests\Tracer.Tests.Unit --configuration Release --no-build
Failed: 0, Passed: 324, Skipped: 0, Total: 324
```

---

## Issues Encountered

### 1. `chooseBucketDuration` boundary conflict

The batch instructions specified `>= 5min → '1s'`, but also included the test:

```
chooseBucketDuration_FiveMinutes_Returns100ms: 300000ms → '100ms'
```

These are contradictory (300000ms == 5min). **Resolution**: changed the threshold to strictly `> MS_5M` (i.e. 300001ms → `'1s'`), making exactly 5 min return `'100ms'`. All tests pass consistently with this rule.

### 2. Invalid Vitest matcher in batch instructions

The instructions used `toBeLessThanOrEqualTo` which is not a valid Vitest/Jest matcher. **Fixed** to `toBeLessThanOrEqual` in `TimelineToolbar.spec.ts`.

---

## Design Decisions

1. **`HitIndex` uniform grid**: Chose 64×16 cells as a fixed grid (not dynamic) for O(1) insertion and O(1) lookup. At canvas sizes up to 4K wide this gives ~60px cells horizontally, sufficient for click precision on 10px markers.

2. **`chooseBucketDuration` strictly-greater boundary**: `> 5min` rather than `>= 5min` keeps the 5-minute exact viewport in the `'100ms'` bucket tier, which is the more useful granularity at that scale.

3. **`useCanvasRenderer` stub**: The composable is a minimal stub (returns `hitIndex: ref(null)`) to satisfy the import contract in `TimelineCanvas.vue`. Full canvas lifecycle (resize observer, `requestAnimationFrame` loop, store subscription) is intentionally deferred to TRC-P5-006 per the batch scope.

4. **Separate `EventDto` types**: `tracerApiClient.ts` had a pre-existing `EventDto` with an `occurredAtUtc` field. The new `src/types/timeline.ts` defines a different `EventDto` with `publishWallclock`. These are independent; rendering code imports exclusively from `@/types/timeline`.

5. **`BundlesView` 404 handling**: `listBundles()` returns `[]` on HTTP 404 (no bundle store configured) rather than throwing, so the view renders an empty list gracefully.

---

## Weak Points Spotted

1. **`useCanvasRenderer` stub**: The composable currently does nothing — `hitIndex` is always `null`. `TimelineCanvas.vue` calls `hitIndex.value?.findMarkerAt(x, y)` which will silently no-op until TRC-P5-006 wires the renderer.

2. **`TimelineAxis.vue` tick layout**: Tick positions are calculated in plain `computed` without a `ResizeObserver`. If the container width changes after mount, ticks do not reflow until the next reactive update. Should add `ResizeObserver` in TRC-P5-006.

3. **`store.zoomBy` in `TimelineToolbar`**: The `full` zoom preset sets `center` to `(from + to) / 2`, but the full session extents are not stored in `timelineStore` yet (deferred to TRC-P5-006). Currently the button computes center from viewport only, not from true session boundaries.

4. **`EventAggregateBucketGroupDto.buckets` ordering**: `timelineAggregator.ts` appends new buckets at the end; no sort is applied. If SSE events arrive out of order, bucket bars may render in wrong time order. TRC-P5-006 should sort by `bucketStartMs` before rendering.

---

## Outstanding Items

- TRC-P5-006: Full canvas lifecycle wiring (`useCanvasRenderer` complete implementation, `requestAnimationFrame` loop, store ↔ renderer data flow)
- TRC-P5-006: `ResizeObserver` for `TimelineAxis` and `TimelineCanvas`
- TRC-P5-006: True session-extent tracking for the `full` zoom preset
- TRC-P5-006: Sort buckets by `bucketStartMs` in aggregator

---

## Suggested Commit Message

```
feat(timeline): TRC-P5-004/005 canvas renderer and timeline Vue components

- Add pure-TS rendering pipeline: colorScheme, timelineLayout, timelineHitTest,
  timelineRenderer, timelineAggregator
- Add Pinia timelineStore with viewport pan/zoom/follow-live actions
- Add TimelineCanvas, TimelineToolbar, TimelineAxis, DensityIndicator, Swimlane
  components and TimelineView / BundlesView
- Extend TracerApiClient with listEvents, aggregateEvents, listBundles, buildBundle
- Add /v/timeline/:sessionId and /bundles routes
- 32 new Vitest unit tests (74 total passing); tsc --noEmit clean; 324 backend tests passing
```
