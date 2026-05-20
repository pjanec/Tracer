<!-- src/components/TraceSummaryPanel.vue -->
<script setup lang="ts">
import { computed } from 'vue';
import type { TraceSummaryDto } from '@/types/causalTree';
import { buildNodeColorMap } from '@/rendering/colorScheme';

const props = defineProps<{ summary: TraceSummaryDto }>();

const nodeColors = computed(() => buildNodeColorMap(props.summary.participatingNodes));

const spanDisplay = computed(() => formatMs(props.summary.totalSpanMs));

function formatMs(ms: number): string {
  if (ms < 1) return `${(ms * 1000).toFixed(0)}μs`;
  if (ms < 1000) return `${ms.toFixed(0)}ms`;
  if (ms < 60000) return `${(ms / 1000).toFixed(2)}s`;
  return `${(ms / 60000).toFixed(1)}min`;
}
</script>

<template>
  <section class="trace-summary">
    <div class="trace-summary__field">
      <div class="trace-summary__label">Trace ID</div>
      <div class="trace-summary__value trace-summary__value--mono">
        {{ summary.traceId }}
      </div>
    </div>

    <div class="trace-summary__row">
      <div class="trace-summary__field">
        <div class="trace-summary__label">Events</div>
        <div class="trace-summary__value">
          {{ summary.totalEvents.toLocaleString() }}
          <span v-if="summary.truncated" class="trace-summary__warn">
            (of {{ summary.totalEventsAvailable?.toLocaleString() ?? 'many' }})
          </span>
        </div>
      </div>
      <div class="trace-summary__field">
        <div class="trace-summary__label">Span</div>
        <div class="trace-summary__value">{{ spanDisplay }}</div>
      </div>
    </div>

    <div class="trace-summary__row">
      <div class="trace-summary__field">
        <div class="trace-summary__label">Roots</div>
        <div class="trace-summary__value">{{ summary.rootCount }}</div>
      </div>
      <div class="trace-summary__field">
        <div class="trace-summary__label">Leaves</div>
        <div class="trace-summary__value">{{ summary.leafCount }}</div>
      </div>
    </div>

    <div class="trace-summary__field">
      <div class="trace-summary__label">
        Nodes ({{ summary.participatingNodes.length }})
      </div>
      <div class="trace-summary__nodes">
        <span
          v-for="node in summary.participatingNodes"
          :key="node"
          class="trace-summary__node"
          :style="{ borderColor: nodeColors.get(node) }"
        >
          {{ node }}
        </span>
      </div>
    </div>

    <div v-if="summary.truncated" class="trace-summary__truncation-notice">
      This trace was truncated. Showing {{ summary.totalEvents.toLocaleString() }} of
      {{ summary.totalEventsAvailable?.toLocaleString() ?? 'many' }} events.
    </div>
  </section>
</template>

<style scoped>
.trace-summary {
  background: var(--c-bg-surface, #1e1e2e);
  border-radius: 12px;
  padding: 1.5rem;
  display: flex;
  flex-direction: column;
  gap: 1rem;
}
.trace-summary__row {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 1rem;
}
.trace-summary__label {
  font-size: 0.75rem;
  color: var(--c-text-muted, #888);
  text-transform: uppercase;
  letter-spacing: 0.05em;
  margin-bottom: 0.25rem;
}
.trace-summary__value {
  font-size: 1.25rem;
  font-weight: 500;
}
.trace-summary__value--mono {
  font-family: var(--font-mono, monospace);
  font-size: 0.875rem;
  word-break: break-all;
}
.trace-summary__warn {
  color: var(--c-warning, #e8b048);
  font-size: 0.875rem;
  margin-left: 0.5rem;
}
.trace-summary__nodes {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
}
.trace-summary__node {
  padding: 0.25rem 0.5rem;
  background: var(--c-bg-subtle, #252538);
  border-left: 3px solid;
  border-radius: 4px;
  font-size: 0.875rem;
  font-family: var(--font-mono, monospace);
}
.trace-summary__truncation-notice {
  padding: 0.75rem;
  background: rgba(232, 176, 72, 0.1);
  border: 1px solid var(--c-warning, #e8b048);
  border-radius: 6px;
  font-size: 0.875rem;
  color: var(--c-warning, #e8b048);
}
</style>
