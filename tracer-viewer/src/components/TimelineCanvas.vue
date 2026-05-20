<template>
  <canvas
    ref="canvasEl"
    class="timeline-canvas"
    @pointerdown="onPointerDown"
    @pointermove="onPointerMove"
    @pointerup="onPointerUp"
  />
</template>

<script setup lang="ts">
import { ref } from 'vue';
import { useTimelineStore } from '@/stores/timelineStore';
import { useCanvasRenderer } from '@/composables/useCanvasRenderer';

const emit = defineEmits<{
  (e: 'markerClick', eventId: string): void;
}>();

const store = useTimelineStore();
const canvasEl = ref<HTMLCanvasElement | null>(null);

const { hitIndex } = useCanvasRenderer(canvasEl);

// --- Pointer / pan tracking ---
let dragging = false;
let lastClientX = 0;

function onPointerDown(e: PointerEvent) {
  dragging = true;
  lastClientX = e.clientX;
  (e.currentTarget as HTMLCanvasElement).setPointerCapture(e.pointerId);
}

function onPointerMove(e: PointerEvent) {
  if (!dragging) return;
  const canvas = canvasEl.value;
  if (!canvas) return;

  const dx = e.clientX - lastClientX;
  lastClientX = e.clientX;

  const spanMs = store.viewportSpanMs;
  const dtMs = -(dx / canvas.clientWidth) * spanMs;
  store.panBy(dtMs);
}

function onPointerUp(e: PointerEvent) {
  if (!dragging) return;
  dragging = false;

  // Click (no significant drag) → hit-test
  const canvas = canvasEl.value;
  if (canvas && hitIndex.value) {
    const rect = canvas.getBoundingClientRect();
    const x = e.clientX - rect.left;
    const y = e.clientY - rect.top;
    const marker = hitIndex.value.findMarkerAt(x, y);
    if (marker) emit('markerClick', marker.eventId);
  }

  (e.currentTarget as HTMLCanvasElement).releasePointerCapture(e.pointerId);
}
</script>

<style scoped>
.timeline-canvas {
  display: block;
  width: 100%;
  cursor: grab;
}

.timeline-canvas:active {
  cursor: grabbing;
}
</style>
