<script setup lang="ts">
import { computed } from 'vue';
import { useLiveStore } from '@/stores/liveStore';

const liveStore = useLiveStore();
const stale = computed(() => {
  if (!liveStore.connection.lastEventAt) return false;
  return Date.now() - liveStore.connection.lastEventAt.getTime() > 30_000;
});
const status = computed(() => {
  if (!liveStore.connection.connected) return 'disconnected';
  if (stale.value) return 'stale';
  return 'live';
});
</script>

<template>
  <div
    class="live-indicator"
    :class="`live-indicator--${status}`"
  >
    <span class="live-indicator__dot" />
    <span class="live-indicator__label">
      {{ status === 'live' ? 'Live' : status === 'stale' ? 'Quiet' : 'Disconnected' }}
    </span>
  </div>
</template>

<style lang="scss">
.live-indicator {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.25rem 0.75rem;
  border-radius: 999px;
  background: var(--c-bg-subtle);
  font-size: 0.875rem;

  &__dot {
    width: 8px;
    height: 8px;
    border-radius: 50%;
  }

  &--live .live-indicator__dot {
    background: var(--c-success);
    animation: pulse 2s infinite;
  }
  &--stale .live-indicator__dot { background: var(--c-warning); }
  &--disconnected .live-indicator__dot { background: var(--c-danger); }
}

@keyframes pulse {
  0%, 100% { opacity: 1; }
  50% { opacity: 0.4; }
}
</style>
