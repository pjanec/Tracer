<template>
  <div class="timeline-toolbar">
    <button data-zoom="5m" @click="zoom5m">5m</button>
    <button data-zoom="1h" @click="zoom1h">1h</button>
    <button data-zoom="full" @click="zoomFull">Full session</button>
    <button
      class="toolbar__follow"
      :class="{ 'toolbar__follow--active': store.viewport.followLive }"
      :disabled="!store.isLiveSession"
      @click="toggleFollow"
    >{{ store.viewport.followLive ? 'Following live' : 'Follow' }}</button>
    <DensityIndicator />
  </div>
</template>

<script setup lang="ts">
import { useTimelineStore } from '@/stores/timelineStore';
import DensityIndicator from './DensityIndicator.vue';

const store = useTimelineStore();

function _midpointMs(): number {
  return (store.viewport.from.getTime() + store.viewport.to.getTime()) / 2;
}

function zoom5m() {
  const center = _midpointMs();
  const span5m = 5 * 60 * 1000;
  const currentSpan = store.viewportSpanMs;
  if (currentSpan === 0) return;
  store.zoomBy(span5m / currentSpan, center);
}

function zoom1h() {
  const center = _midpointMs();
  const span1h = 60 * 60 * 1000;
  const currentSpan = store.viewportSpanMs;
  if (currentSpan === 0) return;
  store.zoomBy(span1h / currentSpan, center);
}

function zoomFull() {
  // Full session zoom — in future will use session bounds; for now zoom out to 24h
  const center = _midpointMs();
  const span24h = 24 * 60 * 60 * 1000;
  const currentSpan = store.viewportSpanMs;
  if (currentSpan === 0) return;
  store.zoomBy(span24h / currentSpan, center);
}

function toggleFollow() {
  store.setFollowLive(!store.viewport.followLive);
}
</script>

<style scoped>
.timeline-toolbar {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 4px 8px;
  background: #1e1e2e;
  border-bottom: 1px solid #313244;
}

.toolbar__follow {
  padding: 2px 8px;
}

.toolbar__follow--active {
  background: #a6e3a1;
  color: #1e1e2e;
}

.toolbar__follow:disabled {
  opacity: 0.4;
  cursor: not-allowed;
}
</style>
