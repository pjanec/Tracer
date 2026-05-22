# BATCH-38 Instructions — Phase 7: Rendering Components (Lifecycle Ribbon, Event Strip, Slow State Chart)

**Target:** Coder Sub-agent  
**Batch:** BATCH-38  
**Tasks:** TRC-P7-011, TRC-P7-012, TRC-P7-013  
**Design reference:** `docs/tracer_phase7_design.md`, `docs/TASK-DETAIL.md`  
**Report path:** `.dev/tracer/reports/BATCH-38-REPORT.md`

---

## 1. Onboarding

**Read before starting (in order):**
1. `.dev/tracer/reviews/BATCH-37-REVIEW.md` — understand BATCH-37 output
2. `docs/tracer_phase7_design.md` §7 (Lifecycle Ribbon), §8 (Slow State Chart), §9 (Event Strip)
3. `docs/TASK-DETAIL.md` sections: TRC-P7-011, TRC-P7-012, TRC-P7-013
4. `tracer-viewer/src/rendering/timelineRenderer.ts` — canvas rendering reference
5. `tracer-viewer/src/composables/useResizeObserver.ts` — responsive canvas pattern
6. `tracer-viewer/src/composables/useCanvasRenderer.ts` — canvas lifecycle reference
7. `tracer-viewer/tests/unit/timelineRenderer.spec.ts` — canvas test patterns (mock ctx)
8. `tracer-viewer/src/api/tracerApiClient.ts` — entity DTOs already defined
9. `tracer-viewer/src/views/EntityHistoryView.vue` — how components are used (prop shapes)

**BATCH-37 established:**
- `tracer-viewer/src/components/EntityLifecycleRibbon.vue` — stub, receives `events: EntityEventsDto` + `timeRange`
- `tracer-viewer/src/components/EntityEventStrip.vue` — stub, receives `events`, `timeRange`, `selectedEventId`; emits `select(eventId | null)`
- `tracer-viewer/src/components/SlowStateChart.vue` — stub, receives `topic`, `samples: SlowStateSampleDto[]`, `timeRange`; emits `select-event(SlowStateSampleDto)`
- `tracer-viewer/src/stores/entityHistoryStore.ts` — `slowStateByTopic: Record<string, SlowStateSampleDto[]>`
- `tracer-viewer/src/api/tracerApiClient.ts` — all entity DTOs including `EntityEventsDto`, `EntityEventDto`, `SlowStateSampleDto`

**Do NOT modify:** `FastStateDrillDown.vue`, `EntitySummaryStrip.vue`, `entityHistoryStore.ts`, `useEntityHistoryQuery.ts`, `useEntityHistoryUrl.ts`, `EntityHistoryView.vue` (unless adding a CSS class is necessary)

---

## 2. Key Type Definitions (from `tracerApiClient.ts`)

```typescript
interface EntityEventDto {
  eventId: string;
  publishWallclock: string;  // ISO 8601
  topic: string;
  publisherNode: string;
  traceId: string | null;   // numeric as string; null or "0" = no trace
  payload: string;          // JSON string
  entityId: string;
}

interface EntityEventsDto {
  entityId: string;
  events: EntityEventDto[];
  truncated: boolean;
}

interface SlowStateSampleDto {
  topic: string;
  publishWallclock: string;  // ISO 8601
  payloadJson: string;       // JSON string
  traceId: string | null;
}
```

---

## 3. Task 1 — TRC-P7-011: `EntityLifecycleRibbon.vue` + `lifecycleClassifier.ts`

### 3.1 File: `tracer-viewer/src/utils/lifecycleClassifier.ts`

Create this utility. It is a pure module with no Vue dependencies.

**Types:**
```typescript
export type LifecycleKind = 'spawn' | 'ownership' | 'destruction';
```

**Function:**
```typescript
export function classifyLifecycleEvent(topic: string): LifecycleKind | null
```

**Classification rules** — examine the last dotted segment of the topic (e.g. `topic.split('.').pop()?.toLowerCase()`):

| `LifecycleKind` | Suffix matches (case-insensitive) |
|---|---|
| `'spawn'` | `'spawned'`, `'spawn'`, `'created'`, `'create'`, `'born'`, `'birth'`, `'instantiated'` |
| `'ownership'` | `'ownership_changed'`, `'owner_changed'`, `'owner_transferred'`, `'ownership_transferred'` |
| `'destruction'` | `'destroyed'`, `'killed'`, `'despawned'`, `'removed'`, `'deleted'`, `'died'`, `'death'` |
| `null` | Anything else |

### 3.2 File: `tracer-viewer/src/components/EntityLifecycleRibbon.vue`

Replace the stub with a full implementation. **No canvas** — use CSS `position: absolute; left: X%`.

**Props (keep existing):**
```typescript
defineProps<{
  events: EntityEventsDto;
  timeRange: { from: Date; to: Date };
}>();
```

**Algorithm:**
1. Filter `events.events` to those where `classifyLifecycleEvent(event.topic) !== null`
2. Compute `xPct(event)`: `clamp((t - from) / (to - from) * 100, 0, 100)` where `t = new Date(event.publishWallclock).getTime()`, `from = timeRange.from.getTime()`, etc.
3. Render ownership bands between consecutive ownership/spawn events:
   - Sort lifecycle events by `publishWallclock` ascending
   - Walk the sorted list; accumulate ownership bands between each spawn/ownership event and the next transition (or the right edge if no next)
   - A band runs from `xPct(eventA)` to `xPct(eventB)` (or 100% if end)
   - Colour: use a different background colour per band (accent blue `#4a9eff` for first, then cycle through a small palette)
4. Render marker divs on top of bands — one per lifecycle event

**CSS classes:**
- Outer: `entity-lifecycle-ribbon` (relative position, `height: 28px; overflow: hidden`)
- Track background: `entity-lifecycle-ribbon__track` (absolute, full-width, low-opacity)
- Ownership band: `entity-lifecycle-ribbon__ownership-band` (absolutely positioned, `height: 100%; background-color: <band color>; opacity: 0.4`)
- Marker: `entity-lifecycle-ribbon__marker` — plus kind-specific modifier: `entity-lifecycle-ribbon__marker--spawn`, `entity-lifecycle-ribbon__marker--ownership`, `entity-lifecycle-ribbon__marker--destruction`
- Marker colours: spawn `#22c55e` (green), ownership `#4a9eff` (blue), destruction `#ef4444` (red)
- Marker style: `position: absolute; width: 2px; height: 100%; cursor: pointer`
- Add a `title` attribute on each marker: `"${kind} @ ${new Date(event.publishWallclock).toISOString()}"`

**Empty state:** if no lifecycle events, render only the track background (no error, no message).

**No `emits` needed** for this task. Selecting a lifecycle event is Phase 10+.

### 3.3 Tests: `tracer-viewer/tests/unit/lifecycleClassifier.spec.ts`

Write tests satisfying all 4 success conditions from TASK-DETAIL TRC-P7-011 §SC-1..4:
1. Spawn suffixes — 3 assertions
2. Ownership suffixes — 2 assertions  
3. Destruction suffixes — 3 assertions
4. Unrelated returns null — 2 assertions

### 3.4 Tests: `tracer-viewer/tests/unit/entityLifecycleRibbon.spec.ts`

Write tests satisfying success conditions SC-5..8:
5. Correct number of markers by kind
6. Marker horizontal position matches time (spawn at 50% → `style.left === "50%"`)
7. No markers when no lifecycle events
8. Two ownership bands when entity has spawn then ownership_changed

Use `@vue/test-utils` `mount`, `@pinia/testing` `createPinia`/`setActivePinia`. Build `EntityEventsDto` helpers inline.

---

## 4. Task 2 — TRC-P7-012: `EntityEventStrip.vue` + `eventStripRenderer.ts`

### 4.1 File: `tracer-viewer/src/rendering/eventStripRenderer.ts`

Pure canvas rendering module (no Vue). File location: `tracer-viewer/src/rendering/eventStripRenderer.ts`.

**Types:**
```typescript
import type { EntityEventDto } from '@/api/tracerApiClient';

export interface EventStripRenderInput {
  width: number;
  height: number;
  fromMs: number;
  toMs: number;
  events: EntityEventDto[];
  selectedEventId: string | null;
  markerRadiusPx?: number;  // default 4
}

export interface EventStripHitEntry {
  eventId: string;
  x: number;
}
```

**Function:**
```typescript
export function renderEventStrip(
  ctx: CanvasRenderingContext2D,
  input: EventStripRenderInput,
): EventStripHitEntry[]
```

**Algorithm:**
1. `ctx.clearRect(0, 0, width, height)`
2. If `events.length === 0`: return `[]`
3. Build `nodeColorMap` via `buildNodeColorMap` (from `colorScheme.ts`) — pass unique publisher nodes from events
4. For each event:
   - `t = new Date(event.publishWallclock).getTime()`
   - `x = (t - fromMs) / (toMs - fromMs) * width` — skip if `x < 0 || x > width`
   - `y = height / 2`
   - Fill circle: `ctx.beginPath(); ctx.arc(x, y, r, 0, 2*Math.PI); ctx.fillStyle = nodeColorMap.get(event.publisherNode) ?? '#888'; ctx.fill()`
   - If this event is selected: draw a ring — `ctx.beginPath(); ctx.arc(x, y, r + 3, 0, 2*Math.PI); ctx.strokeStyle = '#ffffff'; ctx.lineWidth = 2; ctx.stroke()`
5. Return an array of `{ eventId, x }` for all rendered markers (for hit-testing)

### 4.2 File: `tracer-viewer/src/components/EntityEventStrip.vue`

Replace the stub with a full implementation.

**Props (keep existing):**
```typescript
defineProps<{
  events: EntityEventsDto;
  timeRange: { from: Date; to: Date };
  selectedEventId: string | null;
}>();
```

**Emits (keep existing):**
```typescript
defineEmits<{
  select: [eventId: string | null];
}>();
```

**Implementation:**
- `canvasRef = ref<HTMLCanvasElement | null>(null)`
- `hitEntries = ref<EventStripHitEntry[]>([])`
- `THRESHOLD_PX = 8` (click threshold)

**Render function** (call this on any relevant change):
```
function scheduleRender() {
  // requestAnimationFrame to avoid double-render on prop updates
  // DPI-correct canvas sizing (devicePixelRatio)
  // Call renderEventStrip(ctx, { width, height, fromMs, toMs, events, selectedEventId })
  // Save returned hit entries
}
```

**Watch:** `watchEffect` that reads `props.events`, `props.timeRange`, `props.selectedEventId` → calls `scheduleRender()`

**ResizeObserver:** Use `useResizeObserver(canvasRef, () => scheduleRender())`

**Click handler:**
```
function onClick(e: MouseEvent) {
  const rect = canvasRef.value!.getBoundingClientRect();
  const x = e.clientX - rect.left;
  // Find nearest entry: entries.sort by |entry.x - x|; if min distance < THRESHOLD_PX → emit select(eventId)
  // else emit select(null)
}
```

**Template:**
```html
<div class="entity-event-strip">
  <div class="entity-event-strip__header">
    <span>Events</span>
    <span v-if="events.truncated" class="entity-event-strip__truncated">
      (truncated — showing first {{ events.events.length }} events)
    </span>
  </div>
  <canvas ref="canvasRef" class="entity-event-strip__canvas" @click="onClick" />
</div>
```

**CSS:** `entity-event-strip__canvas` → `width: 100%; height: 40px; display: block; cursor: crosshair`

### 4.3 Tests: `tracer-viewer/tests/unit/eventStripRenderer.spec.ts`

Use a mock context (record calls via `vi.fn()`). Satisfy success conditions SC-1..3:
1. Marker at correct x position: single event at 250ms, canvas 1000px wide → `arc` called with `x ≈ 250`
2. Selected event has ring: two events, one selected → `stroke` called (ring drawn)
3. Zero events → no exception, `clearRect` called

### 4.4 Tests: `tracer-viewer/tests/unit/entityEventStrip.spec.ts`

Use `@vue/test-utils` mount with canvas mock. Satisfy SC-4..7:
4. Click near marker emits `select` with event ID — set canvas `getBoundingClientRect` via mock; simulate click
5. Click far from any marker emits `select(null)`
6. `truncated === true` → header shows "truncated"
7. `truncated === false` → "truncated" text absent

**Canvas click testing tip:** `Object.defineProperty(canvasEl, 'getBoundingClientRect', { value: () => ({ left: 0, top: 0, width: 1000, height: 40 }) })`. Use `wrapper.find('canvas').trigger('click', { clientX: 500 })`.

---

## 5. Task 3 — TRC-P7-013: `SlowStateChart.vue` + `slowStateChartRenderer.ts`

### 5.1 File: `tracer-viewer/src/rendering/slowStateChartRenderer.ts`

Pure canvas rendering module. Location: `tracer-viewer/src/rendering/slowStateChartRenderer.ts`.

**Types:**
```typescript
export type FieldKind = 'numeric' | 'categorical';

export interface DetectedField {
  name: string;
  kind: FieldKind;
}

export interface SlowStateSample {
  t: number;         // milliseconds since epoch
  value: unknown;    // the value for the selected field
}

export interface SlowStateRenderInput {
  ctx: CanvasRenderingContext2D;
  width: number;
  height: number;
  fromMs: number;
  toMs: number;
  samples: SlowStateSample[];
  kind: FieldKind;
}
```

**Functions to export:**

```typescript
export function detectFields(payloadJsonSamples: string[], maxSamples?: number): DetectedField[]
```
- Parse up to `maxSamples ?? 20` samples from `payloadJsonSamples`
- For each field in the parsed JSON objects: classify as `'numeric'` if all observed values are `typeof === 'number'`; otherwise `'categorical'`
- Sort fields: numeric preferred-names first (`['value', 'level', 'health', 'count', 'speed', 'amount']`), then other numeric, then categorical preferred-names (`['state', 'status', 'phase', 'kind', 'mode']`), then other categorical
- Return `DetectedField[]` in that order

```typescript
export function renderNumericLine(input: SlowStateRenderInput): void
```
- **Stepped line** (last-value-held): for each sample, draw a horizontal line from current x to next sample's x at current y, then a vertical transition
- Compute `minVal` / `maxVal` from all sample values (numeric); if `maxVal === minVal`, use range of `1e-9` centred at the value
- Y coordinate: `y = height - ((v - minVal) / (maxVal - minVal)) * height` — clamp to `[1, height-1]`
- Extend the last sample's horizontal segment to the right edge (`x = width`)
- Use a fixed `strokeStyle` of `#4a9eff` (accent blue)

```typescript
export function renderCategoricalBands(input: SlowStateRenderInput): void
```
- Ordered palette: `['#4a9eff','#22c55e','#ef4444','#f59e0b','#8b5cf6','#ec4899','#14b8a6','#f97316','#6366f1','#84cc16','#0ea5e9','#d946ef','#a855f7','#10b981','#f43f5e']` — if more than 15 distinct values, use `#888888` (grey) for extras with label "other"
- For each sample, draw `fillRect` from `xStart` to `xEnd` (or right edge), using the colour for that value
- If band is wide enough (> 30px), draw text label centred in the band: `ctx.fillStyle = '#fff'; ctx.font = '10px sans-serif'; ctx.fillText(label, centreX, centreY)`

### 5.2 File: `tracer-viewer/src/components/SlowStateChart.vue`

Replace stub with full implementation.

**Props (keep existing):**
```typescript
defineProps<{
  topic: string;
  samples: SlowStateSampleDto[];
  timeRange: { from: Date; to: Date };
}>();
```

**Emits:**
```typescript
defineEmits<{
  'select-event': [sample: SlowStateSampleDto];
}>();
```

**Local state:**
```typescript
const selectedField = ref<string | null>(null);
const detectedFields = computed(() => detectFields(props.samples.map(s => s.payloadJson)));
const fieldOptions = computed(() => detectedFields.value);
```

**Auto-select on topic/samples change:**
```typescript
watch(() => props.samples, () => {
  selectedField.value = detectedFields.value[0]?.name ?? null;
}, { immediate: true });
```

**Canvas rendering:**
- `canvasRef = ref<HTMLCanvasElement | null>(null)`
- `scheduleRender()`: DPI-correct canvas sizing; parse samples to `SlowStateSample[]` (parse `payloadJson`, extract `selectedField` value); call `renderNumericLine` or `renderCategoricalBands` based on detected kind
- `watchEffect` on `props.samples`, `props.timeRange`, `selectedField`, `canvasRef` → `scheduleRender()`
- `useResizeObserver(canvasRef, () => scheduleRender())`

**Click handler:**
- Find the closest sample by `t` to the click x-position; emit `select-event` if within 10px

**Template:**
```html
<div class="slow-state-chart">
  <div class="slow-state-chart__header">
    <span class="slow-state-chart__topic">{{ topic }}</span>
    <select
      v-if="detectedFields.length > 1"
      v-model="selectedField"
      class="slow-state-chart__field-select"
    >
      <option v-for="f in detectedFields" :key="f.name" :value="f.name">{{ f.name }}</option>
    </select>
  </div>
  <canvas ref="canvasRef" class="slow-state-chart__canvas" @click="onCanvasClick" />
</div>
```

**CSS:** `slow-state-chart__canvas` → `width: 100%; height: 60px; display: block; cursor: crosshair`

### 5.3 Tests: `tracer-viewer/tests/unit/slowStateChartRenderer.spec.ts`

Use mock canvas context. Satisfy SC-1..5:
1. `renderNumericLine` — 3 samples → path commands include correct y-coordinates
2. `renderNumericLine` — one sample → path extends to right edge (`x = 300` for 300px canvas)
3. `renderNumericLine` — all-same values → no exception; all drawn at same y
4. `renderCategoricalBands` — 2 samples → first band width ≈ 500px (1000px canvas, 0–1000ms range, samples at t=0, t=500ms)
5. `renderCategoricalBands` — 0 samples → no exception

### 5.4 Tests: `tracer-viewer/tests/unit/slowStateChart.spec.ts`

Use `@vue/test-utils`. Satisfy SC-6..9:
6. `detectFields` — payload with `{ "health": 100, "state": "idle" }` → returns fields with correct kinds
7. Preferred field is auto-selected (`value` beats `x`)
8. Click emits `selectEvent` with correct sample
9. Smoke: `store.slowStateByTopic = {}` → mounting `EntityHistoryView` renders zero `SlowStateChart` instances

**Note for test SC-9:** Mount `EntityHistoryView` with the real Pinia store (not stubs); mock all sub-components via `global.stubs` except `SlowStateChart`.

---

## 6. Constraints (apply to all tasks)

- **TypeScript strict mode** — no `any` types; `unknown` + type guards where necessary
- **TreatWarningsAsErrors=false for frontend** but `pnpm tsc --noEmit` must pass (0 errors)
- **Canvas DPI scaling**: always use `devicePixelRatio` when setting canvas dimensions
- **useResizeObserver** is already in `src/composables/useResizeObserver.ts` — do not re-implement
- **buildNodeColorMap / getNodeColor** are in `src/rendering/colorScheme.ts` — import from there
- **No external npm packages** — do not add new dependencies
- **Do NOT modify** `EntityHistoryView.vue` template structure (component props already match)

---

## 7. Build and Test Commands

```powershell
# Frontend TypeScript check
cd d:\Work\Tracer\tracer-viewer; pnpm tsc --noEmit

# Frontend tests
cd d:\Work\Tracer\tracer-viewer; pnpm test:unit --run

# Expected: 183 existing + ~35 new ≈ 218+ tests, all passing
```

No backend changes in this batch. Do not run the C# build.

---

## 8. Report

Write `.dev/tracer/reports/BATCH-38-REPORT.md` following the standard format:
- Files created / modified
- Test counts (existing + new; by spec file)
- TypeScript status
- Design decisions beyond spec
- Issues encountered
- Weak points
- Technical debt (IDs DT-032+)
- Suggested git commit message

**Do NOT commit.** The dev lead will review, then commit.
