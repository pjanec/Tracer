<!-- src/components/CausalNodeInspector.vue -->
<script setup lang="ts">
import type { TraceNodeDto } from '@/types/causalTree';

defineProps<{ event: TraceNodeDto }>();
</script>

<template>
  <section class="causal-node-inspector">
    <div class="causal-node-inspector__row">
      <span class="causal-node-inspector__label">Topic</span>
      <span class="causal-node-inspector__value">{{ event.topic }}</span>
    </div>
    <div class="causal-node-inspector__row">
      <span class="causal-node-inspector__label">Node</span>
      <span class="causal-node-inspector__value">{{ event.publisherNode }}</span>
    </div>
    <div class="causal-node-inspector__row">
      <span class="causal-node-inspector__label">Event ID</span>
      <span class="causal-node-inspector__value causal-node-inspector__value--mono">{{ event.eventId }}</span>
    </div>
    <div class="causal-node-inspector__row">
      <span class="causal-node-inspector__label">Time</span>
      <span class="causal-node-inspector__value">{{ event.publishWallclock }}</span>
    </div>
    <div v-if="event.notableLabel" class="causal-node-inspector__row">
      <span class="causal-node-inspector__label">Notable</span>
      <span class="causal-node-inspector__value">{{ event.notableLabel }}</span>
    </div>
  </section>
</template>

<style scoped>
.causal-node-inspector {
  padding: 1rem;
  background: var(--c-bg-surface, #1e1e2e);
  border-radius: 8px;
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}
.causal-node-inspector__row {
  display: flex;
  gap: 0.75rem;
}
.causal-node-inspector__label {
  font-size: 0.75rem;
  color: var(--c-text-muted, #888);
  text-transform: uppercase;
  min-width: 5rem;
}
.causal-node-inspector__value {
  font-size: 0.875rem;
}
.causal-node-inspector__value--mono {
  font-family: var(--font-mono, monospace);
  word-break: break-all;
}
</style>
