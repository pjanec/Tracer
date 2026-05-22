<script setup lang="ts">
import { ref, computed, watch, watchEffect, onUnmounted } from 'vue';
import type { SlowStateSampleDto } from '@/api/tracerApiClient';
import {
  detectFields,
  renderNumericLine,
  renderCategoricalBands,
  type SlowStateSample,
} from '@/rendering/slowStateChartRenderer';
import { useResizeObserver } from '@/composables/useResizeObserver';

const props = defineProps<{
  topic: string;
  samples: SlowStateSampleDto[];
  timeRange: { from: Date; to: Date };
}>();

const emit = defineEmits<{
  'select-event': [sample: SlowStateSampleDto];
}>();

const canvasRef = ref<HTMLCanvasElement | null>(null);
const selectedField = ref<string | null>(null);

const detectedFields = computed(() => detectFields(props.samples.map(s => s.payloadJson)));
const fieldOptions = computed(() => detectedFields.value);

/** Auto-select first detected field when samples change (new entity loaded). */
watch(
  () => props.samples,
  () => {
    selectedField.value = detectedFields.value[0]?.name ?? null;
  },
  { immediate: true },
);

let rafId: number | null = null;

function scheduleRender() {
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

    const field = selectedField.value;
    if (!field || props.samples.length === 0) {
      ctx.clearRect(0, 0, width, height);
      return;
    }

    // Convert DTO samples to SlowStateSample[] for the selected field
    const chartSamples: SlowStateSample[] = [];
    for (const s of props.samples) {
      let parsed: Record<string, unknown> | null = null;
      try {
        const p = JSON.parse(s.payloadJson) as unknown;
        if (typeof p === 'object' && p !== null) {
          parsed = p as Record<string, unknown>;
        }
      } catch {
        // skip malformed JSON
      }
      if (!parsed || !(field in parsed)) continue;
      chartSamples.push({
        t: new Date(s.occurredAtUtc).getTime(),
        value: parsed[field],
      });
    }

    const detectedField = detectedFields.value.find(f => f.name === field);
    const kind = detectedField?.kind ?? 'categorical';

    const renderInput = {
      ctx,
      width,
      height,
      fromMs: props.timeRange.from.getTime(),
      toMs: props.timeRange.to.getTime(),
      samples: chartSamples,
      kind,
    };

    if (kind === 'numeric') {
      renderNumericLine(renderInput);
    } else {
      renderCategoricalBands(renderInput);
    }
  });
}

watchEffect(() => {
  void props.samples;
  void props.timeRange;
  void selectedField.value;
  void canvasRef.value;
  scheduleRender();
});

useResizeObserver(canvasRef, () => scheduleRender());

onUnmounted(() => {
  if (rafId !== null) {
    cancelAnimationFrame(rafId);
    rafId = null;
  }
});

function onCanvasClick(e: MouseEvent) {
  const canvas = canvasRef.value;
  if (!canvas || props.samples.length === 0) return;

  const rect = canvas.getBoundingClientRect();
  const x = e.clientX - rect.left;
  const width = rect.width;
  const fromMs = props.timeRange.from.getTime();
  const toMs = props.timeRange.to.getTime();
  const range = toMs - fromMs;

  let closest: SlowStateSampleDto | null = null;
  let minDist = Infinity;

  for (const s of props.samples) {
    const t = new Date(s.occurredAtUtc).getTime();
    const sx = range === 0 ? 0 : ((t - fromMs) / range) * width;
    const dist = Math.abs(sx - x);
    if (dist < minDist) {
      minDist = dist;
      closest = s;
    }
  }

  if (closest !== null && minDist <= 10) {
    emit('select-event', closest);
  }
}
</script>

<template>
  <div class="slow-state-chart">
    <div class="slow-state-chart__header">
      <span class="slow-state-chart__topic">{{ topic }}</span>
      <select
        v-if="detectedFields.length > 1"
        v-model="selectedField"
        class="slow-state-chart__field-select"
      >
        <option v-for="f in fieldOptions" :key="f.name" :value="f.name">{{ f.name }}</option>
      </select>
    </div>
    <canvas ref="canvasRef" class="slow-state-chart__canvas" @click="onCanvasClick" />
  </div>
</template>

<style scoped>
.slow-state-chart__canvas {
  width: 100%;
  height: 60px;
  display: block;
  cursor: crosshair;
}
</style>
