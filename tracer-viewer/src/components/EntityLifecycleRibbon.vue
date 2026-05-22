<script setup lang="ts">
import { computed } from 'vue';
import type { EntityEventsDto, EntityEventDto } from '@/api/tracerApiClient';
import { classifyLifecycleEvent } from '@/utils/lifecycleClassifier';
import type { LifecycleKind } from '@/utils/lifecycleClassifier';

const props = defineProps<{
  events: EntityEventsDto;
  timeRange: { from: Date; to: Date };
}>();

const BAND_PALETTE = ['#4a9eff', '#22c55e', '#f59e0b', '#8b5cf6', '#ec4899'];

const MARKER_COLORS: Record<LifecycleKind, string> = {
  spawn: '#22c55e',
  ownership: '#4a9eff',
  destruction: '#ef4444',
};

function clamp(v: number, min: number, max: number): number {
  return Math.max(min, Math.min(max, v));
}

function xPct(event: EntityEventDto): number {
  const t = new Date(event.occurredAtUtc).getTime();
  const from = props.timeRange.from.getTime();
  const to = props.timeRange.to.getTime();
  if (to === from) return 0;
  return clamp(((t - from) / (to - from)) * 100, 0, 100);
}

/** All lifecycle events sorted by time ascending. */
const lifecycleEvents = computed(() =>
  props.events.events
    .filter(e => classifyLifecycleEvent(e.topic) !== null)
    .sort(
      (a, b) =>
        new Date(a.occurredAtUtc).getTime() - new Date(b.occurredAtUtc).getTime(),
    ),
);

/**
 * Ownership/spawn bands: one band per spawn or ownership event, extending to the next
 * spawn/ownership transition or the right edge if none.
 */
const ownershipBands = computed(() => {
  const bandEvents = lifecycleEvents.value.filter(e => {
    const k = classifyLifecycleEvent(e.topic);
    return k === 'spawn' || k === 'ownership';
  });

  return bandEvents.map((e, i) => {
    const startPct = xPct(e);
    const endPct = i + 1 < bandEvents.length ? xPct(bandEvents[i + 1]) : 100;
    return {
      startPct,
      endPct,
      color: BAND_PALETTE[i % BAND_PALETTE.length],
    };
  });
});
</script>

<template>
  <div class="entity-lifecycle-ribbon">
    <div class="entity-lifecycle-ribbon__track" />
    <div
      v-for="(band, i) in ownershipBands"
      :key="`band-${i}`"
      class="entity-lifecycle-ribbon__ownership-band"
      :style="{
        left: `${band.startPct}%`,
        width: `${band.endPct - band.startPct}%`,
        backgroundColor: band.color,
      }"
    />
    <div
      v-for="event in lifecycleEvents"
      :key="event.eventId"
      :class="[
        'entity-lifecycle-ribbon__marker',
        `entity-lifecycle-ribbon__marker--${classifyLifecycleEvent(event.topic)}`,
      ]"
      :style="{
        left: `${xPct(event)}%`,
        backgroundColor: MARKER_COLORS[classifyLifecycleEvent(event.topic) as LifecycleKind],
      }"
      :title="`${classifyLifecycleEvent(event.topic)} @ ${new Date(event.occurredAtUtc).toISOString()}`"
    />
  </div>
</template>

<style scoped>
.entity-lifecycle-ribbon {
  position: relative;
  height: 28px;
  overflow: hidden;
}

.entity-lifecycle-ribbon__track {
  position: absolute;
  left: 0;
  right: 0;
  top: 0;
  bottom: 0;
  background: rgba(255, 255, 255, 0.05);
}

.entity-lifecycle-ribbon__ownership-band {
  position: absolute;
  height: 100%;
  opacity: 0.4;
}

.entity-lifecycle-ribbon__marker {
  position: absolute;
  width: 2px;
  height: 100%;
  cursor: pointer;
}
</style>
