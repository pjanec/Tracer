<template>
  <svg class="timeline-axis" :viewBox="`0 0 ${width} 32`" preserveAspectRatio="none">
    <text
      v-for="tick in ticks"
      :key="tick.ms"
      :x="tick.x"
      y="20"
      class="timeline-axis__label"
    >{{ tick.label }}</text>
  </svg>
</template>

<script setup lang="ts">
import { computed, ref, onMounted, onUnmounted } from 'vue';
import { useTimelineStore } from '@/stores/timelineStore';

const store = useTimelineStore();

const width = ref(800);

// Resize observer to track actual width
let ro: ResizeObserver | null = null;

onMounted(() => {
  if (typeof ResizeObserver !== 'undefined') {
    ro = new ResizeObserver((entries) => {
      const w = entries[0]?.contentRect.width;
      if (w) width.value = w;
    });
  }
});

onUnmounted(() => {
  ro?.disconnect();
});

interface Tick {
  ms: number;
  x: number;
  label: string;
}

const ticks = computed<Tick[]>(() => {
  const fromMs = store.viewport.from.getTime();
  const toMs   = store.viewport.to.getTime();
  const spanMs = toMs - fromMs;
  if (spanMs <= 0) return [];

  const w = width.value;
  const MIN_TICKS = 5;
  const MAX_TICKS = 12;

  // Choose tick interval
  const rawInterval = spanMs / MAX_TICKS;
  const interval = _niceTick(rawInterval);

  const result: Tick[] = [];
  const first = Math.ceil(fromMs / interval) * interval;

  for (let ms = first; ms <= toMs; ms += interval) {
    const x = ((ms - fromMs) / spanMs) * w;
    result.push({ ms, x, label: _formatTick(ms, spanMs) });
    if (result.length >= MAX_TICKS) break;
  }

  // Ensure minimum tick count
  if (result.length < MIN_TICKS) {
    const step = spanMs / MIN_TICKS;
    const fallback: Tick[] = [];
    for (let i = 0; i <= MIN_TICKS; i++) {
      const ms = fromMs + i * step;
      const x = (i / MIN_TICKS) * w;
      fallback.push({ ms, x, label: _formatTick(ms, spanMs) });
    }
    return fallback;
  }

  return result;
});

function _niceTick(raw: number): number {
  const niceValues = [
    100, 250, 500, 1000, 2000, 5000, 10_000, 30_000, 60_000,
    5 * 60_000, 10 * 60_000, 30 * 60_000, 60 * 60_000,
  ];
  return niceValues.find((v) => v >= raw) ?? niceValues[niceValues.length - 1];
}

function _formatTick(ms: number, spanMs: number): string {
  const d = new Date(ms);
  const h  = d.getUTCHours().toString().padStart(2, '0');
  const m  = d.getUTCMinutes().toString().padStart(2, '0');
  const s  = d.getUTCSeconds().toString().padStart(2, '0');
  const ms3 = d.getUTCMilliseconds().toString().padStart(3, '0');

  if (spanMs < 60_000) return `.${ms3}`;
  if (spanMs < 3_600_000) return `${h}:${m}:${s}`;
  return `${h}:${m}`;
}
</script>

<style scoped>
.timeline-axis {
  width: 100%;
  height: 32px;
  display: block;
  background: #13131f;
}

.timeline-axis__label {
  font-size: 10px;
  fill: #888;
  font-family: monospace;
  text-anchor: middle;
}
</style>
