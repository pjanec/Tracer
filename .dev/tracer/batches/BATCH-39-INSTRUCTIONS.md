# BATCH-39 Instructions — Phase 7: Fast State Drill-Down (TRC-P7-014, TRC-P7-017)

**Target:** Coder Sub-agent  
**Batch:** BATCH-39  
**Tasks:** TRC-P7-014, TRC-P7-017  
**Design reference:** `docs/tracer_phase7_design.md` §10, `docs/TASK-DETAIL.md` §TRC-P7-014, §TRC-P7-017  
**Report path:** `.dev/tracer/reports/BATCH-39-REPORT.md`

---

## 1. Onboarding

**Read before starting (in order):**
1. `docs/TASK-DETAIL.md` — sections TRC-P7-014 and TRC-P7-017 (full success conditions)
2. `docs/tracer_phase7_design.md` §10 (Fast State Drill-Down)
3. `.dev/tracer/reports/BATCH-38-REPORT.md` — BATCH-38 context (what was built)
4. `tracer-viewer/src/api/tracerApiClient.ts` — `getEntityFastStateSchema`, `getEntityFastState`, `EntityFastStateDto`, `FastStateTopicSchemaDto`, `FastStateColumnDto`, `FastStateSampleDto`
5. `tracer-viewer/src/composables/useEntityHistoryQuery.ts` — AbortController fetch pattern reference
6. `tracer-viewer/src/rendering/slowStateChartRenderer.ts` — canvas rendering pattern reference
7. `tracer-viewer/src/components/FastStateDrillDown.vue` — stub to replace
8. `tracer-viewer/tests/unit/slowStateChart.spec.ts` — Vue component test patterns

**Established by prior batches:**
- `tracer-viewer/src/stores/entityHistoryStore.ts` — `fastStateTopics: string[]` available
- `tracer-viewer/src/views/EntityHistoryView.vue` — passes `entityId`, `sessionId`, `availableTopics`, `timeRange` to `FastStateDrillDown`
- `tracer-viewer/src/composables/useEntityHistoryUrl.ts` — handles `entityId`, `sessionId`, `from`, `to`, `select` URL params (TRC-P7-016 done). You must extend this for `fastStateTopic` and `fastStateColumns` (see §4 below).

---

## 2. Key API Types (from `tracerApiClient.ts`)

```typescript
interface FastStateColumnDto {
  name: string;
  isNumeric: boolean;
}

interface FastStateTopicSchemaDto {
  entityId: string;
  topic: string;
  columns: FastStateColumnDto[];
}

interface FastStateSampleDto {
  ts: string;             // ISO 8601
  values: Record<string, number | null>;  // column name → value (null = missing)
}

interface EntityFastStateDto {
  entityId: string;
  topic: string;
  columns: string[];
  samples: FastStateSampleDto[];
  totalSamples: number;
  downsampled: boolean;
}
```

**API methods available (already in `api`):**
- `api.getEntityFastStateSchema(entityId, topic, sessionId, opts?)` → `FastStateTopicSchemaDto | null`
- `api.getEntityFastState(entityId, topic, sessionId, from, to, columns, opts?)` → `EntityFastStateDto`

---

## 3. Task 1 — TRC-P7-017: `useFastStateChart.ts`

### 3.1 File: `tracer-viewer/src/composables/useFastStateChart.ts`

**Purpose:** Manages all fast-state data fetching for a single entity's fast-state panel. Schema is fetched when topic changes; data is fetched when topic + columns + timeRange changes.

**Inputs (accepted as refs or computed):**
```typescript
function useFastStateChart(
  entityId: Ref<string | null>,
  sessionId: Ref<string | null>,
  selectedTopic: Ref<string | null>,
  selectedColumns: Ref<string[]>,
  timeRange: Ref<{ from: Date; to: Date }>,
): {
  schema: Ref<FastStateTopicSchemaDto | null>;
  data: Ref<EntityFastStateDto | null>;
  schemaLoading: Ref<boolean>;
  dataLoading: Ref<boolean>;
  error: Ref<string | null>;
}
```

**Behaviour:**
1. **Topic change** → cancel any in-flight schema/data fetches; reset `schema`, `data`, `selectedColumns` to empty; fetch new schema; on schema success, auto-select first numeric column if `selectedColumns` is empty
2. **Columns change (topic unchanged)** → cancel any in-flight data fetch; fetch data with current (topic + columns + timeRange)
3. **TimeRange change** → cancel any in-flight data fetch; refetch data with same topic + columns
4. **If `selectedTopic` is null or `availableTopics` is empty** → remain idle (no fetch)
5. **AbortController pattern**: use separate controllers for schema fetch and data fetch; cancel previous before starting new
6. **maxSamples**: hardcode at `5000` for Phase 7

**Auto-select first numeric column** after schema loads:
```typescript
if (selectedColumns.value.length === 0 && schema.value) {
  const firstNumeric = schema.value.columns.find(c => c.isNumeric);
  if (firstNumeric) selectedColumns.value = [firstNumeric.name];
}
```

**Error handling:** Non-abort errors set `error.value = err.message`. Schema load failure does not prevent using a prior schema; it sets `error` and leaves `schema` at its current value.

**Loading flags:** `schemaLoading` during schema fetch; `dataLoading` during data fetch. Both independent.

**Watchers:**
- Watch `selectedTopic` → schema re-fetch + clear data/columns
- Watch `[selectedColumns, timeRange]` (deep for timeRange) → data re-fetch only

### 3.2 Tests: `tracer-viewer/tests/unit/useFastStateChart.spec.ts`

Write tests satisfying all 7 success conditions from TASK-DETAIL TRC-P7-017:
1. Topic change triggers schema fetch (mock API; change `selectedTopic.value`)
2. Topic change clears previous data and columns (prior state populated → topic change → reset before new schema arrives)
3. Column change does NOT refetch schema (change `selectedColumns.value`, assert `getEntityFastStateSchema` not called again)
4. Data fetch triggered after schema resolves and columns auto-selected (`getEntityFastState` called with auto-selected column)
5. TimeRange change triggers data refetch
6. `dataLoading` true while fetch pending; false after resolution
7. URL round-trip for `fastStateTopic`/`fastStateColumns` — **Note:** this URL behaviour lives in `useEntityHistoryUrl.ts` (see §4). In this test, verify the composable initialises `selectedColumns` from external state correctly.

---

## 4. Task 2 — Extend `useEntityHistoryUrl.ts` for Fast-State URL Params

**File:** `tracer-viewer/src/composables/useEntityHistoryUrl.ts`

The existing composable handles `session`, `from`, `to`, `select` params. **Extend it** to also sync `fastStateTopic` and `fastStateColumns`:

**URL → composable output:**
- Read `route.query.fastStateTopic` → return as `Ref<string | null>` from the composable (or emit via a return value)
- Read `route.query.fastStateColumns` (comma-separated string like `"x,y"`) → parse as `string[]`

**Composable output signature change:**
```typescript
// extend the return value of useEntityHistoryUrl() to include:
{
  fastStateTopic: Ref<string | null>;
  fastStateColumns: Ref<string[]>;
}
```

**Store → URL (debounced 250ms):** watch `fastStateTopic` and `fastStateColumns` reactively; on change, call `router.replace` with updated `fastStateTopic` (omit if null) and `fastStateColumns` (comma-joined; omit if empty).

**No new store state:** `fastStateTopic` and `fastStateColumns` are LOCAL reactive refs in `useEntityHistoryUrl`, passed to `useFastStateChart`. They are NOT added to `entityHistoryStore` (FastStateDrillDown is local panel state).

**Existing tests update:** The existing `useEntityHistoryUrl.spec.ts` tests must continue to pass (do not break them). Add tests for the new fast-state params:
1. URL → composable: `?fastStateTopic=transforms&fastStateColumns=x,y` → `fastStateTopic.value === 'transforms'` and `fastStateColumns.value = ['x', 'y']`
2. Composable → URL: set `fastStateTopic.value = 'pos'` → after debounce, `router.replace` called with `fastStateTopic=pos`
3. Null topic → `fastStateTopic` omitted from URL
4. Empty columns → `fastStateColumns` omitted from URL

---

## 5. Task 3 — TRC-P7-014: `FastStateDrillDown.vue`, `FastStateColumnPicker.vue`, `FastStateChart.vue`, `fastStateChartRenderer.ts`

### 5.1 File: `tracer-viewer/src/rendering/fastStateChartRenderer.ts`

Pure canvas rendering module.

**Types:**
```typescript
export interface FastStateRenderInput {
  ctx: CanvasRenderingContext2D;
  width: number;
  height: number;
  fromMs: number;
  toMs: number;
  samples: Array<{ ts: string; values: Record<string, number | null> }>;
  columns: string[];   // which columns to render (in order)
  colors: string[];    // colors[i] maps to columns[i % colors.length]
}
```

**Function:**
```typescript
export function renderFastStateChart(input: FastStateRenderInput): void
```

**Algorithm:**
1. `ctx.clearRect(0, 0, width, height)`
2. If `samples.length === 0` or `columns.length === 0`: return
3. For each column (index `i`):
   - `color = colors[i % colors.length]`
   - `strokeStyle = color`
   - Walk samples in order; compute `x = (new Date(sample.ts).getTime() - fromMs) / (toMs - fromMs) * width`
   - `v = sample.values[column]` — if null, lift path (start new `moveTo` on next non-null)
   - Compute `minVal / maxVal` across ALL samples for this column (ignoring nulls); if all null, skip
   - Y-coordinate: `y = height - ((v - minVal) / (maxVal - minVal)) * (height - 4) - 2` (2px padding)
   - If `maxVal === minVal`: draw at `y = height / 2`
   - Connect non-null points with `lineTo`; lift pen on null values
4. Draw legend in top-left: small coloured rectangle (8×8 px) + column name for each column

**Colour palette (deterministic by index):**
```typescript
export const FAST_STATE_COLORS = [
  '#4a9eff', '#22c55e', '#ef4444', '#f59e0b', '#8b5cf6',
  '#ec4899', '#14b8a6', '#f97316', '#6366f1', '#84cc16',
];
```

### 5.2 File: `tracer-viewer/src/components/FastStateColumnPicker.vue`

**Props:**
```typescript
defineProps<{
  columns: FastStateColumnDto[];   // all columns from schema
  selected: string[];              // currently selected column names
}>();

defineEmits<{
  'update:selected': [columns: string[]];
}>();
```

**Behaviour:**
- Only renders columns where `column.isNumeric === true`
- If any non-numeric columns exist in schema, show hint: "(non-numeric columns hidden)"
- Each numeric column renders as a checkbox chip: checked if `selected.includes(column.name)`
- On click: toggle — if checked, emit `update:selected` with column removed; if unchecked, emit with column added

**Template class prefix:** `fast-state-column-picker__`

### 5.3 File: `tracer-viewer/src/components/FastStateChart.vue`

Canvas wrapper component. Receives data and renders via `renderFastStateChart`.

**Props:**
```typescript
defineProps<{
  data: EntityFastStateDto;
  selectedColumns: string[];
  timeRange: { from: Date; to: Date };
}>();
```

**Implementation:**
- `canvasRef = ref<HTMLCanvasElement | null>(null)`
- `scheduleRender()`: DPI-correct canvas sizing; call `renderFastStateChart({ ctx, width, height, fromMs, toMs, samples: data.samples, columns: selectedColumns, colors: FAST_STATE_COLORS })`
- `watchEffect` on `props.data`, `props.selectedColumns`, `props.timeRange` → `scheduleRender()`
- `useResizeObserver(canvasRef, () => scheduleRender())`
- **CSS:** canvas `width: 100%; height: 120px; display: block`

### 5.4 File: `tracer-viewer/src/components/FastStateDrillDown.vue`

Replace the stub with a full implementation.

**Props (keep existing):**
```typescript
defineProps<{
  entityId: string;
  sessionId: string;
  availableTopics: string[];
  timeRange: { from: Date; to: Date };
}>();
```

**Local state:**
```typescript
const expanded = ref(false);
const selectedTopic = ref<string | null>(null);
const selectedColumns = ref<string[]>([]);

// Initialize from URL (via useEntityHistoryUrl return values)
const { fastStateTopic, fastStateColumns } = useEntityHistoryUrl();
// Sync from URL on mount
watchEffect(() => {
  if (fastStateTopic.value && availableTopics.includes(fastStateTopic.value)) {
    selectedTopic.value = fastStateTopic.value;
  }
  if (fastStateColumns.value.length > 0) {
    selectedColumns.value = fastStateColumns.value;
  }
});

// useFastStateChart composable
const { schema, data, schemaLoading, dataLoading, error } = useFastStateChart(
  computed(() => props.entityId),
  computed(() => props.sessionId),
  selectedTopic,
  selectedColumns,
  computed(() => props.timeRange),
);
```

**Template:**
```html
<div class="fast-state-drill-down">
  <button class="fast-state-drill-down__toggle" @click="onToggle">
    <template v-if="availableTopics.length === 0">
      Fast State (no fast-state data)
    </template>
    <template v-else>
      Fast State {{ expanded ? '▲' : '▼' }}
    </template>
  </button>

  <div v-show="expanded && availableTopics.length > 0" class="fast-state-drill-down__body">
    <select v-model="selectedTopic" class="fast-state-drill-down__topic-select">
      <option value="">— select topic —</option>
      <option v-for="t in availableTopics" :key="t" :value="t">{{ t }}</option>
    </select>

    <div v-if="schemaLoading || dataLoading" class="fast-state-drill-down__loading">Loading…</div>
    <div v-else-if="error" class="fast-state-drill-down__error">{{ error }}</div>
    <template v-else-if="schema && selectedTopic">
      <FastStateColumnPicker
        :columns="schema.columns"
        :selected="selectedColumns"
        @update:selected="selectedColumns = $event"
      />
      <div v-if="data?.downsampled" class="fast-state-drill-down__downsampled-notice">
        Showing {{ data.samples.length.toLocaleString() }} of {{ data.totalSamples.toLocaleString() }} samples (downsampled)
      </div>
      <FastStateChart
        v-if="data && selectedColumns.length > 0"
        :data="data"
        :selected-columns="selectedColumns"
        :time-range="timeRange"
      />
    </template>
  </div>
</div>
```

**`onToggle()`:** `if (availableTopics.length > 0) expanded = !expanded`

### 5.5 Modify `EntityHistoryView.vue`

`useEntityHistoryUrl()` is already called in `EntityHistoryView.vue`. Since you're returning `fastStateTopic` and `fastStateColumns` from the composable, and `FastStateDrillDown` calls `useEntityHistoryUrl()` internally, **no change needed in `EntityHistoryView.vue`** — `FastStateDrillDown` calls the composable directly.

---

## 6. Tests

### 6.1 `tracer-viewer/tests/unit/fastStateChartRenderer.spec.ts`

Satisfy SC-10..12 from TRC-P7-014:
10. Two columns → `ctx.strokeStyle` set to two distinct colours
11. Null values → at least two `moveTo` calls (line lifted at null)
12. 0 samples → no exception

### 6.2 `tracer-viewer/tests/unit/fastStateColumnPicker.spec.ts`

Satisfy SC-7..9 from TRC-P7-014:
7. Only numeric columns rendered (non-numeric absent)
8. Toggle adds to selected → `update:selected` emitted with new array
9. Toggle removes from selected → `update:selected` emitted without it

### 6.3 `tracer-viewer/tests/unit/fastStateDrillDown.spec.ts`

Satisfy SC-1..6 from TRC-P7-014:
1. Collapsed by default
2. Toggle button expands body
3. No data hint when `availableTopics = []`
4. Expand with no topics → body remains hidden
5. Auto-selects first numeric column on topic selection (mock `api.getEntityFastStateSchema`)
6. Downsampled notice shown when `data.downsampled === true`

**Mock `@/api/tracerApiClient` at module level** (see existing test patterns in `useEntityHistoryQuery.spec.ts`).

### 6.4 Update `tracer-viewer/tests/unit/useEntityHistoryUrl.spec.ts`

Add 4 tests for fast-state URL params (items listed in §4 above). Do NOT break existing 7 tests.

---

## 7. Constraints

- **TypeScript strict** — no `any`; use `unknown` + guards
- **No new npm packages**
- **Do NOT modify** `entityHistoryStore.ts` — `fastStateTopic`/`fastStateColumns` are NOT store state
- **Do NOT modify** `EntityHistoryView.vue` template structure (FastStateDrillDown props unchanged)
- **Do NOT modify** `EntityLifecycleRibbon.vue`, `EntityEventStrip.vue`, `SlowStateChart.vue` (BATCH-38 output)
- `useFastStateChart` must be called from `FastStateDrillDown.vue` setup, not from the store

---

## 8. Build and Test Commands

```powershell
# TypeScript check
cd d:\Work\Tracer\tracer-viewer; pnpm tsc --noEmit

# Frontend tests  
cd d:\Work\Tracer\tracer-viewer; pnpm test:unit --run

# Expected: 207 existing + ~30 new ≈ 237+ tests, all passing
```

No backend changes. Do not run the C# build.

---

## 9. Report

Write `.dev/tracer/reports/BATCH-39-REPORT.md` (at workspace root `d:\WORK\Tracer\.dev\tracer\reports\`, NOT inside `tracer-viewer\`) following the standard format:
- Files created / modified
- Test counts per spec file
- TypeScript status (0 errors required)
- Design decisions beyond spec
- Issues encountered
- Weak points
- Technical debt (IDs DT-032+)
- Suggested git commit message

**Do NOT commit.** Dev lead will review, then commit.
