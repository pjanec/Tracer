<script setup lang="ts">
import { ref, watchEffect, onUnmounted } from 'vue';
import type { EntityFastStateDto } from '@/api/tracerApiClient';
import { renderFastStateChart, FAST_STATE_COLORS } from '@/rendering/fastStateChartRenderer';
import { useResizeObserver } from '@/composables/useResizeObserver';

const props = defineProps<{
  data: EntityFastStateDto;
  selectedColumns: string[];
  timeRange: { from: Date; to: Date };
}>();

const canvasRef = ref<HTMLCanvasElement | null>(null);
let rafId: number | null = null;

function scheduleRender(): void {
  if (rafId !== null) cancelAnimationFrame(rafId);
  rafId = requestAnimationFrame(() => {
    rafId = null;
    const canvas = canvasRef.value;
    if (!canvas) return;
    const ctx = canvas.getContext('2d');
    if (!ctx) return;

    const dpr = window.devicePixelRatio || 1;
    const width = canvas.clientWidth;
    const height = canvas.clientHeight;

    if (
      canvas.width !== Math.round(width * dpr) ||
      canvas.height !== Math.round(height * dpr)
    ) {
      canvas.width = Math.round(width * dpr);
      canvas.height = Math.round(height * dpr);
      ctx.scale(dpr, dpr);
    }

    renderFastStateChart({
      ctx,
      width,
      height,
      fromMs: props.timeRange.from.getTime(),
      toMs: props.timeRange.to.getTime(),
      samples: props.data.samples,
      columns: props.selectedColumns,
      colors: FAST_STATE_COLORS,
    });
  });
}

watchEffect(() => {
  // Track data, selectedColumns, and timeRange for reactivity
  void props.data;
  void props.selectedColumns;
  void props.timeRange;
  scheduleRender();
});

useResizeObserver(canvasRef, () => scheduleRender());

onUnmounted(() => {
  if (rafId !== null) {
    cancelAnimationFrame(rafId);
    rafId = null;
  }
});
</script>

<template>
  <canvas
    ref="canvasRef"
    class="fast-state-chart__canvas"
  />
</template>

<style scoped>
.fast-state-chart__canvas {
  width: 100%;
  height: 120px;
  display: block;
}
</style>
