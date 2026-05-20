# BATCH-32 Instructions — TRC-P6-007 + TRC-P6-008

**Tasks:** TRC-P6-007 (CausalTreeView Vue component) + TRC-P6-008 (Causal tree composables and store)  
**Expected new tests:** 18 (9 for TRC-P6-007 + 9 for TRC-P6-008)

---

## Context

- Workspace root: `d:\Work\Tracer`
- Frontend: `d:\Work\Tracer\tracer-viewer\`
- All `@/` paths resolve to `tracer-viewer/src/`
- Run tests: `cd d:\Work\Tracer\tracer-viewer ; npx vitest run`
- Tests use Vitest + `@vue/test-utils` + jsdom; `ResizeObserver` is stubbed in `tests/setup.ts`
- Existing tests: 127 passing — do NOT break them

## Already done (do NOT recreate)

- `src/types/causalTree.ts` — `TraceTreeDto`, `TraceNodeDto`, `TraceEdgeDto`, `TraceSummaryDto` interfaces
- `src/rendering/causalTreeLayout.ts` — `layout()`, `LayoutResult`, `LaidOutNode`, `LaidOutEdge`, `LayoutConfig`
- `src/rendering/causalTreeRenderer.ts` — `renderTree()`, `CausalTreeRenderInput`
- `src/rendering/causalTreeHitTest.ts` — `findNodeAt()`
- `src/rendering/colorScheme.ts` — `buildNodeColorMap()` added

## Implementation Order

Implement in this order: API methods → store → composables → components → view → tests.

---

## Step 1 — Add causal tree API methods to `tracerApiClient.ts`

File: `tracer-viewer/src/api/tracerApiClient.ts`

Add these 4 methods to the `TracerApiClient` class (before the closing `}`):

```typescript
async getTraceTree(
  traceId: string,
  maxEvents = 1000,
  opts?: { signal?: AbortSignal },
): Promise<TraceTreeDto> {
  const params = new URLSearchParams({ maxEvents: String(maxEvents) });
  const res = await fetch(`/api/traces/${traceId}/tree?${params}`, { signal: opts?.signal });
  if (!res.ok) throw new Error(`getTraceTree: ${res.status}`);
  return res.json() as Promise<TraceTreeDto>;
}

async getTraceByEvent(
  eventId: string,
  maxEvents = 1000,
  opts?: { signal?: AbortSignal },
): Promise<TraceTreeDto> {
  const params = new URLSearchParams({ maxEvents: String(maxEvents) });
  const res = await fetch(`/api/events/${eventId}/trace?${params}`, { signal: opts?.signal });
  if (!res.ok) throw new Error(`getTraceByEvent: ${res.status}`);
  return res.json() as Promise<TraceTreeDto>;
}

async getEventAncestors(
  eventId: string,
  maxDepth = 50,
  opts?: { signal?: AbortSignal },
): Promise<TraceTreeDto> {
  const params = new URLSearchParams({ maxDepth: String(maxDepth) });
  const res = await fetch(`/api/events/${eventId}/ancestors?${params}`, { signal: opts?.signal });
  if (!res.ok) throw new Error(`getEventAncestors: ${res.status}`);
  return res.json() as Promise<TraceTreeDto>;
}

async getEventDescendants(
  eventId: string,
  maxDepth = 30,
  maxNodes = 1000,
  opts?: { signal?: AbortSignal },
): Promise<TraceTreeDto> {
  const params = new URLSearchParams({ maxDepth: String(maxDepth), maxNodes: String(maxNodes) });
  const res = await fetch(`/api/events/${eventId}/descendants?${params}`, { signal: opts?.signal });
  if (!res.ok) throw new Error(`getEventDescendants: ${res.status}`);
  return res.json() as Promise<TraceTreeDto>;
}
```

You also need to add the import at the top of `tracerApiClient.ts`:
```typescript
import type { TraceTreeDto } from '@/types/causalTree';
```

---

## Step 2 — Create `src/stores/causalTreeStore.ts`

```typescript
// src/stores/causalTreeStore.ts
import { defineStore } from 'pinia';
import type { TraceTreeDto } from '@/types/causalTree';

export interface CausalTreeRequest {
  kind: 'trace' | 'event' | 'ancestors' | 'descendants';
  id: string;
  maxEvents?: number;
  maxDepth?: number;
  maxNodes?: number;
}

export const useCausalTreeStore = defineStore('causalTree', {
  state: () => ({
    request: null as CausalTreeRequest | null,
    tree: null as TraceTreeDto | null,
    loading: false,
    error: null as string | null,
    selectedEventId: null as string | null,
  }),
  actions: {
    openTrace(traceId: string, maxEvents?: number) {
      this.request = { kind: 'trace', id: traceId, maxEvents };
      this.tree = null;
      this.selectedEventId = null;
    },
    openByEvent(eventId: string, maxEvents?: number) {
      this.request = { kind: 'event', id: eventId, maxEvents };
      this.tree = null;
      this.selectedEventId = eventId;
    },
    openAncestors(eventId: string, maxDepth?: number) {
      this.request = { kind: 'ancestors', id: eventId, maxDepth };
      this.tree = null;
      this.selectedEventId = eventId;
    },
    openDescendants(eventId: string, maxDepth?: number, maxNodes?: number) {
      this.request = { kind: 'descendants', id: eventId, maxDepth, maxNodes };
      this.tree = null;
      this.selectedEventId = eventId;
    },
    selectEvent(eventId: string | null) {
      this.selectedEventId = eventId;
    },
    setResult(tree: TraceTreeDto) {
      this.tree = tree;
      if (
        this.selectedEventId &&
        !tree.nodes.some(n => n.eventId === this.selectedEventId)
      ) {
        this.selectedEventId = pickInitialSelection(tree);
      } else if (!this.selectedEventId) {
        this.selectedEventId = pickInitialSelection(tree);
      }
    },
    setError(message: string) {
      this.error = message;
    },
    clear() {
      this.request = null;
      this.tree = null;
      this.selectedEventId = null;
      this.error = null;
    },
    retry() {
      const r = this.request;
      if (!r) return;
      this.request = null;
      this.request = { ...r }; // new object reference so watch fires again
    },
  },
});

function pickInitialSelection(tree: TraceTreeDto): string | null {
  const notable = tree.nodes.find(n => n.notableLabel);
  if (notable) return notable.eventId;
  return tree.nodes[0]?.eventId ?? null;
}
```

---

## Step 3 — Create `src/composables/useCausalTreeQuery.ts`

```typescript
// src/composables/useCausalTreeQuery.ts
import { watch } from 'vue';
import { useCausalTreeStore } from '@/stores/causalTreeStore';
import { api } from '@/api/tracerApiClient';

export function useCausalTreeQuery() {
  const store = useCausalTreeStore();
  let abortCtrl: AbortController | null = null;

  watch(
    () => store.request,
    async (req) => {
      if (!req) return;

      abortCtrl?.abort();
      abortCtrl = new AbortController();
      const signal = abortCtrl.signal;

      store.loading = true;
      store.error = null;

      try {
        let tree;
        switch (req.kind) {
          case 'trace':
            tree = await api.getTraceTree(req.id, req.maxEvents ?? 1000, { signal });
            break;
          case 'event':
            tree = await api.getTraceByEvent(req.id, req.maxEvents ?? 1000, { signal });
            break;
          case 'ancestors':
            tree = await api.getEventAncestors(req.id, req.maxDepth ?? 50, { signal });
            break;
          case 'descendants':
            tree = await api.getEventDescendants(
              req.id,
              req.maxDepth ?? 30,
              req.maxNodes ?? 1000,
              { signal },
            );
            break;
          default:
            return;
        }
        store.setResult(tree);
      } catch (err: unknown) {
        if (err instanceof Error && err.name === 'AbortError') return;
        store.setError(err instanceof Error ? err.message : 'Failed to load causal tree');
      } finally {
        store.loading = false;
      }
    },
    { immediate: true },
  );
}
```

---

## Step 4 — Create `src/composables/useCausalTreeLayout.ts`

```typescript
// src/composables/useCausalTreeLayout.ts
import { ref, watchEffect } from 'vue';
import { useCausalTreeStore } from '@/stores/causalTreeStore';
import { layout, type LayoutResult, type LayoutConfig } from '@/rendering/causalTreeLayout';

const DEFAULT_CONFIG: LayoutConfig = {
  nodeRadiusPx: 14,
  hSpacingPx: 40,
  vSpacingPx: 80,
  paddingPx: 40,
};

export function useCausalTreeLayout(config: LayoutConfig = DEFAULT_CONFIG) {
  const store = useCausalTreeStore();
  const layoutResult = ref<LayoutResult | null>(null);

  watchEffect(() => {
    if (store.tree) {
      layoutResult.value = layout(store.tree, config);
    } else {
      layoutResult.value = null;
    }
  });

  return { layoutResult };
}
```

---

## Step 5 — Create `src/components/CausalNodeInspector.vue`

This is a simple node inspector for Phase 6. The full pivot-button functionality comes in TRC-P6-009.

```vue
<!-- src/components/CausalNodeInspector.vue -->
<script setup lang="ts">
import type { TraceNodeDto } from '@/types/causalTree';

defineProps<{ event: TraceNodeDto }>();
</script>

<template>
  <section class="causal-node-inspector">
    <div class="causal-node-inspector__row">
      <span class="causal-node-inspector__label">Topic</span>
      <span class="causal-node-inspector__value">{{ event.topic }}</span>
    </div>
    <div class="causal-node-inspector__row">
      <span class="causal-node-inspector__label">Node</span>
      <span class="causal-node-inspector__value">{{ event.publisherNode }}</span>
    </div>
    <div class="causal-node-inspector__row">
      <span class="causal-node-inspector__label">Event ID</span>
      <span class="causal-node-inspector__value causal-node-inspector__value--mono">{{ event.eventId }}</span>
    </div>
    <div class="causal-node-inspector__row">
      <span class="causal-node-inspector__label">Time</span>
      <span class="causal-node-inspector__value">{{ event.publishWallclock }}</span>
    </div>
    <div v-if="event.notableLabel" class="causal-node-inspector__row">
      <span class="causal-node-inspector__label">Notable</span>
      <span class="causal-node-inspector__value">{{ event.notableLabel }}</span>
    </div>
  </section>
</template>

<style scoped>
.causal-node-inspector {
  padding: 1rem;
  background: var(--c-bg-surface, #1e1e2e);
  border-radius: 8px;
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}
.causal-node-inspector__row {
  display: flex;
  gap: 0.75rem;
}
.causal-node-inspector__label {
  font-size: 0.75rem;
  color: var(--c-text-muted, #888);
  text-transform: uppercase;
  min-width: 5rem;
}
.causal-node-inspector__value {
  font-size: 0.875rem;
}
.causal-node-inspector__value--mono {
  font-family: var(--font-mono, monospace);
  word-break: break-all;
}
</style>
```

---

## Step 6 — Create `src/components/TraceSummaryPanel.vue`

Note: `formatDuration` in `src/utils/time.ts` takes an ISO duration string. `totalSpanMs` is a number.
Define a local `formatMs(ms: number): string` helper in the component script.

```vue
<!-- src/components/TraceSummaryPanel.vue -->
<script setup lang="ts">
import { computed } from 'vue';
import type { TraceSummaryDto } from '@/types/causalTree';
import { buildNodeColorMap } from '@/rendering/colorScheme';

const props = defineProps<{ summary: TraceSummaryDto }>();

const nodeColors = computed(() => buildNodeColorMap(props.summary.participatingNodes));

const spanDisplay = computed(() => formatMs(props.summary.totalSpanMs));

function formatMs(ms: number): string {
  if (ms < 1) return `${(ms * 1000).toFixed(0)}μs`;
  if (ms < 1000) return `${ms.toFixed(0)}ms`;
  if (ms < 60000) return `${(ms / 1000).toFixed(2)}s`;
  return `${(ms / 60000).toFixed(1)}min`;
}
</script>

<template>
  <section class="trace-summary">
    <div class="trace-summary__field">
      <div class="trace-summary__label">Trace ID</div>
      <div class="trace-summary__value trace-summary__value--mono">
        {{ summary.traceId }}
      </div>
    </div>

    <div class="trace-summary__row">
      <div class="trace-summary__field">
        <div class="trace-summary__label">Events</div>
        <div class="trace-summary__value">
          {{ summary.totalEvents.toLocaleString() }}
          <span v-if="summary.truncated" class="trace-summary__warn">
            (of {{ summary.totalEventsAvailable?.toLocaleString() ?? 'many' }})
          </span>
        </div>
      </div>
      <div class="trace-summary__field">
        <div class="trace-summary__label">Span</div>
        <div class="trace-summary__value">{{ spanDisplay }}</div>
      </div>
    </div>

    <div class="trace-summary__row">
      <div class="trace-summary__field">
        <div class="trace-summary__label">Roots</div>
        <div class="trace-summary__value">{{ summary.rootCount }}</div>
      </div>
      <div class="trace-summary__field">
        <div class="trace-summary__label">Leaves</div>
        <div class="trace-summary__value">{{ summary.leafCount }}</div>
      </div>
    </div>

    <div class="trace-summary__field">
      <div class="trace-summary__label">
        Nodes ({{ summary.participatingNodes.length }})
      </div>
      <div class="trace-summary__nodes">
        <span
          v-for="node in summary.participatingNodes"
          :key="node"
          class="trace-summary__node"
          :style="{ borderColor: nodeColors.get(node) }"
        >
          {{ node }}
        </span>
      </div>
    </div>

    <div v-if="summary.truncated" class="trace-summary__truncation-notice">
      This trace was truncated. Showing {{ summary.totalEvents.toLocaleString() }} of
      {{ summary.totalEventsAvailable?.toLocaleString() ?? 'many' }} events.
    </div>
  </section>
</template>

<style scoped>
.trace-summary {
  background: var(--c-bg-surface, #1e1e2e);
  border-radius: 12px;
  padding: 1.5rem;
  display: flex;
  flex-direction: column;
  gap: 1rem;
}
.trace-summary__row {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 1rem;
}
.trace-summary__label {
  font-size: 0.75rem;
  color: var(--c-text-muted, #888);
  text-transform: uppercase;
  letter-spacing: 0.05em;
  margin-bottom: 0.25rem;
}
.trace-summary__value {
  font-size: 1.25rem;
  font-weight: 500;
}
.trace-summary__value--mono {
  font-family: var(--font-mono, monospace);
  font-size: 0.875rem;
  word-break: break-all;
}
.trace-summary__warn {
  color: var(--c-warning, #e8b048);
  font-size: 0.875rem;
  margin-left: 0.5rem;
}
.trace-summary__nodes {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
}
.trace-summary__node {
  padding: 0.25rem 0.5rem;
  background: var(--c-bg-subtle, #252538);
  border-left: 3px solid;
  border-radius: 4px;
  font-size: 0.875rem;
  font-family: var(--font-mono, monospace);
}
.trace-summary__truncation-notice {
  padding: 0.75rem;
  background: rgba(232, 176, 72, 0.1);
  border: 1px solid var(--c-warning, #e8b048);
  border-radius: 6px;
  font-size: 0.875rem;
  color: var(--c-warning, #e8b048);
}
</style>
```

---

## Step 7 — Create `src/components/TraceSearchInput.vue`

```vue
<!-- src/components/TraceSearchInput.vue -->
<script setup lang="ts">
import { ref } from 'vue';
import { useRouter } from 'vue-router';

const router = useRouter();
const input = ref('');
const kind = ref<'event' | 'trace'>('event');
const error = ref<string | null>(null);

function submit() {
  error.value = null;
  const value = input.value.trim();
  if (!value) return;

  if (!/^[0-9a-fA-F]{16}$/.test(value)) {
    error.value = 'Expected a 16-character hex ID';
    return;
  }

  if (kind.value === 'trace') {
    void router.push({ name: 'causal-by-trace', params: { traceId: value.toLowerCase() } });
  } else {
    void router.push({ name: 'causal-by-event', params: { eventId: value.toLowerCase() } });
  }
  input.value = '';
}
</script>

<template>
  <form
    class="trace-search"
    @submit.prevent="submit"
  >
    <select
      v-model="kind"
      class="trace-search__kind"
    >
      <option value="event">
        Event
      </option>
      <option value="trace">
        Trace
      </option>
    </select>
    <input
      v-model="input"
      type="text"
      placeholder="Paste 16-char hex ID"
      class="trace-search__input"
      :class="{ 'trace-search__input--error': error }"
    />
    <button
      type="submit"
      class="trace-search__btn"
      :disabled="!input"
    >
      Open
    </button>
    <div
      v-if="error"
      class="trace-search__error"
    >
      {{ error }}
    </div>
  </form>
</template>

<style scoped>
.trace-search {
  display: flex;
  gap: 0.5rem;
  flex: 1;
  position: relative;
  align-items: center;
}
.trace-search__kind {
  padding: 0.5rem;
  background: var(--c-bg-subtle, #252538);
  border: 1px solid var(--c-bg-subtle, #252538);
  border-radius: 6px;
  color: var(--c-text, #cdd6f4);
  font-size: 0.875rem;
}
.trace-search__input {
  flex: 1;
  padding: 0.5rem 0.75rem;
  background: var(--c-bg-subtle, #252538);
  border: 1px solid var(--c-bg-subtle, #252538);
  border-radius: 6px;
  color: var(--c-text, #cdd6f4);
  font-family: var(--font-mono, monospace);
  font-size: 0.875rem;
}
.trace-search__input--error {
  border-color: var(--c-danger, #e85c5c);
}
.trace-search__btn {
  padding: 0.5rem 1rem;
  background: var(--c-accent, #1976d2);
  color: white;
  border: none;
  border-radius: 6px;
  cursor: pointer;
}
.trace-search__btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}
.trace-search__error {
  position: absolute;
  top: 100%;
  left: 0;
  margin-top: 0.25rem;
  color: var(--c-danger, #e85c5c);
  font-size: 0.75rem;
}
</style>
```

---

## Step 8 — Create `src/components/TraceNodeTooltip.vue`

Simple tooltip — no dedicated tests needed for Phase 6.

```vue
<!-- src/components/TraceNodeTooltip.vue -->
<script setup lang="ts">
import type { TraceNodeDto } from '@/types/causalTree';

defineProps<{
  node: TraceNodeDto;
  x: number;
  y: number;
}>();
</script>

<template>
  <div
    class="trace-node-tooltip"
    :style="{ left: `${x}px`, top: `${y}px` }"
  >
    <div class="trace-node-tooltip__topic">{{ node.topic }}</div>
    <div class="trace-node-tooltip__node">{{ node.publisherNode }}</div>
    <div class="trace-node-tooltip__time">{{ node.publishWallclock }}</div>
  </div>
</template>

<style scoped>
.trace-node-tooltip {
  position: absolute;
  background: var(--c-bg-surface, #1e1e2e);
  border: 1px solid var(--c-bg-subtle, #252538);
  border-radius: 6px;
  padding: 0.5rem 0.75rem;
  font-size: 0.8125rem;
  pointer-events: none;
  z-index: 100;
  max-width: 260px;
}
.trace-node-tooltip__topic {
  font-weight: 600;
  margin-bottom: 0.25rem;
}
.trace-node-tooltip__node {
  color: var(--c-text-muted, #888);
  font-family: var(--font-mono, monospace);
  font-size: 0.75rem;
}
.trace-node-tooltip__time {
  color: var(--c-text-muted, #888);
  font-size: 0.75rem;
  margin-top: 0.25rem;
}
</style>
```

---

## Step 9 — Create `src/components/CausalTreeCanvas.vue`

```vue
<!-- src/components/CausalTreeCanvas.vue -->
<script setup lang="ts">
import { ref, watch, onMounted } from 'vue';
import { layout, type LayoutResult } from '@/rendering/causalTreeLayout';
import { renderTree } from '@/rendering/causalTreeRenderer';
import { findNodeAt } from '@/rendering/causalTreeHitTest';
import type { TraceTreeDto } from '@/types/causalTree';
import { useResizeObserver } from '@/composables/useResizeObserver';
import { buildNodeColorMap } from '@/rendering/colorScheme';

const props = defineProps<{
  tree: TraceTreeDto;
  selectedEventId: string | null;
}>();

const emit = defineEmits<{ select: [eventId: string | null] }>();

const containerRef = ref<HTMLDivElement | null>(null);
const canvasRef = ref<HTMLCanvasElement | null>(null);
const layoutResult = ref<LayoutResult | null>(null);

const viewport = ref({ tx: 0, ty: 0, scale: 1 });

watch(
  () => props.tree,
  (tree) => {
    layoutResult.value = layout(tree, {
      nodeRadiusPx: 14,
      hSpacingPx: 40,
      vSpacingPx: 80,
      paddingPx: 40,
    });
  },
  { immediate: true },
);

function draw() {
  const canvas = canvasRef.value;
  const layoutR = layoutResult.value;
  if (!canvas || !layoutR) return;
  const ctx = canvas.getContext('2d');
  if (!ctx) return;

  const dpr = window.devicePixelRatio || 1;
  const cssWidth = canvas.clientWidth;
  const cssHeight = canvas.clientHeight;
  if (canvas.width !== cssWidth * dpr) canvas.width = cssWidth * dpr;
  if (canvas.height !== cssHeight * dpr) canvas.height = cssHeight * dpr;
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0);

  ctx.clearRect(0, 0, cssWidth, cssHeight);
  ctx.save();
  ctx.translate(viewport.value.tx, viewport.value.ty);
  ctx.scale(viewport.value.scale, viewport.value.scale);

  const nodeColors = buildNodeColorMap(props.tree.summary.participatingNodes);
  renderTree(ctx, layoutR, {
    selectedEventId: props.selectedEventId,
    nodeColors,
  });

  ctx.restore();
}

watch([layoutResult, viewport, () => props.selectedEventId], draw, { deep: true });
useResizeObserver(containerRef, draw);
onMounted(draw);

let dragging = false;
let lastX = 0;
let lastY = 0;

function onPointerDown(e: PointerEvent) {
  dragging = true;
  lastX = e.clientX;
  lastY = e.clientY;
  (e.target as Element).setPointerCapture(e.pointerId);
}

function onPointerMove(e: PointerEvent) {
  if (!dragging) return;
  viewport.value = {
    ...viewport.value,
    tx: viewport.value.tx + e.clientX - lastX,
    ty: viewport.value.ty + e.clientY - lastY,
  };
  lastX = e.clientX;
  lastY = e.clientY;
}

function onPointerUp(e: PointerEvent) {
  dragging = false;
  (e.target as Element).releasePointerCapture(e.pointerId);
}

function onWheel(e: WheelEvent) {
  e.preventDefault();
  const canvas = canvasRef.value!;
  const rect = canvas.getBoundingClientRect();
  const cursorX = e.clientX - rect.left;
  const cursorY = e.clientY - rect.top;

  const worldX = (cursorX - viewport.value.tx) / viewport.value.scale;
  const worldY = (cursorY - viewport.value.ty) / viewport.value.scale;

  const factor = e.deltaY > 0 ? 0.85 : 1.18;
  const newScale = Math.max(0.2, Math.min(4, viewport.value.scale * factor));

  viewport.value = {
    tx: cursorX - worldX * newScale,
    ty: cursorY - worldY * newScale,
    scale: newScale,
  };
}

function onClick(e: PointerEvent) {
  if (!layoutResult.value || !canvasRef.value) return;
  const rect = canvasRef.value.getBoundingClientRect();
  const cursorX = e.clientX - rect.left;
  const cursorY = e.clientY - rect.top;
  const worldX = (cursorX - viewport.value.tx) / viewport.value.scale;
  const worldY = (cursorY - viewport.value.ty) / viewport.value.scale;
  const hit = findNodeAt(layoutResult.value, worldX, worldY, 14);
  emit('select', hit?.eventId ?? null);
}
</script>

<template>
  <div
    ref="containerRef"
    class="causal-tree-canvas"
  >
    <canvas
      ref="canvasRef"
      @pointerdown="onPointerDown"
      @pointermove="onPointerMove"
      @pointerup="onPointerUp"
      @wheel.prevent="onWheel"
      @click="onClick"
    />
  </div>
</template>

<style scoped>
.causal-tree-canvas {
  position: relative;
  background: var(--c-bg-surface, #1e1e2e);
  border-radius: 12px;
  overflow: hidden;
  min-height: 500px;
}
.causal-tree-canvas canvas {
  width: 100%;
  height: 100%;
  display: block;
  cursor: grab;
}
.causal-tree-canvas canvas:active {
  cursor: grabbing;
}
</style>
```

---

## Step 10 — Create `src/views/CausalTreeView.vue`

```vue
<!-- src/views/CausalTreeView.vue -->
<script setup lang="ts">
import { computed } from 'vue';
import { useCausalTreeStore } from '@/stores/causalTreeStore';
import { useCausalTreeQuery } from '@/composables/useCausalTreeQuery';
import CausalTreeCanvas from '@/components/CausalTreeCanvas.vue';
import TraceSummaryPanel from '@/components/TraceSummaryPanel.vue';
import CausalNodeInspector from '@/components/CausalNodeInspector.vue';
import TraceSearchInput from '@/components/TraceSearchInput.vue';
import LoadingSpinner from '@/components/LoadingSpinner.vue';
import ErrorMessage from '@/components/ErrorMessage.vue';

const store = useCausalTreeStore();
useCausalTreeQuery();

const selectedNode = computed(() => {
  if (!store.selectedEventId || !store.tree) return null;
  return store.tree.nodes.find(n => n.eventId === store.selectedEventId) ?? null;
});
</script>

<template>
  <div class="causal-tree-view">
    <header class="causal-tree-view__header">
      <h1>Causal tree</h1>
      <TraceSearchInput />
    </header>

    <LoadingSpinner v-if="store.loading && !store.tree" />

    <div
      v-else-if="store.error"
      data-testid="error-message"
    >
      <ErrorMessage
        :message="store.error"
        @retry="store.retry"
      />
    </div>

    <div
      v-else-if="store.tree"
      class="causal-tree-view__grid"
      :class="{ 'causal-tree-view__grid--with-inspector': selectedNode !== null }"
    >
      <TraceSummaryPanel
        class="causal-tree-view__summary"
        :summary="store.tree.summary"
      />
      <CausalTreeCanvas
        class="causal-tree-view__canvas"
        :tree="store.tree"
        :selected-event-id="store.selectedEventId"
        @select="store.selectEvent"
      />
      <CausalNodeInspector
        v-if="selectedNode"
        class="causal-tree-view__inspector"
        :event="selectedNode"
      />
    </div>

    <div
      v-else
      class="causal-tree-view__empty"
    >
      Open a causal tree from the timeline, or paste an event ID above.
    </div>
  </div>
</template>

<style scoped>
.causal-tree-view {
  max-width: 1600px;
  margin: 0 auto;
  padding: 1.5rem;
}
.causal-tree-view__header {
  display: flex;
  align-items: center;
  gap: 1.5rem;
  margin-bottom: 1rem;
}
.causal-tree-view__header h1 {
  margin: 0;
}
.causal-tree-view__grid {
  display: grid;
  grid-template-columns: 280px 1fr;
  grid-template-areas: "summary canvas";
  gap: 1.5rem;
}
.causal-tree-view__grid--with-inspector {
  grid-template-columns: 280px 1fr 400px;
  grid-template-areas: "summary canvas inspector";
}
.causal-tree-view__summary  { grid-area: summary; }
.causal-tree-view__canvas   { grid-area: canvas; }
.causal-tree-view__inspector { grid-area: inspector; }
</style>
```

---

## Step 11 — Update `src/router/index.ts`

Add the two new routes before the closing `]`:

```typescript
{
  path: '/v/trace/:traceId',
  name: 'causal-by-trace',
  component: () => import('@/views/CausalTreeView.vue'),
},
{
  path: '/v/causal/:eventId',
  name: 'causal-by-event',
  component: () => import('@/views/CausalTreeView.vue'),
},
```

---

## Step 12 — Tests

### 12.1 — `tests/unit/causalTreeStore.spec.ts`

```typescript
import { describe, it, expect, beforeEach } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';
import { useCausalTreeStore } from '../../src/stores/causalTreeStore';
import type { TraceTreeDto, TraceNodeDto } from '../../src/types/causalTree';

function makeTree(nodes: Partial<TraceNodeDto>[]): TraceTreeDto {
  const fullNodes: TraceNodeDto[] = nodes.map((n, i) => ({
    eventId: `evt-${i}`,
    traceId: 'trace-1',
    publishWallclock: '2026-01-01T10:00:00.000Z',
    publisherNode: 'node-a',
    topic: 'test.topic',
    ...n,
  }));
  return {
    traceId: 'trace-1',
    nodes: fullNodes,
    edges: [],
    rootEventIds: fullNodes.map(n => n.eventId),
    leafEventIds: fullNodes.map(n => n.eventId),
    summary: {
      traceId: 'trace-1',
      totalEvents: fullNodes.length,
      truncated: false,
      totalSpanMs: 0,
      participatingNodes: ['node-a'],
      rootCount: fullNodes.length,
      leafCount: fullNodes.length,
    },
  };
}

describe('causalTreeStore', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
  });

  it('openTrace_SetsRequestKindTraceAndClearsTree', () => {
    const store = useCausalTreeStore();
    // Seed some state first
    store.tree = makeTree([{ eventId: 'old-evt' }]);

    store.openTrace('abc0123456789def');

    expect(store.request).not.toBeNull();
    expect(store.request!.kind).toBe('trace');
    expect(store.request!.id).toBe('abc0123456789def');
    expect(store.tree).toBeNull();
  });

  it('setResult_WhenSelectedIdNotInTree_SelectsFirstNotableNode', () => {
    const store = useCausalTreeStore();
    store.selectedEventId = 'nonexistent';

    const tree = makeTree([
      { eventId: 'plain-evt' },
      { eventId: 'notable-evt', notableLabel: 'ImportantThing' },
    ]);

    store.setResult(tree);

    expect(store.selectedEventId).toBe('notable-evt');
  });

  it('setResult_WhenNoNotableNodes_SelectsFirstNode', () => {
    const store = useCausalTreeStore();
    store.selectedEventId = null;

    const tree = makeTree([
      { eventId: 'first-evt' },
      { eventId: 'second-evt' },
    ]);

    store.setResult(tree);

    expect(store.selectedEventId).toBe('first-evt');
  });

  it('retry_ReassignsRequest_TriggeringWatcher', () => {
    const store = useCausalTreeStore();
    store.openTrace('abc0123456789def');

    const firstRef = store.request;
    store.retry();
    const secondRef = store.request;

    expect(secondRef).not.toBeNull();
    expect(secondRef).not.toBe(firstRef); // new object reference
    expect(secondRef!.kind).toBe('trace');
    expect(secondRef!.id).toBe('abc0123456789def');
  });
});
```

### 12.2 — `tests/unit/useCausalTreeQuery.spec.ts`

```typescript
import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';
import { defineComponent, nextTick } from 'vue';
import { mount, flushPromises } from '@vue/test-utils';
import { useCausalTreeStore } from '../../src/stores/causalTreeStore';
import type { TraceTreeDto } from '../../src/types/causalTree';

vi.mock('@/api/tracerApiClient', () => ({
  api: {
    getTraceTree:       vi.fn(),
    getTraceByEvent:    vi.fn(),
    getEventAncestors:  vi.fn(),
    getEventDescendants: vi.fn(),
  },
}));

function makeMinimalTree(): TraceTreeDto {
  return {
    traceId: 'trace-1',
    nodes: [{ eventId: 'e1', traceId: 'trace-1', publishWallclock: '2026-01-01T10:00:00.000Z', publisherNode: 'n', topic: 't' }],
    edges: [],
    rootEventIds: ['e1'],
    leafEventIds: ['e1'],
    summary: { traceId: 'trace-1', totalEvents: 1, truncated: false, totalSpanMs: 0, participatingNodes: ['n'], rootCount: 1, leafCount: 1 },
  };
}

describe('useCausalTreeQuery', () => {
  let pinia: ReturnType<typeof createPinia>;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
    vi.clearAllMocks();
  });

  afterEach(() => {
    vi.clearAllMocks();
  });

  function mountWithQuery() {
    // Dynamic import to ensure mocks are applied before module load
    return mount(defineComponent({
      setup() {
        // Inline require to pick up mocks
        const { useCausalTreeQuery } = require('../../src/composables/useCausalTreeQuery');
        useCausalTreeQuery();
        return {};
      },
      template: '<div/>',
    }), { global: { plugins: [pinia] } });
  }

  it('requestKindTrace_CallsGetTraceTree', async () => {
    const { api } = await import('@/api/tracerApiClient');
    (api.getTraceTree as ReturnType<typeof vi.fn>).mockResolvedValue(makeMinimalTree());

    const store = useCausalTreeStore();
    mountWithQuery();

    store.request = { kind: 'trace', id: 'abc1234567890def', maxEvents: 1000 };
    await flushPromises();

    expect(api.getTraceTree).toHaveBeenCalledWith('abc1234567890def', 1000, expect.any(Object));
  });

  it('requestKindAncestors_CallsGetEventAncestors', async () => {
    const { api } = await import('@/api/tracerApiClient');
    (api.getEventAncestors as ReturnType<typeof vi.fn>).mockResolvedValue(makeMinimalTree());

    const store = useCausalTreeStore();
    mountWithQuery();

    store.request = { kind: 'ancestors', id: 'def1234567890abc', maxDepth: 50 };
    await flushPromises();

    expect(api.getEventAncestors).toHaveBeenCalledWith('def1234567890abc', 50, expect.any(Object));
  });

  it('secondRequest_AbortsFirst_BeforeFirstResolves', async () => {
    const { api } = await import('@/api/tracerApiClient');

    // Intercept AbortController to spy on abort
    const OriginalAbortController = globalThis.AbortController;
    const controllers: { abort: ReturnType<typeof vi.fn>; signal: AbortSignal }[] = [];
    class MockAbortController {
      abort = vi.fn();
      signal: AbortSignal;
      constructor() {
        this.signal = new OriginalAbortController().signal;
        controllers.push(this as unknown as { abort: ReturnType<typeof vi.fn>; signal: AbortSignal });
      }
    }
    globalThis.AbortController = MockAbortController as unknown as typeof AbortController;

    let firstResolve!: (v: TraceTreeDto) => void;
    const firstPending = new Promise<TraceTreeDto>(r => { firstResolve = r; });
    (api.getTraceTree as ReturnType<typeof vi.fn>)
      .mockReturnValueOnce(firstPending)
      .mockResolvedValueOnce(makeMinimalTree());

    const store = useCausalTreeStore();
    mountWithQuery();

    store.request = { kind: 'trace', id: 'first1234567890aa' };
    await nextTick();
    await nextTick();

    expect(controllers.length).toBeGreaterThanOrEqual(1);

    store.request = { kind: 'trace', id: 'second123456789b' };
    await nextTick();
    await nextTick();

    expect(controllers[0].abort).toHaveBeenCalled();

    // Cleanup
    globalThis.AbortController = OriginalAbortController;
    firstResolve(makeMinimalTree());
    await flushPromises();
  });

  it('abortError_DoesNotSetStoreError', async () => {
    const { api } = await import('@/api/tracerApiClient');
    const abortError = new DOMException('AbortError', 'AbortError');
    Object.defineProperty(abortError, 'name', { value: 'AbortError' });
    (api.getTraceTree as ReturnType<typeof vi.fn>).mockRejectedValue(abortError);

    const store = useCausalTreeStore();
    mountWithQuery();

    store.request = { kind: 'trace', id: 'abc1234567890def' };
    await flushPromises();

    expect(store.error).toBeNull();
  });
});
```

**IMPORTANT NOTE for `useCausalTreeQuery.spec.ts`**: The `require()` inside `setup()` approach may not work with ES modules. If it doesn't work, use this alternative — just import `useCausalTreeQuery` statically at the top of the file (after the `vi.mock` call), since Vitest hoists `vi.mock` calls before imports:

```typescript
import { useCausalTreeQuery } from '../../src/composables/useCausalTreeQuery';
// ...
function mountWithQuery() {
  return mount(defineComponent({
    setup() { useCausalTreeQuery(); return {}; },
    template: '<div/>',
  }), { global: { plugins: [pinia] } });
}
```

Use whichever approach compiles without errors.

### 12.3 — `tests/unit/useCausalTreeLayout.spec.ts`

```typescript
import { describe, it, expect, beforeEach } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';
import { defineComponent, nextTick } from 'vue';
import { mount } from '@vue/test-utils';
import { useCausalTreeStore } from '../../src/stores/causalTreeStore';
import { useCausalTreeLayout } from '../../src/composables/useCausalTreeLayout';
import type { TraceTreeDto, TraceNodeDto } from '../../src/types/causalTree';

function makeTree(n: number): TraceTreeDto {
  const nodes: TraceNodeDto[] = Array.from({ length: n }, (_, i) => ({
    eventId: `evt-${i}`,
    traceId: 'trace-1',
    publishWallclock: `2026-01-01T10:0${i % 10}:00.000Z`,
    publisherNode: 'node-a',
    topic: 'test',
  }));
  return {
    traceId: 'trace-1',
    nodes,
    edges: [],
    rootEventIds: nodes.map(n => n.eventId),
    leafEventIds: nodes.map(n => n.eventId),
    summary: {
      traceId: 'trace-1', totalEvents: n, truncated: false, totalSpanMs: 0,
      participatingNodes: ['node-a'], rootCount: n, leafCount: n,
    },
  };
}

describe('useCausalTreeLayout', () => {
  let pinia: ReturnType<typeof createPinia>;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
  });

  it('layoutUpdates_WhenTreePropChanges', async () => {
    const store = useCausalTreeStore();
    let layoutRef: ReturnType<typeof useCausalTreeLayout>['layoutResult'];

    const wrapper = mount(defineComponent({
      setup() {
        const { layoutResult } = useCausalTreeLayout();
        layoutRef = layoutResult;
        return {};
      },
      template: '<div/>',
    }), { global: { plugins: [pinia] } });

    // Initially null
    expect(layoutRef!.value).toBeNull();

    // Set tree with 5 nodes
    store.tree = makeTree(5);
    await nextTick();
    expect(layoutRef!.value).not.toBeNull();
    expect(layoutRef!.value!.nodes.size).toBe(5);

    // Change to 10 nodes
    store.tree = makeTree(10);
    await nextTick();
    expect(layoutRef!.value!.nodes.size).toBe(10);

    wrapper.unmount();
  });
});
```

### 12.4 — `tests/unit/CausalTreeView.spec.ts`

```typescript
import { describe, it, expect, beforeEach, vi } from 'vitest';
import { mount, flushPromises } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { createRouter, createMemoryHistory } from 'vue-router';
import CausalTreeView from '@/views/CausalTreeView.vue';
import { useCausalTreeStore } from '@/stores/causalTreeStore';
import type { TraceTreeDto } from '@/types/causalTree';

// Mock useCausalTreeQuery so it doesn't fire API calls in view tests
vi.mock('@/composables/useCausalTreeQuery', () => ({
  useCausalTreeQuery: vi.fn(),
}));

function makeRouter() {
  return createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/', component: { template: '<div/>' } }],
  });
}

function makeTree(): TraceTreeDto {
  return {
    traceId: 'aabbccddeeff0011',
    nodes: [
      { eventId: 'evt-1', traceId: 'aabbccddeeff0011', publishWallclock: '2026-01-01T10:00:00.000Z', publisherNode: 'node-a', topic: 'test' },
    ],
    edges: [],
    rootEventIds: ['evt-1'],
    leafEventIds: ['evt-1'],
    summary: {
      traceId: 'aabbccddeeff0011', totalEvents: 1, truncated: false, totalSpanMs: 100,
      participatingNodes: ['node-a'], rootCount: 1, leafCount: 1,
    },
  };
}

describe('CausalTreeView', () => {
  let pinia: ReturnType<typeof createPinia>;
  let router: ReturnType<typeof makeRouter>;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
    router = makeRouter();
  });

  function mountView() {
    return mount(CausalTreeView, {
      global: {
        plugins: [pinia, router],
        stubs: {
          CausalTreeCanvas: true,
          TraceSummaryPanel: true,
          CausalNodeInspector: true,
          TraceSearchInput: true,
          LoadingSpinner: { template: '<div class="loading-spinner"/>' },
          ErrorMessage: { template: '<div><slot/><button @click="$emit(\'retry\')">Retry</button></div>', emits: ['retry'] },
        },
      },
    });
  }

  it('renders_LoadingSpinner_WhenStoreIsLoadingAndNoTree', async () => {
    const store = useCausalTreeStore();
    store.loading = true;
    store.tree = null;

    const wrapper = mountView();
    await flushPromises();

    expect(wrapper.find('.loading-spinner').exists()).toBe(true);
    expect(wrapper.findComponent({ name: 'CausalTreeCanvas' }).exists()).toBe(false);
  });

  it('renders_ErrorMessage_WithRetryButton_WhenStoreHasError', async () => {
    const store = useCausalTreeStore();
    store.error = 'timeout';
    store.loading = false;
    store.tree = null;

    const wrapper = mountView();
    await flushPromises();

    const errorDiv = wrapper.find('[data-testid="error-message"]');
    expect(errorDiv.exists()).toBe(true);

    const retryBtn = errorDiv.find('button');
    expect(retryBtn.exists()).toBe(true);

    const retrySpy = vi.spyOn(store, 'retry');
    await retryBtn.trigger('click');
    expect(retrySpy).toHaveBeenCalled();
  });

  it('renders_ThreeColumnGrid_WhenTreeLoadedAndNodeSelected', async () => {
    const store = useCausalTreeStore();
    store.tree = makeTree();
    store.selectedEventId = 'evt-1';
    store.loading = false;
    store.error = null;

    const wrapper = mountView();
    await flushPromises();

    expect(wrapper.find('.causal-tree-view__summary').exists()).toBe(true);
    expect(wrapper.find('.causal-tree-view__canvas').exists()).toBe(true);
    expect(wrapper.find('.causal-tree-view__inspector').exists()).toBe(true);
  });

  it('renders_TwoColumnGrid_WhenTreeLoadedAndNoNodeSelected', async () => {
    const store = useCausalTreeStore();
    store.tree = makeTree();
    store.selectedEventId = null;
    store.loading = false;
    store.error = null;

    const wrapper = mountView();
    await flushPromises();

    expect(wrapper.find('.causal-tree-view__summary').exists()).toBe(true);
    expect(wrapper.find('.causal-tree-view__canvas').exists()).toBe(true);
    expect(wrapper.find('.causal-tree-view__inspector').exists()).toBe(false);
  });

  it('renders_EmptyPrompt_WhenNoTreeAndNotLoading', async () => {
    const store = useCausalTreeStore();
    store.tree = null;
    store.loading = false;
    store.error = null;

    const wrapper = mountView();
    await flushPromises();

    expect(wrapper.find('.causal-tree-view__empty').exists()).toBe(true);
  });
});
```

### 12.5 — `tests/unit/TraceSummaryPanel.spec.ts`

```typescript
import { describe, it, expect } from 'vitest';
import { mount } from '@vue/test-utils';
import TraceSummaryPanel from '@/components/TraceSummaryPanel.vue';
import type { TraceSummaryDto } from '@/types/causalTree';
import { buildNodeColorMap } from '@/rendering/colorScheme';

function makeSummary(overrides: Partial<TraceSummaryDto> = {}): TraceSummaryDto {
  return {
    traceId: 'aabbccddeeff0011',
    totalEvents: 10,
    truncated: false,
    totalSpanMs: 500,
    participatingNodes: ['node-alpha'],
    rootCount: 1,
    leafCount: 3,
    ...overrides,
  };
}

describe('TraceSummaryPanel', () => {
  it('renders_TruncationNotice_WhenSummaryTruncatedIsTrue', () => {
    const summary = makeSummary({
      truncated: true,
      totalEventsAvailable: 6000,
      totalEvents: 1000,
    });

    const wrapper = mount(TraceSummaryPanel, { props: { summary } });

    const notice = wrapper.find('.trace-summary__truncation-notice');
    expect(notice.exists()).toBe(true);
    expect(notice.text()).toContain('6,000');
  });

  it('renders_NodeList_WithBorderColorMatchingNodeColorMap', () => {
    const nodes = ['node-alpha', 'node-beta'];
    const summary = makeSummary({ participatingNodes: nodes });
    const colorMap = buildNodeColorMap(nodes);

    const wrapper = mount(TraceSummaryPanel, { props: { summary } });

    const nodeEls = wrapper.findAll('.trace-summary__node');
    expect(nodeEls).toHaveLength(2);

    nodeEls.forEach((el, i) => {
      const expectedColor = colorMap.get(nodes[i]) ?? '';
      const style = el.attributes('style') ?? '';
      // border-color style should contain the expected color
      expect(style).toContain(expectedColor);
    });
  });
});
```

### 12.6 — `tests/unit/TraceSearchInput.spec.ts`

```typescript
import { describe, it, expect, beforeEach, vi } from 'vitest';
import { mount, flushPromises } from '@vue/test-utils';
import { createRouter, createMemoryHistory } from 'vue-router';
import TraceSearchInput from '@/components/TraceSearchInput.vue';

function makeRouter() {
  return createRouter({
    history: createMemoryHistory(),
    routes: [{ path: '/', component: { template: '<div/>' } }],
  });
}

describe('TraceSearchInput', () => {
  let router: ReturnType<typeof makeRouter>;

  beforeEach(() => {
    router = makeRouter();
  });

  it('submit_WithValidEventHex_NavigatesToCausalByEventRoute', async () => {
    const pushSpy = vi.spyOn(router, 'push');

    const wrapper = mount(TraceSearchInput, {
      global: { plugins: [router] },
    });

    // Set kind to 'event' (it's the default, but be explicit)
    await wrapper.find('select').setValue('event');
    await wrapper.find('input').setValue('aabbccddeeff0011');
    await wrapper.find('form').trigger('submit');
    await flushPromises();

    expect(pushSpy).toHaveBeenCalledWith({
      name: 'causal-by-event',
      params: { eventId: 'aabbccddeeff0011' },
    });
  });

  it('submit_WithNonHexValue_DisplaysValidationError', async () => {
    const pushSpy = vi.spyOn(router, 'push');

    const wrapper = mount(TraceSearchInput, {
      global: { plugins: [router] },
    });

    await wrapper.find('input').setValue('zzzzzzzzzzzzzzzz');
    await wrapper.find('form').trigger('submit');

    expect(wrapper.find('.trace-search__error').exists()).toBe(true);
    expect(pushSpy).not.toHaveBeenCalled();
  });
});
```

---

## Critical notes

1. **`useCausalTreeQuery` import style**: `vi.mock('@/api/tracerApiClient', ...)` must be at the top level of the test file (Vitest hoists mocks). Then import `useCausalTreeQuery` statically. The `require()` approach may fail with ESM.

2. **AbortError test (SC7)**: The `MockAbortController` approach intercepts all `new AbortController()` calls. After the test, restore `globalThis.AbortController`. Make sure to call `firstResolve(makeMinimalTree())` before `flushPromises()` at the end to clean up the pending promise.

3. **`TraceSummaryPanel` truncation test**: the template shows `6,000` (with locale comma). Use `toContain('6,000')` OR `toContain('6000')` — check what Node's `toLocaleString()` returns for `6000` in the test environment. It's safest to check that the number appears in the text somewhere: `notice.text().replace(/,/g, '').includes('6000')` if locale comma causes issues. Actually, just check `notice.text()).toContain('6')` would be too loose. Use a regex: `expect(notice.text()).toMatch(/6[,.]?000/)`.

4. **`useCausalTreeLayout` composable**: The `watchEffect` reactive dependency on `store.tree` may need `nextTick()` after setting `store.tree` for the effect to re-run. Use `await nextTick()` before the assertion.

5. **No `useCausalTreeUrl`**: That composable is part of TRC-P6-010 (BATCH-33). `CausalTreeView.vue` does NOT import `useCausalTreeUrl` in this batch. The URL binding happens in a separate composable in the next batch.

6. **Router registration**: After adding routes to `router/index.ts`, the existing tests should still pass. The new routes use lazy-loaded `() => import(...)` syntax, which is correct.

7. **`ErrorMessage` stub in CausalTreeView tests**: The stub needs to emit `retry` when the Retry button is clicked. The stub template above does this correctly: `@click="$emit('retry')"`.

8. **`LoadingSpinner` stub**: Must render an element with class `loading-spinner` so the test `wrapper.find('.loading-spinner').exists()` works.

---

## Completion Checklist

After implementation, run:
```powershell
cd d:\Work\Tracer\tracer-viewer ; npx vitest run
```

Expected: all 127 + 18 = **145 tests passing**, 0 failures.

Report back with:
- The test result summary
- List of all files created/modified
- Any deviations from the instructions and why
