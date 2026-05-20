<!-- src/components/TraceNodeTooltip.vue -->
<script setup lang="ts">
import type { TraceNodeDto } from '@/types/causalTree';

defineProps<{
  node: TraceNodeDto;
  x: number;
  y: number;
}>();
</script>

<template>
  <div
    class="trace-node-tooltip"
    :style="{ left: `${x}px`, top: `${y}px` }"
  >
    <div class="trace-node-tooltip__topic">{{ node.topic }}</div>
    <div class="trace-node-tooltip__node">{{ node.publisherNode }}</div>
    <div class="trace-node-tooltip__time">{{ node.publishWallclock }}</div>
  </div>
</template>

<style scoped>
.trace-node-tooltip {
  position: absolute;
  background: var(--c-bg-surface, #1e1e2e);
  border: 1px solid var(--c-bg-subtle, #252538);
  border-radius: 6px;
  padding: 0.5rem 0.75rem;
  font-size: 0.8125rem;
  pointer-events: none;
  z-index: 100;
  max-width: 260px;
}
.trace-node-tooltip__topic {
  font-weight: 600;
  margin-bottom: 0.25rem;
}
.trace-node-tooltip__node {
  color: var(--c-text-muted, #888);
  font-family: var(--font-mono, monospace);
  font-size: 0.75rem;
}
.trace-node-tooltip__time {
  color: var(--c-text-muted, #888);
  font-size: 0.75rem;
  margin-top: 0.25rem;
}
</style>
