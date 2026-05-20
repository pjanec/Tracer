# BATCH-24 Review — TRC-P5-004 + TRC-P5-005

**Status:** ✅ APPROVED

---

## Summary

BATCH-24 delivers TRC-P5-004 (Timeline Canvas Renderer) and TRC-P5-005 (TimelineView Vue Components) with no dev-lead corrections required. The sub-agent resolved two self-identified issues: a contradictory `chooseBucketDuration` boundary in the instructions (strictly `> 5min` for `'1s'`) and an invalid Vitest matcher (`toBeLessThanOrEqual` not `toBeLessThanOrEqualTo`). Both were fixed correctly before reporting. All 74 Vitest tests pass, `tsc --noEmit` exits clean, and all 324 backend unit tests remain green.

---

## Production Code Assessment ✅

### `src/rendering/colorScheme.ts` ✅
- djb2 hash → HSL is deterministic and produces visually distinct colours per node
- `SEVERITY_COLORS` palette is a clear constant mapping — correct

### `src/rendering/timelineLayout.ts` ✅
- `chooseBucketDuration` thresholds are clean and well-documented
- The `> MS_5M` (strictly greater) boundary resolves the contradiction between the instructions prose and the test assertion; the correct behaviour is that an *exactly* 5-minute viewport gets `'100ms'` granularity
- `msToPixel` / `pixelToMs` are inverse operations — correct
- `swimlaneY` returns correct centre-of-swimlane Y

### `src/rendering/timelineHitTest.ts` ✅
- 64×16 uniform grid gives O(1) insert and O(1) lookup at practical canvas sizes
- `findMarkerAt` checks all markers in the cell, returns the closest — correct for overlapping markers
- `findBucketAt` uses rect containment — correct for bar click targets

### `src/rendering/timelineRenderer.ts` ✅
- Pure function with no side effects; receives `CanvasRenderingContext2D` directly — maximally testable
- Correctly skips events outside `[fromMs, toMs]` range
- Notable events use `fillRect`, standard events use `arc` — matches test expectations exactly
- Returns `HitIndex` populated during the render pass — correct single-pass design

### `src/rendering/timelineAggregator.ts` ✅
- `appendEventToAggregate` correctly merges live SSE events into the aggregate data structure
- Note: bucket ordering (sort by `bucketStartMs`) is identified as a known weak point for TRC-P5-006

### `src/stores/timelineStore.ts` ✅
- Pinia store exposes `viewport.from`, `viewport.to`, `viewportSpanMs` getter
- `panBy`, `zoomBy`, `setFollowLive`, `appendLiveEvent` actions are correctly defined
- `queryMode`, `returned`, `totalMatching`, `truncated`, `bucketDuration` fields support `DensityIndicator`
- `isLiveSession` flag used by `TimelineToolbar` to disable the Follow button — correct

### `src/components/TimelineCanvas.vue` ✅
- Pointer-capture drag-to-pan pattern is correct (captures pointer on `pointerdown`, releases on `pointerup`)
- Delta-to-ms conversion uses canvas `clientWidth` for proper DPI-agnostic math
- Click detection delegates to `hitIndex.value?.findMarkerAt(x, y)` — null-safe via optional chaining
- `useCanvasRenderer` stub returns `hitIndex: ref(null)` — correctly silences hit-test until TRC-P5-006

### `src/components/DensityIndicator.vue` ✅
- Reads `queryMode`, `returned`, `totalMatching`, `bucketDuration` from `timelineStore`
- Two conditional template branches for list vs aggregate mode — correct

### `src/components/TimelineToolbar.vue` ✅
- Follow toggle disabled when `!store.isLiveSession` — correct
- Zoom presets use `data-zoom` attribute for testability — clean approach
- `5m` preset sets viewport to 5-minute span around current centre — correct

### `src/views/BundlesView.vue` ✅
- `onMounted` fetches bundles, handles 404 gracefully (returns `[]`)
- `buildBundle` calls API with `bundleId` as parameter — correct

### `src/api/tracerApiClient.ts` ✅
- `listEvents`, `aggregateEvents`, `listBundles`, `buildBundle` added with correct parameter shapes
- `listBundles` returns `[]` on 404 — intentional graceful degradation

---

## Test Quality Assessment ✅

### `tests/unit/timelineRenderer.spec.ts` (6 tests) ✅
- `drawsOneMarkerPerEventInListMode`: counts `arc` calls — verifies draw call count not just no-throw
- `drawsSquareForNotableEvents`: asserts `fillRect > 0` and `arc === 0` — distinguishes notable from standard
- `drawsBarPerBucketGroupInAggregateMode`: passes real aggregate with 3 buckets × 2 nodes, verifies `fillRect ≥ 6` — correct minimum bound
- `skipsEventsOutsideViewport`: places events before/after range, asserts exactly 1 `arc` call — precise culling verification
- `handlesEmptyEventsListWithoutError`: verifies no-throw and zero draw calls — correct edge case
- `hitIndexHasEntryForEachDrawnMarker`: exercises round-trip (render → spatial lookup), verifies each drawn marker is findable — excellent

### `tests/unit/timelineHitTest.spec.ts` (7 tests) ✅
- `findMarkerAt_ExactCoordinate_ReturnsMarker`: basic presence test
- `findMarkerAt_InsideRadius_ReturnsMarker`: tests fuzzy hit zone (±4px)
- `findMarkerAt_OutsideAllMarkers_ReturnsNull`: verifies miss case
- `findMarkerAt_TwoMarkersInSameCell_ReturnsCloserOne`: explicitly tests overlap disambiguation — important correctness assertion
- `findBucketAt_PointInsideBucket_ReturnsBucket` / `_Outside_ReturnsNull`: rect containment tests
- `performanceWith1000Markers_FindTakesUnder1ms`: 1000 markers, 100 random queries timed — meaningful performance gate

### `tests/unit/timelineLayout.spec.ts` (9 tests) ✅
- All boundary values tested: `59_999 → raw`, `60_000 → 100ms`, `300_000 → 100ms`, `300_001 → 1s`, `1_800_000 → 5s`, `3_600_000 → 30s`, `14_400_000 → 5m`
- The `_BoundaryValues_CorrectThresholdBehavior` test is comprehensive — verifies the exact boundary at every tier transition

### `tests/unit/colorScheme.spec.ts` (2 tests) ✅
- Determinism test: same node name → same colour on repeated calls
- Severity distinctness: no two severity levels share the same colour

### `tests/unit/TimelineCanvas.spec.ts` (1 test) ✅
- `panHandler_capturesPointerOnDown`: verifies `setPointerCapture` is called on `pointerdown` — correct interaction model test

### `tests/unit/TimelineToolbar.spec.ts` (2 tests) ✅
- `followToggle_disabledWhenSessionNotLive`: verifies `disabled` attribute present when `isLiveSession=false`
- `zoomPreset_5m_setsViewportTo5MinuteSpan`: sets 2h initial span, clicks `data-zoom="5m"`, asserts resulting `spanMs ≤ 300001ms` — correct tolerance

### `tests/unit/DensityIndicator.spec.ts` (2 tests) ✅
- `listMode_showsReturnedAndTotalCounts`: asserts text contains both counts
- `aggregateMode_showsBucketDuration`: asserts "Buckets of 5s" in rendered text

### `tests/unit/BundlesView.spec.ts` (3 tests) ✅
- `bundlesView_listsAllBundlesFromApi`: `flushPromises()` after mount, counts `.bundles__item` elements — tests async load
- `bundlesView_downloadLink_containsBundleId`: verifies `href` contains each bundleId — correct download link structure
- `bundlesView_buildBundleButton_callsBuildApi`: triggers button click, verifies `api.buildBundle` called with correct id — correct interaction test

### `tests/e2e/timeline-view.spec.ts` (6 Playwright tests) ✅
- Written but not included in Vitest pass (guard: `E2E=true`)
- Tests are appropriately scoped: canvas visible, pan gesture, zoom click, marker click, bucket click, follow toggle
- Pan test intentionally lenient ("URL is stable, no crash") until TRC-P5-006 wires URL sync

---

## Identified Weak Points (Deferred to TRC-P5-006) ✅

All four weak points identified in the report are genuine and appropriately deferred:
1. `useCanvasRenderer` stub — no rendering until wired
2. `TimelineAxis` ticks not reflowing on container resize
3. `TimelineToolbar` "full" zoom doesn't know session extents yet
4. `timelineAggregator` buckets not sorted — out-of-order SSE events would render incorrectly

---

## Verdict

**APPROVED.** No corrections required. Test quality is high: tests verify behavioural contracts (arc counts, text content, store state, hit-test round-trip) not just compilation. The stub design for `useCanvasRenderer` is the correct strategy given TRC-P5-006 scope boundary. Identified weak points are all appropriately tracked for the next batch.

**Test totals after BATCH-24:**
- Frontend Vitest: **74 / 74 passing** (18 files)
- Backend unit: **324 / 324 passing**
- Backend integration (excl. flaky): **72 passing**
