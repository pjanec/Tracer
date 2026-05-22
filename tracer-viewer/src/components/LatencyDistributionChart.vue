<template>
  <div class="latency-distribution-chart">
    <div v-if="loading" class="latency-distribution-chart__loading">Loading…</div>
    <canvas ref="canvasEl" class="latency-distribution-chart__canvas" />
  </div>
</template>

<script setup lang="ts">
import { ref, watch, onMounted, onBeforeUnmount } from 'vue';
import type { LatencyDistributionDto, LatencyBudgetDto } from '@/api/tracerApiClient';
import { renderHistogram } from '@/rendering/histogramRenderer';

const props = defineProps<{
  distribution: LatencyDistributionDto | null;
  budget: LatencyBudgetDto | null;
  loading: boolean;
}>();

const canvasEl = ref<HTMLCanvasElement | null>(null);
let resizeObserver: ResizeObserver | null = null;

function draw() {
  const canvas = canvasEl.value;
  if (!canvas) return;
  const ctx = canvas.getContext('2d');
  if (!ctx) return;
  if (!props.distribution) {
    ctx.clearRect(0, 0, canvas.width, canvas.height);
    return;
  }
  renderHistogram(ctx, {
    distribution: props.distribution,
    budget: props.budget,
    canvasWidth: canvas.width,
    canvasHeight: canvas.height,
  });
}

function resize(entry?: ResizeObserverEntry) {
  const canvas = canvasEl.value;
  if (!canvas) return;
  const w = entry ? Math.round(entry.contentRect.width) : canvas.clientWidth;
  const h = entry ? Math.round(entry.contentRect.height) : canvas.clientHeight;
  if (w > 0) canvas.width = w;
  if (h > 0) canvas.height = h;
  draw();
}

onMounted(() => {
  if (canvasEl.value) {
    resizeObserver = new ResizeObserver(entries => {
      if (entries[0]) resize(entries[0]);
    });
    resizeObserver.observe(canvasEl.value);
    resize();
  }
});

onBeforeUnmount(() => {
  resizeObserver?.disconnect();
});

watch(() => [props.distribution, props.budget], draw, { deep: true });
</script>

<style lang="scss">
.latency-distribution-chart {
  position: relative;
  width: 100%;
  height: 200px;

  &__canvas {
    display: block;
    width: 100%;
    height: 100%;
  }

  &__loading {
    position: absolute;
    inset: 0;
    display: flex;
    align-items: center;
    justify-content: center;
    color: var(--c-text-muted, #888);
    font-size: 0.9rem;
  }
}
</style>
