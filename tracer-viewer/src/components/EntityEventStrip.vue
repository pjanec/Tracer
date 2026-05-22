<script setup lang="ts">
import { ref, watchEffect, onUnmounted } from 'vue';
import type { EntityEventsDto, AnnotationDto } from '@/api/tracerApiClient';
import { renderEventStrip, type EventStripHitEntry } from '@/rendering/eventStripRenderer';
import { useResizeObserver } from '@/composables/useResizeObserver';
import AnnotationMarker from '@/components/AnnotationMarker.vue';

const props = defineProps<{
  events: EntityEventsDto;
  timeRange: { from: Date; to: Date };
  selectedEventId: string | null;
}>();

const emit = defineEmits<{
  select: [eventId: string | null];
  'annotation-edit': [annotation: AnnotationDto];
}>();

const canvasRef = ref<HTMLCanvasElement | null>(null);
const hitEntries = ref<EventStripHitEntry[]>([]);
const THRESHOLD_PX = 8;

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

    hitEntries.value = renderEventStrip(ctx, {
      width,
      height,
      fromMs: props.timeRange.from.getTime(),
      toMs: props.timeRange.to.getTime(),
      events: props.events.events,
      selectedEventId: props.selectedEventId,
    });
  });
}

watchEffect(() => {
  void props.events;
  void props.timeRange;
  void props.selectedEventId;
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

function onClick(e: MouseEvent) {
  const canvas = canvasRef.value;
  if (!canvas) return;
  const rect = canvas.getBoundingClientRect();
  const x = e.clientX - rect.left;

  const sorted = [...hitEntries.value].sort(
    (a, b) => Math.abs(a.x - x) - Math.abs(b.x - x),
  );

  if (sorted.length > 0 && Math.abs(sorted[0].x - x) < THRESHOLD_PX) {
    emit('select', sorted[0].eventId);
  } else {
    emit('select', null);
  }
}
</script>

<template>
  <div class="entity-event-strip">
    <div class="entity-event-strip__header">
      <span>Events</span>
      <span v-if="events.truncated" class="entity-event-strip__truncated">
        (truncated — showing first {{ events.events.length }} events)
      </span>
    </div>
    <canvas ref="canvasRef" class="entity-event-strip__canvas" @click="onClick" />
    <div class="entity-event-strip__annotation-overlay">
      <AnnotationMarker
        v-for="event in events.events"
        :key="`ann-${event.eventId}`"
        :event-id="event.eventId"
        @edit="$emit('annotation-edit', $event)"
      />
    </div>
  </div>
</template>

<style scoped>
.entity-event-strip__canvas {
  width: 100%;
  height: 40px;
  display: block;
  cursor: crosshair;
}
</style>
