# BATCH-24: Timeline Canvas Renderer + TimelineView Vue Components

**Batch Number:** BATCH-24  
**Tasks:** TRC-P5-004 (Timeline Canvas Renderer), TRC-P5-005 (TimelineView Vue Components)  
**Phase:** Phase 5 — Engineer Timeline View  
**Estimated Effort:** 14–18 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-23 (TRC-P5-002 + TRC-P5-003) — committed at `897c5a6`

---

## 📋 Onboarding & Workflow

### Developer Instructions

This batch implements the frontend Canvas2D rendering engine and the Vue component shell for the Timeline View. The rendering modules are pure TypeScript (no Vue reactivity, no DOM) and must be fully unit-testable via Vitest with a canvas mock. The Vue components wire up the canvas and provide the layout shell, route integration, and supporting components.

**Complete all tasks to 100% before submitting. Run all tests and fix ALL failures. Write the batch report when everything is green. No stopping midway to ask permission.**

### Required Reading (IN ORDER)
1. **Task Definitions:** `docs/TASK-DETAIL.md` — sections TRC-P5-004 and TRC-P5-005
2. **Phase 5 Design:** `docs/tracer_phase5_design.md` — sections §5.1–§5.8, §6.1–§6.5, §9.1–§9.2
3. **Previous Review:** `.dev/tracer/reviews/BATCH-23-REVIEW.md` — know what issues to avoid

### 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **TRC-P5-004:** Create all rendering modules → Write all required tests → **ALL tests pass** ✅
2. **TRC-P5-005:** Create Vue components + routes → Write all required tests → **ALL tests pass** ✅

**DO NOT** move to TRC-P5-005 until:
- ✅ All 5 rendering modules created
- ✅ All 4 test files written
- ✅ **`npm run test` (Vitest) passes completely**

### Build & Test Commands
```powershell
cd d:\Work\Tracer\tracer-viewer

# Install deps if needed
npm install

# Unit tests (Vitest)
npm run test
# OR run once:
npx vitest run

# Type check
npx tsc --noEmit

# Lint
npm run lint 2>&1 || true  # may not exist; skip if not configured
```

**Backend tests** — do not break them:
```powershell
cd d:\Work\Tracer
dotnet build Tracer.sln --configuration Release
dotnet test tests/Tracer.Tests.Unit --configuration Release --no-build
dotnet test tests/Tracer.Tests.Integration --configuration Release --no-build --filter "FullyQualifiedName!~Publish_ProducesExpectedLayout"
```

### Source Code Location
- **Frontend work area:** `d:\Work\Tracer\tracer-viewer\src\`
- **Rendering modules:** `d:\Work\Tracer\tracer-viewer\src\rendering\` (create new folder)
- **Types:** `d:\Work\Tracer\tracer-viewer\src\types\` (create new folder)
- **Unit tests:** `d:\Work\Tracer\tracer-viewer\tests\unit\`
- **E2E tests:** `d:\Work\Tracer\tracer-viewer\tests\e2e\`
- **API client:** `d:\Work\Tracer\tracer-viewer\src\api\tracerApiClient.ts`
- **Router:** `d:\Work\Tracer\tracer-viewer\src\router\index.ts`

### Report Submission
Submit your report to: `.dev/tracer/reports/BATCH-24-REPORT.md`

---

## Context

BATCH-23 added backend `/api/events` list and aggregate endpoints and extended SSE. BATCH-24 builds the frontend side: the pure TypeScript rendering engine and the Vue component shell. This is the first batch touching the frontend canvas and the timeline route.

**Existing frontend structure:**
```
tracer-viewer/src/
  api/tracerApiClient.ts    ← EXTEND with listEvents + aggregateEvents
  api/useApi.ts
  router/index.ts           ← EXTEND with /v/timeline/:sessionId + /bundles routes
  stores/ (existing)
  views/SessionBrowserView.vue, ScenarioView.vue, BundleOpenView.vue
  components/ (existing)
  composables/ (existing)
  utils/time.ts
```

**No existing `rendering/` or `types/` folders.** Create them.

**`tracer-viewer/package.json`** — use existing npm packages only. No new packages. Canvas2D is built-in. Vitest + `@vue/test-utils` are already installed.

---

## 🎯 Batch Objectives

1. Create `src/rendering/` module suite — pure, testable TypeScript
2. Create `src/types/timeline.ts` — shared TypeScript interfaces
3. Extend `src/api/tracerApiClient.ts` — `listEvents()` and `aggregateEvents()` methods
4. Create the Vue component shell for `TimelineView` and `BundlesView`
5. Add routes, update `src/router/index.ts`
6. Write all required tests per success conditions

---

## ✅ Tasks

### Task 1: TRC-P5-004 — Timeline Canvas Renderer

**Design Reference:** `docs/tracer_phase5_design.md` §5.5, §5.6, §5.7, §6.3

#### 1a. `src/types/timeline.ts` (NEW FILE)

TypeScript interfaces shared across rendering and components:

```typescript
export interface TimeRange {
  from: Date;
  to:   Date;
}

export interface TimelineFilter {
  topics?: string[];
  nodes?: string[];
  traceId?: string;
  entityIds?: string[];
  playerIds?: string[];
  severities?: string[];
  notablesOnly?: boolean;
}

// DTOs mirroring backend /api/events response
export interface EventDto {
  eventId: string;
  traceId: string;
  publishWallclock: string;  // ISO 8601
  publisherNode: string;
  topic: string;
  severity?: string;
  notableLabel?: string;
  payloadJson?: string;
}

export interface EventListDto {
  events: EventDto[];
  totalMatching: number;
  returned: number;
  truncated: boolean;
}

export interface EventAggregateBucketGroupDto {
  groupKey: string | null;
  count: number;
}

export interface EventAggregateBucketDto {
  bucketStartUtc: string;
  groups: EventAggregateBucketGroupDto[];
  total: number;
}

export interface EventAggregateDto {
  bucketDuration: string;
  buckets: EventAggregateBucketDto[];
}
```

#### 1b. `src/rendering/colorScheme.ts` (NEW FILE)

```typescript
// Deterministic per-node color from node name hash
export function getNodeColor(nodeName: string): string { ... }

// Returns a stable hex color; same name = same color on every call
// Use a simple hash function (e.g., djb2 or similar) to map name → hue
// Then convert to HSL with fixed saturation/lightness

export const SEVERITY_COLORS = {
  info:    '#5b9dff',
  warning: '#e8b048',
  error:   '#e85c5c',
} as const;
```

Design: §5.8 mentions `buildNodeColorMap(nodes)` uses `colorScheme.ts`. Implement `getNodeColor(name: string): string` as the core function.

#### 1c. `src/rendering/timelineLayout.ts` (NEW FILE)

**Critical function** (exact success condition names):

```typescript
/**
 * Choose the aggregate bucket duration based on the visible time span in ms.
 * Returns 'raw' when the span is small enough that raw events fit in the row budget.
 */
export function chooseBucketDuration(spanMs: number): string {
  // Design reference: docs/tracer_phase5_design.md §5.5
  // Thresholds (as described in design):
  //   >= 4h      → '5m'
  //   >= 1h      → '30s'
  //   >= 30 min  → '5s'
  //   >= 5 min   → '1s'
  //   >= 1 min   → '100ms'
  //   < 1 min    → 'raw'
}

/** Convert px-coordinate to timestamp (milliseconds) given viewport. */
export function pixelToMs(px: number, widthPx: number, fromMs: number, toMs: number): number

/** Convert timestamp (ms) to px-coordinate. */
export function msToPixel(ms: number, widthPx: number, fromMs: number, toMs: number): number

/** Compute swimlane Y-center for a node index. */
export function swimlaneY(nodeIndex: number, swimlaneHeightPx: number): number
```

#### 1d. `src/rendering/timelineHitTest.ts` (NEW FILE)

Implement the `HitIndex` class exactly as described in `docs/tracer_phase5_design.md §5.7`:
- 64 columns × 16 rows uniform grid
- `add(MarkerHitEntry)` — registers a marker in overlapping cells
- `addBucket(BucketHitEntry)` — registers a bucket
- `findMarkerAt(x, y)` — returns closest marker within hit radius, or null
- `findBucketAt(x, y)` — returns first bucket whose rect contains (x, y), or null

Exact types (export them for testing):
```typescript
export interface MarkerHitEntry { x: number; y: number; w: number; h: number; eventId: string; }
export interface BucketHitEntry { x: number; y: number; w: number; h: number; bucketStartUtc: string; nodeId: string; count: number; }
export class HitIndex { ... }
```

#### 1e. `src/rendering/timelineRenderer.ts` (NEW FILE)

Pure draw logic. See design §5.6 for the full module code. Key points:
- `render(ctx: CanvasRenderingContext2D, input: TimelineRenderInput): TimelineRenderOutput`
- In list mode: `ctx.arc()` for standard events, `ctx.fillRect()` for notables
- In aggregate mode: `ctx.fillRect()` for each bar
- Skips events outside `[fromMs, toMs]` range
- Builds `HitIndex` during render and returns it in the output
- Exports `TimelineRenderInput` and `TimelineRenderOutput` interfaces

#### 1f. `src/rendering/timelineAggregator.ts` (NEW FILE)

Client-side bucket merging for live mode. Used when new events arrive via SSE and the current view is in aggregate mode.

```typescript
/** 
 * Merges a new event into an existing aggregate result.
 * Finds the correct bucket for the event's timestamp and increments the matching group's count.
 * If no matching bucket/group exists, creates it.
 * Returns a new EventAggregateDto (immutable update).
 */
export function appendEventToAggregate(
  existing: EventAggregateDto,
  event: EventDto,
  groupBy: 'node' | 'topic' | 'severity' | 'none'
): EventAggregateDto { ... }
```

#### 1g. Tests for TRC-P5-004

**`tests/unit/timelineRenderer.spec.ts`** — 6 required tests:

Canvas mock setup needed:
```typescript
// jsdom doesn't implement canvas fully — mock the 2D context
function makeCanvasMock() {
  const calls = { arc: 0, fillRect: 0 };
  const ctx = {
    arc: vi.fn(() => { calls.arc++; }),
    fill: vi.fn(),
    fillRect: vi.fn(() => { calls.fillRect++; }),
    beginPath: vi.fn(),
    clearRect: vi.fn(),
    fillStyle: '',
    setTransform: vi.fn(),
  } as unknown as CanvasRenderingContext2D;
  return { ctx, calls };
}
```

Required test methods (exact names):
- `drawsOneMarkerPerEventInListMode` — render with 5 events → `ctx.arc` called exactly 5 times
- `drawsSquareForNotableEvents` — event with non-null `notableLabel` → `ctx.fillRect` (not `ctx.arc`)
- `drawsBarPerBucketGroupInAggregateMode` — 3 buckets × 2 nodes → `ctx.fillRect` called ≥ 6 times
- `skipsEventsOutsideViewport` — events outside `[fromMs, toMs]` → no draw calls for them
- `handlesEmptyEventsListWithoutError` — render with empty events list → no throw
- `hitIndexHasEntryForEachDrawnMarker` — for each drawn marker, `hitIndex.findMarkerAt(x, y)` is non-null

**`tests/unit/timelineLayout.spec.ts`** — 6 required tests:

Required test methods (exact names):
- `chooseBucketDuration_SubOneMinute_ReturnsRaw` — span < 60000ms → `'raw'`
- `chooseBucketDuration_FiveMinutes_Returns100ms` — span = 5*60*1000 → `'100ms'`
- `chooseBucketDuration_ThirtyMinutes_Returns5s` — span = 30*60*1000 → `'5s'`
- `chooseBucketDuration_OneHour_Returns30s` — span = 60*60*1000 → `'30s'`
- `chooseBucketDuration_FourHoursOrMore_Returns5m` — span = 4*60*60*1000 → `'5m'`
- `chooseBucketDuration_BoundaryValues_CorrectThresholdBehavior` — test boundary values at 60000, 300000, 1800000, 3600000, 14400000 ms

**`tests/unit/timelineHitTest.spec.ts`** — 7 required tests:

Required test methods (exact names):
- `findMarkerAt_ExactCoordinate_ReturnsMarker`
- `findMarkerAt_InsideRadius_ReturnsMarker`
- `findMarkerAt_OutsideAllMarkers_ReturnsNull`
- `findMarkerAt_TwoMarkersInSameCell_ReturnsCloserOne`
- `findBucketAt_PointInsideBucket_ReturnsBucket`
- `findBucketAt_PointOutsideBucket_ReturnsNull`
- `performanceWith1000Markers_FindTakesUnder1ms` — insert 1000 markers; run 100 random lookups; each under 1ms using `performance.now()`

**`tests/unit/colorScheme.spec.ts`** — 2 required assertions:
- `isDeterministic` — same node name → same hex color on two independent calls
- Severity colors `info`, `warning`, `error` are all distinct strings

---

### Task 2: TRC-P5-005 — TimelineView Vue Components

**Design Reference:** `docs/tracer_phase5_design.md` §5.1, §5.2, §6.1, §6.3, §6.4, §6.5, §8.3, §9.1, §9.2

#### 2a. Extend `src/api/tracerApiClient.ts`

Add interfaces and methods. The backend is at `GET /api/events` and `GET /api/events/aggregate`.

```typescript
export interface EventListRequestDto {
  sessionId: string;
  from?: Date;
  to?: Date;
  topics?: string[];
  nodes?: string[];
  traceId?: string;
  entityIds?: string[];
  playerIds?: string[];
  severities?: string[];
  notablesOnly?: boolean;
  limit?: number;
}

export interface EventAggregateRequestDto {
  sessionId: string;
  from: Date;
  to:   Date;
  bucketDuration: string;  // '100ms' | '1s' | '5s' | '30s' | '1m' | '5m' | '30m' | '1h'
  groupBy?: 'node' | 'topic' | 'severity' | 'none';
  topics?: string[];
  nodes?: string[];
  traceId?: string;
  entityIds?: string[];
  playerIds?: string[];
  severities?: string[];
  notablesOnly?: boolean;
}
```

Add to `TracerApiClient`:
```typescript
async listEvents(req: EventListRequestDto, opts?: { signal?: AbortSignal }): Promise<EventListDto>
async aggregateEvents(req: EventAggregateRequestDto, opts?: { signal?: AbortSignal }): Promise<EventAggregateDto>
async listBundles(): Promise<{ bundleId: string; label?: string; createdAtUtc: string }[]>
async buildBundle(sessionId: string): Promise<{ bundleId: string }>
```

Import `EventListDto` and `EventAggregateDto` from `@/types/timeline`.

**`listBundles()`** — `GET /api/bundle/list` (may return 404 or empty array if not available)  
**`buildBundle(sessionId)`** — `POST /api/bundle/build` with body `{ sessionId }`

#### 2b. `src/stores/timelineStore.ts` (NEW FILE)

Minimal Pinia store (full store is TRC-P5-006). For this batch, the store just needs to support the component tests:

```typescript
import { defineStore } from 'pinia';
import type { TimelineFilter } from '@/types/timeline';

export const useTimelineStore = defineStore('timeline', {
  state: () => ({
    sessionId: null as string | null,
    viewport: {
      from: new Date(),
      to: new Date(),
      followLive: false,
    },
    filter: {} as TimelineFilter,
    queryMode: 'list' as 'list' | 'aggregate',
    loading: false,
    error: null as string | null,
    selectedEventId: null as string | null,
    isLiveSession: false,
  }),
  actions: {
    setSession(id: string) { this.sessionId = id; },
    panBy(ms: number) {
      this.viewport = {
        from: new Date(this.viewport.from.getTime() + ms),
        to:   new Date(this.viewport.to.getTime() + ms),
        followLive: false,
      };
    },
    zoomBy(factor: number, centerMs: number) {
      const span = this.viewport.to.getTime() - this.viewport.from.getTime();
      const newSpan = span * factor;
      this.viewport = {
        from: new Date(centerMs - newSpan / 2),
        to:   new Date(centerMs + newSpan / 2),
        followLive: false,
      };
    },
    setFollowLive(v: boolean) { this.viewport = { ...this.viewport, followLive: v }; },
  },
  getters: {
    viewportSpanMs: (state) => state.viewport.to.getTime() - state.viewport.from.getTime(),
  },
});
```

#### 2c. `src/views/TimelineView.vue` (NEW FILE)

Top-level page component:
- Reads `:sessionId` from route params via `props: true`
- Calls `store.setSession(sessionId)` on mount
- CSS Grid layout shell: FilterPanel (left, 280px), TimelineCanvas (main, flex), EventInspector (right, 400px, hidden when no selection)
- Below the canvas: TimelineAxis
- Above the canvas: TimelineToolbar
- Does NOT need full filter wiring for this batch — use empty filter state

Minimal implementation — the component tree must render without errors:
```vue
<template>
  <div class="timeline-view">
    <TimelineToolbar />
    <div class="timeline-view__layout">
      <!-- FilterPanel placeholder -->
      <div class="filter-panel-placeholder" />
      <div class="timeline-view__main">
        <TimelineCanvas class="timeline-canvas" />
        <TimelineAxis />
      </div>
    </div>
  </div>
</template>
```

#### 2d. `src/components/TimelineCanvas.vue` (NEW FILE)

The canvas element wrapper. Key requirements:
1. Renders a `<canvas class="timeline-canvas">` element
2. Registers `pointerdown`, `pointermove`, `pointerup` event handlers on the canvas
3. `onPointerDown`: calls `setPointerCapture(e.pointerId)` and begins tracking drag
4. `onPointerMove`: if dragging, computes `dtMs = -(dx / canvas.clientWidth) * spanMs` and calls `store.panBy(dtMs)`
5. `onPointerUp`: calls `releasePointerCapture(e.pointerId)`, ends drag
6. Emits a `markerClick` event with the `eventId` when a marker is clicked (hit-test using `hitIndex`)
7. Uses `useCanvasRenderer` composable (or an inline stub for this batch)

**Minimal `useCanvasRenderer` stub** (full implementation is TRC-P5-006):
```typescript
// src/composables/useCanvasRenderer.ts (stub for this batch)
import { ref } from 'vue';
import type { HitIndex } from '@/rendering/timelineHitTest';

export function useCanvasRenderer(_canvasRef: Ref<HTMLCanvasElement | null>) {
  const hitIndex = ref<HitIndex | null>(null);
  // Full rendering wired in TRC-P5-006
  return { hitIndex };
}
```

#### 2e. `src/components/TimelineToolbar.vue` (NEW FILE)

Toolbar with:
- **Zoom preset buttons**: "5m", "1h", "Full session" — clicking "5m" sets viewport span to 5 minutes centered on current midpoint via `store.zoomBy(newSpan / currentSpan, center)`
- **Follow toggle button** with class `toolbar__follow--active` when `store.viewport.followLive === true`
- Follow button is **disabled** when `store.isLiveSession === false`
- `DensityIndicator` component embedded (see 2g)

```vue
<template>
  <div class="timeline-toolbar">
    <button @click="zoom5m">5m</button>
    <button @click="zoom1h">1h</button>
    <button @click="zoomFull">Full session</button>
    <button
      class="toolbar__follow"
      :class="{ 'toolbar__follow--active': store.viewport.followLive }"
      :disabled="!store.isLiveSession"
      @click="toggleFollow"
    >Follow</button>
    <DensityIndicator />
  </div>
</template>
```

#### 2f. `src/components/TimelineAxis.vue` (NEW FILE)

SVG tick row below the canvas. Renders time tick labels at regular intervals based on viewport span. Minimum 5 ticks, maximum 12 ticks. Format based on span:
- < 1 min: `.SSS` millisecond labels
- < 1 hour: `HH:mm:ss`
- otherwise: `HH:mm`

```vue
<template>
  <svg class="timeline-axis" :viewBox="`0 0 ${width} 32`">
    <text v-for="tick in ticks" :key="tick.ms" :x="tick.x" y="20" class="timeline-axis__label">
      {{ tick.label }}
    </text>
  </svg>
</template>
```

#### 2g. `src/components/DensityIndicator.vue` (NEW FILE)

Reads from `useTimelineStore()`.

When `queryMode === 'list'`: renders `"Showing N of M events"` (or `"Showing N events"` if not truncated).  
When `queryMode === 'aggregate'`: renders `"Buckets of Xs"` where X is the `bucketDuration`.

```vue
<template>
  <span class="density-indicator">
    <template v-if="store.queryMode === 'list'">
      Showing {{ returned }} of {{ total }} events
    </template>
    <template v-else>
      Buckets of {{ bucketDuration }}
    </template>
  </span>
</template>
```

#### 2h. `src/components/Swimlane.vue` (NEW FILE)

Per-node lane label. Props: `nodeName: string`, `color: string`, `index: number`.  
Renders a colored rectangle + node name text in the left-edge chrome.

#### 2i. `src/views/BundlesView.vue` (NEW FILE)

Page listing built bundles. See `docs/tracer_phase5_design.md §9.1`.

```vue
<template>
  <div class="bundles-view">
    <h1>Bundle Library</h1>
    <ul class="bundles__list">
      <li v-for="bundle in bundles" :key="bundle.bundleId" class="bundles__item">
        <a :href="`/api/bundle/${bundle.bundleId}/download`">{{ bundle.label ?? bundle.bundleId }}</a>
        <button @click="buildBundle(bundle.bundleId)">Build bundle</button>
      </li>
    </ul>
  </div>
</template>
```

On mount: calls `api.listBundles()` to populate `bundles`.  
`buildBundle(sessionId)`: calls `api.buildBundle(sessionId)`.

#### 2j. Extend `src/router/index.ts`

Add two lazy-loaded routes:
```typescript
{
  path: '/v/timeline/:sessionId',
  name: 'timeline',
  component: () => import('@/views/TimelineView.vue'),
  props: true,
},
{
  path: '/bundles',
  name: 'bundles',
  component: () => import('@/views/BundlesView.vue'),
},
```

---

### Tests for TRC-P5-005

#### `tests/unit/TimelineCanvas.spec.ts`

Required test method (exact name):
- `panHandler_capturesPointerOnDown` — mount `TimelineCanvas`; dispatch `pointerdown` event; assert `setPointerCapture` was called with the event's `pointerId`

```typescript
it('panHandler_capturesPointerOnDown', async () => {
  const setPointerCapture = vi.fn();
  const wrapper = mount(TimelineCanvas, {
    global: { plugins: [createPinia()] },
    attachTo: document.body,
  });
  const canvas = wrapper.find('canvas');
  // Mock setPointerCapture on the canvas element
  (canvas.element as any).setPointerCapture = setPointerCapture;
  await canvas.trigger('pointerdown', { pointerId: 42, clientX: 100, clientY: 50 });
  expect(setPointerCapture).toHaveBeenCalledWith(42);
});
```

#### `tests/unit/TimelineToolbar.spec.ts`

Required test methods (exact names):
- `followToggle_disabledWhenSessionNotLive` — mount with `isLiveSession = false`; Follow button has `disabled` attribute
- `zoomPreset_5m_setsViewportTo5MinuteSpan` — click "5m" button; store's viewport span is ≤ 5 minutes

```typescript
it('followToggle_disabledWhenSessionNotLive', () => {
  const pinia = createPinia();
  setActivePinia(pinia);
  const store = useTimelineStore();
  store.isLiveSession = false;
  const wrapper = mount(TimelineToolbar, { global: { plugins: [pinia] } });
  expect(wrapper.find('.toolbar__follow').attributes('disabled')).toBeDefined();
});

it('zoomPreset_5m_setsViewportTo5MinuteSpan', async () => {
  const pinia = createPinia();
  setActivePinia(pinia);
  const store = useTimelineStore();
  store.viewport.from = new Date('2026-01-01T10:00:00Z');
  store.viewport.to   = new Date('2026-01-01T12:00:00Z');  // 2h span initially
  const wrapper = mount(TimelineToolbar, { global: { plugins: [pinia] } });
  await wrapper.find('button[data-zoom="5m"], button:nth-child(1)').trigger('click');
  const spanMs = store.viewport.to.getTime() - store.viewport.from.getTime();
  expect(spanMs).toBeLessThanOrEqualTo(5 * 60 * 1000 + 1);  // ≤5min (+1ms rounding)
});
```

#### `tests/unit/BundlesView.spec.ts`

Mock `@/api/tracerApiClient`:
```typescript
vi.mock('@/api/tracerApiClient', () => ({
  api: {
    listBundles: vi.fn().mockResolvedValue([
      { bundleId: 'b1', label: 'Alpha', createdAtUtc: '2026-01-01T00:00:00Z' },
      { bundleId: 'b2', label: 'Beta',  createdAtUtc: '2026-01-02T00:00:00Z' },
      { bundleId: 'b3', label: null,    createdAtUtc: '2026-01-03T00:00:00Z' },
    ]),
    buildBundle: vi.fn().mockResolvedValue({ bundleId: 'new-bundle' }),
  },
}));
```

Required test methods (exact names):
- `bundlesView_listsAllBundlesFromApi` — after mount and flushPromises; 3 `<li class="bundles__item">` elements rendered
- `bundlesView_downloadLink_containsBundleId` — each item's anchor `href` includes the bundle's `bundleId`
- `bundlesView_buildBundleButton_callsBuildApi` — click "Build bundle" on first item; `api.buildBundle` called with `'b1'`

#### `tests/unit/DensityIndicator.spec.ts`

Required assertions:
- In `list` mode with `totalMatching=1200, returned=500, truncated=true`: rendered text contains "500" and "1200"
- In `aggregate` mode with `bucketDuration='5s'`: rendered text contains "Buckets of 5s"

#### `tests/e2e/timeline-view.spec.ts` (Playwright)

See `docs/TASK-DETAIL.md#trc-p5-005--timelineview-vue-components` for the 6 required test methods (exact names):
1. `timelineView_renders_canvasAfterSessionLoad`
2. `timelineView_pan_updatesUrlFromTo`
3. `timelineView_zoom_changesViewportSpan`
4. `timelineView_clickMarker_opensInspector`
5. `timelineView_clickBucket_zoomsIn`
6. `timelineView_followToggle_enablesAutoFollow`

**IMPORTANT:** Playwright e2e tests require the server to be running. They are only executed when `E2E=true` is set (see `tracer-viewer/playwright.config.ts`). Write the tests so they can run when `E2E=true`. For the automated test run of this batch, the Vitest unit tests are the gate — Playwright e2e tests are not run in CI for this batch.

Write the e2e tests using `test.describe` and implement them properly with real assertions, but they don't need to pass in the Vitest run. Structure them with a `baseURL` of `http://localhost:5300` and navigate to `/v/timeline/{sessionId}`.

---

## ⚠️ Important Implementation Notes

### Canvas Mock in Vitest

jsdom's `canvas` support is limited. In `timelineRenderer.spec.ts`, pass a mock `CanvasRenderingContext2D` object directly to `render()` — don't mount a component:

```typescript
const ctx = {
  arc: vi.fn(),
  fill: vi.fn(),
  fillRect: vi.fn(),
  beginPath: vi.fn(),
  clearRect: vi.fn(),
  setTransform: vi.fn(),
  fillStyle: '',
} as unknown as CanvasRenderingContext2D;

const output = render(ctx, input);
// assert ctx.arc.mock.calls.length === 5
```

### Component Tests with Pinia

All Vue component tests need:
```typescript
import { setActivePinia, createPinia } from 'pinia';
beforeEach(() => setActivePinia(createPinia()));
```

### `TimelineCanvas` pointer test

The pointer test needs `attachTo: document.body` to have a real DOM element with `setPointerCapture`. If `setPointerCapture` doesn't exist on the mock element, set it manually before triggering.

### BundlesView API methods

`listBundles()` and `buildBundle()` are new methods you add to `TracerApiClient`. If the backend doesn't have these endpoints yet, return sensible stubs. The API client methods should:
```typescript
async listBundles(): Promise<...[]> {
  const res = await fetch('/api/bundle/list');
  if (res.status === 404) return [];
  if (!res.ok) throw new Error(`listBundles: ${res.status}`);
  return res.json();
}
```

### TypeScript strict mode

The project has `strict: true` (implied by `TreatWarningsAsErrors` in .NET side but for TS check `tsconfig.json`). Avoid `any` casts except where genuinely needed for canvas mocks. Use `as unknown as T` rather than `as any`.

---

## 🧪 Testing Requirements

### Vitest Unit Tests (must pass 100%):

| File | Required Tests |
|---|---|
| `timelineRenderer.spec.ts` | 6 (exact names from §1g above) |
| `timelineLayout.spec.ts` | 6 (exact names from §1g above) |
| `timelineHitTest.spec.ts` | 7 (exact names from §1g above) |
| `colorScheme.spec.ts` | 2 assertions |
| `TimelineCanvas.spec.ts` | 1 (`panHandler_capturesPointerOnDown`) |
| `TimelineToolbar.spec.ts` | 2 (exact names from §2 above) |
| `BundlesView.spec.ts` | 3 (exact names from §2 above) |
| `DensityIndicator.spec.ts` | 2 assertions |

**All existing tests must still pass** (`npm run test`).

### Playwright E2E (not required to pass in Vitest run):
- Write all 6 tests in `tests/e2e/timeline-view.spec.ts` with correct assertions

### TypeScript:
- `npx tsc --noEmit` must exit 0

---

## 📊 Report Requirements

**Report to:** `.dev/tracer/reports/BATCH-24-REPORT.md`

Sections:
1. **Tasks Completed** — list each file created/modified
2. **Test Results** — exact counts (`npm run test` output)
3. **Issues Encountered** — problems hit during implementation and how you solved them
4. **Design Decisions** — choices made beyond the spec (e.g., canvas mock approach, component structure)
5. **Weak Points Spotted** — any code smell, performance concerns, or improvement opportunities
6. **Outstanding Items** — anything intentionally deferred (e.g., full useCanvasRenderer wiring deferred to TRC-P5-006)
7. **Suggested Commit Message** — one-line summary

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] All 5 rendering modules created in `src/rendering/`
- [ ] `src/types/timeline.ts` created
- [ ] `src/api/tracerApiClient.ts` extended with `listEvents`, `aggregateEvents`, `listBundles`, `buildBundle`
- [ ] `src/stores/timelineStore.ts` created (minimal version for component support)
- [ ] All 7 Vue components/views created
- [ ] `src/router/index.ts` extended with `/v/timeline/:sessionId` and `/bundles` routes
- [ ] All 6 `timelineRenderer.spec.ts` tests pass
- [ ] All 6 `timelineLayout.spec.ts` tests pass
- [ ] All 7 `timelineHitTest.spec.ts` tests pass
- [ ] `colorScheme.spec.ts` 2 assertions pass
- [ ] `TimelineCanvas.spec.ts` 1 test passes
- [ ] `TimelineToolbar.spec.ts` 2 tests pass
- [ ] `BundlesView.spec.ts` 3 tests pass
- [ ] `DensityIndicator.spec.ts` 2 assertions pass
- [ ] All 6 `timeline-view.spec.ts` Playwright tests written (not required to run)
- [ ] `npx tsc --noEmit` exits 0
- [ ] All previously-passing Vitest tests still pass
- [ ] Backend `dotnet test` still 324 unit + 72 integration passing
- [ ] Report submitted

---

## ⚠️ Common Pitfalls

1. **Don't mock `window.devicePixelRatio`** in renderer tests — pass `ctx` directly to `render()`, no DOM involved
2. **`chooseBucketDuration` boundary values**: `>=4h → '5m'`, `>=1h → '30s'` (not `>1h`). Test boundary values carefully.
3. **`findMarkerAt` distance check**: must compare against marker's `w/2` and `h/2` — a point at (x + w/2 + 1, y) should NOT return the marker
4. **Vue Test Utils + Pinia**: always call `setActivePinia(createPinia())` before each test that uses a store
5. **`flushPromises()`**: needed after mount in tests that trigger async data loading (BundlesView)
6. **Pointer test**: `setPointerCapture` may not be defined on the jsdom canvas element — mock it with `vi.fn()` before triggering

---

## 📚 Reference Materials
- **Task Defs:** `docs/TASK-DETAIL.md` — TRC-P5-004, TRC-P5-005
- **Phase 5 Design:** `docs/tracer_phase5_design.md` — §5.1–§5.8, §6, §9.1–§9.2
- **Previous Review:** `.dev/tracer/reviews/BATCH-23-REVIEW.md`
- **Existing test patterns:** `tracer-viewer/tests/unit/useScenarioQuery.spec.ts`, `scaffold.spec.ts`
- **Vite config (alias @):** `tracer-viewer/vite.config.ts` — `@` resolves to `src/`
- **Existing API client:** `tracer-viewer/src/api/tracerApiClient.ts` — follow the same pattern
