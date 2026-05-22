<template>
  <div class="network-graph-canvas" ref="containerEl">
    <canvas
      ref="canvasEl"
      class="network-graph-canvas__canvas"
      @click="onClick"
      @mousemove="onMouseMove"
      @mouseleave="hoveredNode = null"
    />
  </div>
</template>

<script setup lang="ts">
import { ref, watch, onMounted, onBeforeUnmount } from 'vue';
import { layoutGraph } from '@/rendering/networkGraphLayout';
import { renderGraph } from '@/rendering/networkGraphRenderer';
import type { LaidOutGraph } from '@/rendering/networkGraphLayout';

const props = defineProps<{
  nodes: string[];
  edges: { from: string; to: string; weight: number }[];
  selectedEdge: { from: string; to: string } | null;
}>();

const emit = defineEmits<{
  (e: 'select-edge', edge: { from: string; to: string }): void;
}>();

const canvasEl = ref<HTMLCanvasElement | null>(null);
const containerEl = ref<HTMLElement | null>(null);
let resizeObserver: ResizeObserver | null = null;
let layout: LaidOutGraph = { nodes: new Map() };
const hoveredNode = ref<string | null>(null);

const NODE_HIT_RADIUS = 22;
const EDGE_HIT_DISTANCE = 10;

function rebuildLayout() {
  const canvas = canvasEl.value;
  if (!canvas) return;
  layout = layoutGraph({
    nodes: props.nodes,
    edges: props.edges,
    canvasWidth: canvas.width,
    canvasHeight: canvas.height,
  });
  redraw();
}

function redraw() {
  const canvas = canvasEl.value;
  if (!canvas) return;
  const ctx = canvas.getContext('2d');
  if (!ctx) return;
  renderGraph(ctx, {
    layout,
    nodes: props.nodes,
    edges: props.edges,
    selectedEdge: props.selectedEdge,
    hoveredNode: hoveredNode.value,
  });
}

function resize(entry?: ResizeObserverEntry) {
  const canvas = canvasEl.value;
  if (!canvas) return;
  const w = entry ? Math.round(entry.contentRect.width) : canvas.clientWidth;
  const h = entry ? Math.round(entry.contentRect.height) : canvas.clientHeight;
  if (w > 0) canvas.width = w;
  if (h > 0) canvas.height = h;
  rebuildLayout();
}

function onClick(evt: MouseEvent) {
  const canvas = canvasEl.value;
  if (!canvas) return;
  const rect = canvas.getBoundingClientRect();
  const x = (evt.clientX - rect.left) * (canvas.width / rect.width);
  const y = (evt.clientY - rect.top) * (canvas.height / rect.height);

  // Hit-test edges (proximity to line midpoint)
  for (const edge of props.edges) {
    const from = layout.nodes.get(edge.from);
    const to = layout.nodes.get(edge.to);
    if (!from || !to) continue;
    const mx = (from.x + to.x) / 2;
    const my = (from.y + to.y) / 2;
    const dist = Math.sqrt((x - mx) ** 2 + (y - my) ** 2);
    if (dist < EDGE_HIT_DISTANCE * 3) {
      emit('select-edge', { from: edge.from, to: edge.to });
      return;
    }
  }
}

function onMouseMove(evt: MouseEvent) {
  const canvas = canvasEl.value;
  if (!canvas) return;
  const rect = canvas.getBoundingClientRect();
  const x = (evt.clientX - rect.left) * (canvas.width / rect.width);
  const y = (evt.clientY - rect.top) * (canvas.height / rect.height);
  let found: string | null = null;
  for (const node of props.nodes) {
    const pos = layout.nodes.get(node);
    if (!pos) continue;
    const dist = Math.sqrt((x - pos.x) ** 2 + (y - pos.y) ** 2);
    if (dist < NODE_HIT_RADIUS) { found = node; break; }
  }
  if (hoveredNode.value !== found) {
    hoveredNode.value = found;
    redraw();
  }
}

watch(() => [props.nodes, props.edges], rebuildLayout, { deep: true });
watch(() => [props.selectedEdge, hoveredNode.value], redraw, { deep: true });

onMounted(() => {
  if (containerEl.value) {
    resizeObserver = new ResizeObserver(entries => {
      if (entries[0]) resize(entries[0]);
    });
    resizeObserver.observe(containerEl.value);
  }
  resize();
});

onBeforeUnmount(() => {
  resizeObserver?.disconnect();
});
</script>

<style lang="scss">
.network-graph-canvas {
  width: 100%;
  height: 100%;
  min-height: 300px;

  &__canvas {
    display: block;
    width: 100%;
    height: 100%;
  }
}
</style>
