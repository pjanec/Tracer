# BATCH-50 Instructions — Phase 9 Frontend: Latency Analysis, Gap Detection, Network Topology

**Batch:** BATCH-50  
**Tasks:** TRC-P9-011 through TRC-P9-019  
**Phase:** 9 — Replication Latency, Gap Detection, Network Topology (Frontend)  
**Report to:** `.dev/tracer/reports/BATCH-50-REPORT.md`

---

## Onboarding

Read before starting:
- `docs/tracer_phase9_design.md` — full Phase 9 design (core reference)
- `docs/TASK-DETAIL.md` — sections TRC-P9-011 through TRC-P9-019
- `.dev/tracer/reviews/BATCH-45-REVIEW.md` — for existing Vue component patterns
- `.dev/tracer/reviews/BATCH-47-REVIEW.md` — for composable patterns (useBookmarks, BookmarkBar)
- `tracer-viewer/src/router/index.ts` — existing routes pattern
- `tracer-viewer/src/api/tracerApiClient.ts` — existing API client (add Phase 9 DTOs here)

Phase 9 frontend is bundle-mode only. All three new views show a `BundleModeRequiredBanner` and hide content when the API returns HTTP 409.

---

## Architecture Notes

### 1. API Types to Add to `tracerApiClient.ts`

Add the following DTOs and API methods to `tracer-viewer/src/api/tracerApiClient.ts`:

```typescript
// Latency DTOs
export interface HistogramBucketDto {
  index: number;
  lowMs: number;
  highMs: number;
  count: number;
}

export interface LatencyDistributionDto {
  sampleCount: number;
  p50Ms: number;
  p90Ms: number;
  p99Ms: number;
  p999Ms: number;
  maxMs: number;
  minMs: number;
  meanMs: number;
  stddevMs: number;
  buckets: HistogramBucketDto[];
}

export interface LatencyPairSummaryDto {
  topic: string;
  publisherNode: string;
  subscriberNode: string;
  sampleCount: number;
  p50Ms: number;
  p99Ms: number;
  maxMs: number;
}

export interface LatencyTimeSeriesPointDto {
  bucketStartUtc: string;
  bucketEndUtc: string;
  sampleCount: number;
  p50Ms: number;
  p99Ms: number;
}

export interface LatencyTimeSeriesDto {
  points: LatencyTimeSeriesPointDto[];
}

export interface LatencyOutlierDto {
  topic: string;
  publisherNode: string;
  subscriberNode: string;
  publishWallclockUtc: string;
  latencyMs: number;
  thresholdMs: number;
  budgetSource: string; // "budget" | "top-0.1%"
}

export interface LatencyOutlierListDto {
  outliers: LatencyOutlierDto[];
}

// Gap DTOs
export interface GapDto {
  topic: string;
  publisherNode: string;
  subscriberNode: string;
  previousSequence: number;
  resumedAtSequence: number;
  missingCount: number;
  resumedAtWallclockUtc: string;
}

export interface GapResultDto {
  gaps: GapDto[];
  totalGaps: number;
}

// Network Topology DTOs
export interface TopologyEdgeDto {
  topic: string;
  publisherNode: string;
  subscriberNode: string;
  messageCount: number;
  firstSeenUtc: string;
  lastSeenUtc: string;
}

export interface NetworkTopologyDto {
  nodes: string[];
  edges: TopologyEdgeDto[];
}

// Budget DTOs
export interface LatencyBudgetDto {
  topic: string;
  p99BudgetMs?: number;
  absoluteMaxMs?: number;
}

export interface LatencyBudgetListDto {
  budgets: LatencyBudgetDto[];
}
```

**API methods to add:**

```typescript
// In the tracerApiClient singleton-style export:

async getLatencyDistribution(params: {
  from: string; to: string;
  topic?: string; publisherNode?: string; subscriberNode?: string;
  excludeSelf?: boolean;
}, signal?: AbortSignal): Promise<LatencyDistributionDto>

async getLatencyPairs(params: {
  from: string; to: string;
  minSamples?: number; limit?: number;
}, signal?: AbortSignal): Promise<LatencyPairSummaryDto[]>

async getLatencyTimeSeries(params: {
  from: string; to: string;
  topic?: string; publisherNode?: string; subscriberNode?: string;
}, signal?: AbortSignal): Promise<LatencyTimeSeriesDto>

async getLatencyOutliers(params: {
  from: string; to: string;
  topic?: string; publisherNode?: string; subscriberNode?: string;
}, signal?: AbortSignal): Promise<LatencyOutlierListDto>

async getGaps(params: {
  from: string; to: string;
  topic?: string; publisherNode?: string; subscriberNode?: string;
}, signal?: AbortSignal): Promise<GapResultDto>

async getNetworkTopology(params: {
  from: string; to: string;
}, signal?: AbortSignal): Promise<NetworkTopologyDto>

async getLatencyBudgets(sessionId: string, signal?: AbortSignal): Promise<LatencyBudgetListDto>
```

### 2. `BundleModeRequiredBanner` Component

Create `src/components/BundleModeRequiredBanner.vue`:
- CSS class `bundle-mode-required-banner`
- Shows an info/warning panel
- Content: "This view requires bundle mode." + explanation "Replication latency analysis requires per-node receive times, which are only available when a bundle has been opened."
- Text must contain "requires bundle mode" (test requirement)
- Optional `detail` prop for API-provided detail text

### 3. Composables Pattern

All five composables follow this exact pattern (see existing `useTimelineQuery.ts` for reference):

```typescript
export function useLatencyDistribution(filter: Ref<LatencyFilter | null>) {
  const distribution = ref<LatencyDistributionDto | null>(null);
  const loading = ref(false);
  const error = ref<{ status: number } | null>(null);
  let controller: AbortController | null = null;

  async function fetchFn() {
    if (!filter.value?.from || !filter.value?.to) return;
    controller?.abort();
    controller = new AbortController();
    loading.value = true;
    error.value = null;
    try {
      distribution.value = await tracerApiClient.getLatencyDistribution(
        { ...filter.value },
        controller.signal
      );
    } catch (e: unknown) {
      if (e instanceof Error && e.name === 'AbortError') return;
      const status = (e as { status?: number }).status ?? 0;
      error.value = { status };
      distribution.value = null;
    } finally {
      loading.value = false;
    }
  }

  watch(filter, fetchFn, { immediate: true, deep: true });
  onUnmounted(() => controller?.abort());

  return { distribution, loading, error };
}
```

**Type `LatencyFilter`:**
```typescript
export interface LatencyFilter {
  from: string | null;
  to: string | null;
  topic?: string;
  publisherNode?: string;
  subscriberNode?: string;
}
```

### 4. Canvas Renderer Files

Put renderer files in `src/rendering/`:
- `histogramRenderer.ts` — `renderHistogram(ctx, input: HistogramRenderInput)`, `formatMs(ms: number): string`
- `latencyTimeSeriesRenderer.ts` — `renderTimeSeries(ctx, input: TimeSeriesRenderInput)`, `hitTestTimeSeries(...)`
- `networkGraphLayout.ts` — `layoutGraph(input: GraphLayoutInput): LaidOutGraph`
- `networkGraphRenderer.ts` — `renderGraph(ctx, input: GraphRenderInput)`

### 5. Naming Conflicts

- Do NOT use `TopologyDto` — it's already used by Phase 3 in `Dtos.cs`. The network topology type for Phase 9 in the frontend is `NetworkTopologyDto` (consistent with backend).
- Do NOT use `useTopology` if a composable with that name already exists — check first.

### 6. Route Names

| Route path | Route name |
|---|---|
| `/v/latency/:sessionId` | `replication-latency` |
| `/v/gaps/:sessionId` | `gap-detection` |
| `/v/topology/:sessionId` | `network-topology` |

---

## Tasks

### TRC-P9-011 — `ReplicationLatencyView.vue` — Main Latency View

**File:** `tracer-viewer/src/views/ReplicationLatencyView.vue`  
**Route:** `/v/latency/:sessionId` (name `replication-latency`)

**Behavior:**
- On mount: fetch session (from/to), budgets via `getLatencyBudgets(sessionId)`, and pairs via `getLatencyPairs({ from, to, minSamples: 50, limit: 200 })`
- `selectedPair` reactive ref (initially `null`); when set, the filter for distribution/timeseries/outliers composables is narrowed to `{ topic, publisherNode, subscriberNode }` from the pair
- Three-panel layout: matrix (left), distribution + timeseries (centre), outliers (right)
- `BundleModeRequiredBanner` shown when any Phase 9 endpoint returns 409; content panels hidden
- Add a link to this view from the session card (use text "Latency" or "Latency analysis")
- Register route in `tracer-viewer/src/router/index.ts`

**Required tests (unit):**
1. `ReplicationLatencyView_MountsWithPairList` — stub API with 3 pairs; assert `PublisherSubscriberMatrix` receives `pairs` prop length 3
2. `ReplicationLatencyView_409_ShowsBanner` — stub pairs endpoint returning 409; assert `BundleModeRequiredBanner` is rendered; content panels absent
3. `ReplicationLatencyView_SelectPair_UpdatesComposableFilter` — mount with 3 pairs; simulate click on second pair; assert `selectedPair` equals second pair; assert filter `topic` matches
4. `ReplicationLatencyView_ClearPair_ResetsFilter` — with `selectedPair` set; click × button; assert `selectedPair === null`

**Required E2E tests** (in `tracer-viewer/tests/e2e/replication-latency-view.spec.ts`; skipped unless `E2E=true`):
1. "bundle session shows pair matrix" — navigate to `/v/latency/{sessionId}`; assert `.pair-matrix__row` visible; assert `h1` text is "Replication latency"
2. "live mode shows bundle required banner" — visit latency view against live Observer; assert `.bundle-mode-required-banner` visible; assert contains "requires bundle mode"

---

### TRC-P9-012 — `LatencyDistributionChart.vue` and `histogramRenderer.ts`

**Files:**
- `tracer-viewer/src/rendering/histogramRenderer.ts`
- `tracer-viewer/src/components/LatencyDistributionChart.vue`

**`histogramRenderer.ts` exports:**
```typescript
export interface HistogramRenderInput {
  distribution: LatencyDistributionDto;
  budget?: LatencyBudgetDto | null;
  canvasWidth: number;
  canvasHeight: number;
}
export function renderHistogram(ctx: CanvasRenderingContext2D, input: HistogramRenderInput): void
export function formatMs(ms: number): string // μs for < 1ms, ms for 1-1000ms, s for > 1000ms
```

**Rendering rules:**
- Log10 scale on x-axis (use `b.lowMs` and `b.highMs` from bucket)
- Dashed vertical percentile lines: p50 (`#4ec97a`), p99 (`#e8b048`), p99.9 (`#e85c5c`)
- Budget lines (thicker, solid) when `budget.absoluteMaxMs` or `budget.p99BudgetMs` is provided
- Upper-right summary: sample count, p50, p99, max
- Centre "No data in range" when `sampleCount === 0` or `buckets` empty

**`LatencyDistributionChart.vue`:**
- `<canvas>` with ResizeObserver calling `renderHistogram` on resize and prop changes
- Props: `distribution: LatencyDistributionDto | null`, `budget: LatencyBudgetDto | null`, `loading: boolean`

**Required tests (unit):**
1. `histogramRenderer.spec.ts` — `EmptyDistribution_DrawsNoDataMessage` — fillText called with "No data in range"
2. `histogramRenderer.spec.ts` — `SingleBucket_DrawsBar` — fillRect called at least once
3. `histogramRenderer.spec.ts` — `P99Line_DrawnAtCorrectX` — vertical stroke at x for log10(p99Ms)
4. `histogramRenderer.spec.ts` — `BudgetLine_DrawnWhenPresent` — additional stroke at absoluteMaxMs position
5. `histogramRenderer.spec.ts` — `BudgetLine_AbsentWhenBudgetNull` — no budget-coloured stroke when null
6. `LatencyDistributionChart_ResizeTriggers_Redraw` — ResizeObserver triggers second renderHistogram call

---

### TRC-P9-013 — `LatencyTimeSeriesChart.vue`

**Files:**
- `tracer-viewer/src/rendering/latencyTimeSeriesRenderer.ts`
- `tracer-viewer/src/components/LatencyTimeSeriesChart.vue`

**`latencyTimeSeriesRenderer.ts` exports:**
```typescript
export interface TimeSeriesRenderInput {
  timeseries: LatencyTimeSeriesDto;
  canvasWidth: number;
  canvasHeight: number;
}
export function renderTimeSeries(ctx: CanvasRenderingContext2D, input: TimeSeriesRenderInput): void
export function hitTestTimeSeries(points: LatencyTimeSeriesPointDto[], mouseX: number, canvasWidthPx: number): number // returns index
```

**Rendering:**
- Two lines: p50 (dim, dashed, thinner) and p99 (bright, solid, thicker)
- Y-axis: 0 to `max(p99Ms) * 1.1` (minimum 1ms)
- "No data" when `points` empty
- X-axis: time-based

**`LatencyTimeSeriesChart.vue`:**
- Props: `timeseries: LatencyTimeSeriesDto | null`, `loading: boolean`
- Hover interaction: tooltip overlay with bucket start, p50, p99, sample count

**Required tests (unit):**
1. `latencyTimeSeriesRenderer.spec.ts` — `EmptyPoints_DrawsNoDataMessage`
2. `latencyTimeSeriesRenderer.spec.ts` — `TwoLines_P99ThickerThanP50` — lineWidth before p99 stroke > lineWidth before p50 stroke
3. `latencyTimeSeriesRenderer.spec.ts` — `YAxis_UpperBoundCoversMaxP99` — max p99=80; y-axis upper bound ≥ 80
4. `LatencyTimeSeriesChart_HoverShowsTooltip` — mouse-move to x-position of point 3; tooltip visible with p99Ms value
5. `LatencyTimeSeriesChart_LoadingState_ShowsIndicator` — loading=true; loading indicator visible

---

### TRC-P9-014 — `LatencyOutliersTable.vue`

**File:** `tracer-viewer/src/components/LatencyOutliersTable.vue`

**Columns:** timestamp, topic, `publisherNode → subscriberNode`, latencyMs (2dp), thresholdMs (2dp), budgetSource

**"Timeline →" button:**
- Navigates to `{ name: 'timeline', params: { sessionId }, query: { from: (T-1s).toISOString(), to: (T+1s).toISOString(), topic, node: subscriberNode } }`
- T = `publishWallclockUtc`

**Required tests (unit):**
1. `LatencyOutliersTable_RendersAllRows` — 3 items → 3 `<tr>` in `<tbody>`
2. `LatencyOutliersTable_EmptyState_ShowsMessage` — outliers=[] → "No outliers detected" text, no rows
3. `LatencyOutliersTable_ShowInTimeline_NavigatesCorrectly` — sessionId="s1", T, topic="T1", subscriberNode="node-B"; assert router.push called with correct params
4. `LatencyOutliersTable_BudgetSource_Displayed` — "budget" and "top-0.1%" displayed in respective cells

---

### TRC-P9-015 — `PublisherSubscriberMatrix.vue`

**File:** `tracer-viewer/src/components/PublisherSubscriberMatrix.vue`

**Structure:** scrollable list of `LatencyPairSummaryDto[]`
- Per row: topic (monospace), `publisherNode → subscriberNode`, p99 (1dp ms), sample count
- CSS class `pair-matrix__row--over-budget` when `pair.p99Ms > budgetByTopic[pair.topic].p99BudgetMs`
- CSS class `pair-matrix__row--selected` when row matches `selectedPair` prop
- Emits `select` event with clicked `LatencyPairSummaryDto`
- Section heading: "Worst legs (by p99)"
- Style: `max-height: 70vh; overflow-y: auto`

**Props:**
- `pairs: LatencyPairSummaryDto[]`
- `budgets: LatencyBudgetDto[]` (used to build `budgetByTopic` lookup)
- `selectedPair: LatencyPairSummaryDto | null`

**Required tests (unit):**
1. `PublisherSubscriberMatrix_RendersAllPairs` — 5 pairs → 5 `li.pair-matrix__row`
2. `PublisherSubscriberMatrix_OverBudget_AppliesClass` — pair p99=100, budget p99BudgetMs=50 → `pair-matrix__row--over-budget`
3. `PublisherSubscriberMatrix_NoBudget_NoOverBudgetClass` — no budget entry → no `pair-matrix__row--over-budget`
4. `PublisherSubscriberMatrix_ClickRow_EmitsSelect` — click row 2; assert select emitted with row 2 object
5. `PublisherSubscriberMatrix_SelectedPair_AppliesSelectedClass` — selectedPair = row 3 → row 3 has `pair-matrix__row--selected`; others do not

---

### TRC-P9-016 — `GapDetectionView.vue` and `GapList.vue`

**Files:**
- `tracer-viewer/src/views/GapDetectionView.vue`
- `tracer-viewer/src/components/GapList.vue`

**`GapDetectionView.vue`:**
- Route `/v/gaps/:sessionId` (name `gap-detection`)
- On mount: load session; fetch gaps via `getGaps({ from, to })`
- Tuple summary panel: group gaps by `(topic, publisherNode, subscriberNode)`; sort by sum of missingCount DESC
- Gap list panel: `<GapList :gaps="gapResult.gaps" :sessionId="sessionId" />`
- `BundleModeRequiredBanner` on 409

**`GapList.vue`:**
- Columns: `resumedAtWallclockUtc` (formatted), topic, `publisherNode → subscriberNode`, previousSequence, last missing seq (`resumedAtSequence - 1`), missingCount, "Timeline →"
- "Timeline →" pivot: `{ name: 'timeline', ..., query: { from: (T-5s).toISOString(), to: (T+1s).toISOString(), topic, node: subscriberNode } }`
- "No gaps detected" empty state

**Required tests (unit):**
1. `GapDetectionView_409_ShowsBanner`
2. `GapDetectionView_TupleSummary_SortedByMissingCount` — tuple B (missing=25) before tuple A (missing=10)
3. `GapList_RendersGaps` — 3 gaps → 3 `<tr>` in `<tbody>`
4. `GapList_EmptyState_ShowsMessage` — gaps=[] → "No gaps detected"
5. `GapList_ShowInTimeline_NavigatesCorrectly` — T, topic="T1", subscriberNode="node-C"; assert router.push with `query.from=(T-5s)`, `query.to=(T+1s)`

**Required E2E tests** (in `tracer-viewer/tests/e2e/gap-detection.spec.ts`; skipped unless `E2E=true`):
1. "gap detection view loads" — navigate to `/v/gaps/{sessionId}`; assert `h1` contains "Gap detection"; no JS errors

---

### TRC-P9-017 — `NetworkTopologyView.vue` and `NetworkGraphCanvas.vue`

**Files:**
- `tracer-viewer/src/rendering/networkGraphLayout.ts`
- `tracer-viewer/src/rendering/networkGraphRenderer.ts`
- `tracer-viewer/src/components/NetworkGraphCanvas.vue`
- `tracer-viewer/src/views/NetworkTopologyView.vue`

**`networkGraphLayout.ts` exports:**
```typescript
export interface GraphLayoutInput {
  nodes: string[];
  edges: { from: string; to: string; weight: number }[];
  canvasWidth: number;
  canvasHeight: number;
}
export interface NodePosition { x: number; y: number }
export interface LaidOutGraph {
  nodes: Map<string, NodePosition>;
}
export function layoutGraph(input: GraphLayoutInput): LaidOutGraph
```

**Layout algorithm (Fruchterman-Reingold-ish):**
- Initial positions: nodes placed on a circle (deterministic by index order)
- 200 iterations; repulsive forces between all node pairs; attractive forces on edges scaled by `log10(weight + 1)`
- Temperature decay: start T=0.1*(canvasWidth+canvasHeight)/2; cool by `T *= 0.95` each iteration
- Clamp positions within canvas bounds (40px margin)
- MUST be deterministic: same input → same output

**`networkGraphRenderer.ts` exports:**
```typescript
export interface GraphRenderInput {
  layout: LaidOutGraph;
  nodes: string[];
  edges: { from: string; to: string; weight: number }[];
  selectedEdge: { from: string; to: string } | null;
  hoveredNode: string | null;
}
export function renderGraph(ctx: CanvasRenderingContext2D, input: GraphRenderInput): void
```

**Rendering:**
- Bezier edges with arrowheads; `lineWidth = clamp(log10(weight + 1) * 1.5, 1, 8)`
- Selected edge: `#5b9dff`
- Node circles: radius 14px normal, 18px hovered; label below

**`NetworkGraphCanvas.vue`:**
- Props: `nodes: string[]`, `edges: { from: string; to: string; weight: number }[]`, `selectedEdge: { from: string; to: string } | null`
- Runs `layoutGraph` on mount and when nodes/edges change
- Canvas click → proximity hit-test → emits `select-edge({ from, to })`
- Uses `ResizeObserver` to redraw on size change

**`NetworkTopologyView.vue`:**
- Route `/v/topology/:sessionId` (name `network-topology`)
- Bundles edges by `(publisherNode, subscriberNode)` pair, summing `messageCount` across topics
- `selectedEdge` ref → side panel with per-topic breakdown
- "Latency →" per-topic row: navigates to `{ name: 'replication-latency', params: { sessionId }, query: { publisherNode, subscriberNode, topic } }`
- `BundleModeRequiredBanner` on 409

**Required tests (unit):**
1. `networkGraphLayout.spec.ts` — `EmptyGraph_ReturnsEmptyNodes` — no error; `result.nodes.size === 0`
2. `networkGraphLayout.spec.ts` — `SingleNode_PositionedNearCanvasCenter` — within 80px of (200, 200)
3. `networkGraphLayout.spec.ts` — `ConnectedNodes_CloserThanDisconnected` — A-B edge weight 100; distance(A,B) < distance(A,C)
4. `networkGraphLayout.spec.ts` — `Layout_IsDeterministic` — two runs with same input → identical positions
5. `NetworkGraphCanvas_RendersCanvas` — 3 nodes, 2 edges → `<canvas>` present with non-zero dimensions
6. `NetworkTopologyView_DrillIntoEdge_NavigatesCorrectly` — select edge; click "Latency →"; assert router.push with `name: 'replication-latency'`
7. `NetworkTopologyView_409_ShowsBanner`

**Required E2E tests** (in `tracer-viewer/tests/e2e/network-topology-view.spec.ts`; skipped unless `E2E=true`):
1. "topology view renders canvas" — navigate to `/v/topology/{sessionId}`; assert `<canvas>` visible; no JS errors

---

### TRC-P9-018 — Composables

**Files:**
- `tracer-viewer/src/composables/useLatencyDistribution.ts`
- `tracer-viewer/src/composables/useLatencyTimeSeries.ts`
- `tracer-viewer/src/composables/useLatencyOutliers.ts`
- `tracer-viewer/src/composables/useGapDetection.ts`
- `tracer-viewer/src/composables/useTopology.ts` (check if this already exists first!)

**Pattern:** (see Architecture Notes §3 above for the template)

**`LatencyFilter` interface** (shared, define in `useLatencyDistribution.ts` and re-export or define in types):
```typescript
export interface LatencyFilter {
  from: string | null;
  to: string | null;
  topic?: string;
  publisherNode?: string;
  subscriberNode?: string;
}
```

**Required tests (unit):**
1. `useLatencyDistribution.spec.ts` — `FilterChange_RefetchesCalled`
2. `useLatencyDistribution.spec.ts` — `FilterChange_AbortsPreviousRequest` — first request AbortSignal.aborted === true
3. `useLatencyDistribution.spec.ts` — `On409_ErrorStatusSet_DataNull`
4. `useLatencyDistribution.spec.ts` — `OnUnmount_RequestAborted`
5. `useGapDetection.spec.ts` — `Loading_TrueWhileFetching_FalseAfter`
6. `useTopology.spec.ts` — `NoCallWhenFromIsNull`

---

### TRC-P9-019 — Integration of Remaining Tests

The backend unit tests specified in TRC-P9-019 were largely implemented in BATCH-49 (220 tests pass). The remaining work for this batch is:

**Frontend E2E placeholders** (already listed per-view above, but also ensure the `.spec.ts` files exist):
- `tracer-viewer/tests/e2e/replication-latency-view.spec.ts`
- `tracer-viewer/tests/e2e/gap-detection.spec.ts`
- `tracer-viewer/tests/e2e/network-topology-view.spec.ts`

All E2E tests use `test.skip(process.env['E2E'] !== 'true', ...)` pattern (same as Phase 8 E2E stubs).

**Backend integration tests** (`tests/Tracer.Tests.Integration/`):
- `LatencyAnalysisRoundTripTests.cs` — healthy network fixture (p99 < 5ms); degraded network fixture (at least one pair p99 > 15ms)
- `GapDetectionIntegrationTests.cs` — lossy network fixture → gaps present
- `TopologyIntegrationTests.cs` — multi-node fixture → node + edge counts match

For integration tests, use the `ObserverFixture` pattern with `configureExtraServices: s => s.AddSingleton<IBundleModeMarker, TestBundleSentinel>()`.

`FakeNetworkModel` is already implemented in `src/Tracer.Adapters.Mock/FakeNetworkModel.cs` (BATCH-49). Use it to push events with realistic per-subscriber latency variations.

---

## Build and Test Commands

```powershell
# Kill stale testhost
Get-Process -Name "testhost" -ErrorAction SilentlyContinue | Stop-Process -Force

# Build backend
cd d:\Work\Tracer; dotnet build Tracer.sln -c Release --no-incremental 2>&1 | Select-Object -Last 5

# Run Phase 9 backend unit tests
dotnet test tests\Tracer.Tests.Unit -c Release --no-build --filter "FullyQualifiedName~Latency OR FullyQualifiedName~GapDetection OR FullyQualifiedName~NetworkTopology OR FullyQualifiedName~BudgetService OR FullyQualifiedName~QuantileSink OR FullyQualifiedName~HistogramSink" 2>&1 | Select-Object -Last 10

# Run Phase 9 integration tests
dotnet test tests\Tracer.Tests.Integration -c Release --no-build --filter "FullyQualifiedName~LatencyAnalysis OR FullyQualifiedName~GapDetection OR FullyQualifiedName~TopologyIntegration" 2>&1 | Select-Object -Last 10

# Frontend unit tests
cd d:\Work\Tracer\tracer-viewer; pnpm test:unit -- --reporter=verbose 2>&1 | Select-Object -Last 20
```

---

## Notes

1. **Check for existing `useTopology.ts`** before creating it — a `TopologyQueryService` and `TopologyEndpoints` existed in Phase 3; there may already be a `useTopology` composable. If it exists, name the new composable `useNetworkTopology` instead.

2. **`FakeNetworkModel`** — for integration tests, read the existing implementation to understand the constructor parameters before calling it.

3. **Router registration** — add three new routes to `tracer-viewer/src/router/index.ts`.

4. **BundleModeRequiredBanner** — create once; reuse across all three views.

5. **Test file locations:**
   - Unit tests: `tracer-viewer/tests/unit/` (check existing structure in `tracer-viewer/tests/unit/components/` etc.)
   - E2E tests: `tracer-viewer/tests/e2e/`
