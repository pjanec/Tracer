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
