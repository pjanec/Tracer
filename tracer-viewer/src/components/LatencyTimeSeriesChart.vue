<template>
  <div class="latency-timeseries-chart" @mousemove="onMouseMove" @mouseleave="tooltip = null">
    <div v-if="loading" class="latency-timeseries-chart__loading">Loading…</div>
    <canvas ref="canvasEl" class="latency-timeseries-chart__canvas" />
    <div
      v-if="tooltip"
      class="latency-timeseries-chart__tooltip"
      :style="{ left: tooltip.x + 'px', top: '4px' }"
    >
      <div>{{ tooltip.label }}</div>
      <div>p50: {{ tooltip.p50 }}</div>
      <div>p99: {{ tooltip.p99 }}</div>
      <div>n={{ tooltip.n }}</div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, watch, onMounted, onBeforeUnmount } from 'vue';
import type { LatencyTimeSeriesDto } from '@/api/tracerApiClient';
import { renderTimeSeries, hitTestTimeSeries } from '@/rendering/latencyTimeSeriesRenderer';
import { formatMs } from '@/rendering/histogramRenderer';

const props = defineProps<{
  timeseries: LatencyTimeSeriesDto | null;
  loading: boolean;
}>();

const canvasEl = ref<HTMLCanvasElement | null>(null);
let resizeObserver: ResizeObserver | null = null;

interface TooltipState {
  x: number;
  label: string;
  p50: string;
  p99: string;
  n: number;
}
const tooltip = ref<TooltipState | null>(null);

function draw() {
  const canvas = canvasEl.value;
  if (!canvas) return;
  const ctx = canvas.getContext('2d');
  if (!ctx) return;
  if (!props.timeseries) {
    ctx.clearRect(0, 0, canvas.width, canvas.height);
    return;
  }
  renderTimeSeries(ctx, {
    timeseries: props.timeseries,
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

function onMouseMove(evt: MouseEvent) {
  if (!props.timeseries || !canvasEl.value) return;
  const rect = canvasEl.value.getBoundingClientRect();
  const mouseX = evt.clientX - rect.left;
  const idx = hitTestTimeSeries(props.timeseries.points, mouseX, canvasEl.value.width);
  if (idx < 0) { tooltip.value = null; return; }
  const pt = props.timeseries.points[idx];
  tooltip.value = {
    x: Math.round((idx / (props.timeseries.points.length - 1 || 1)) * canvasEl.value.clientWidth),
    label: new Date(pt.bucketStartUtc).toLocaleTimeString(),
    p50: formatMs(pt.p50Ms),
    p99: formatMs(pt.p99Ms),
    n: pt.sampleCount,
  };
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

watch(() => props.timeseries, draw, { deep: true });
</script>

<style lang="scss">
.latency-timeseries-chart {
  position: relative;
  width: 100%;
  height: 160px;

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

  &__tooltip {
    position: absolute;
    background: rgba(30, 30, 40, 0.92);
    color: #eee;
    border-radius: 4px;
    padding: 0.35rem 0.6rem;
    font-size: 0.8rem;
    pointer-events: none;
    white-space: nowrap;
    transform: translateX(-50%);
    z-index: 10;
  }
}
</style>
