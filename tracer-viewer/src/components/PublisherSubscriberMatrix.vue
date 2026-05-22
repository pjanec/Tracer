<template>
  <div class="publisher-subscriber-matrix">
    <h3 class="publisher-subscriber-matrix__heading">Worst legs (by p99)</h3>
    <ul
      class="publisher-subscriber-matrix__list"
      style="max-height: 70vh; overflow-y: auto; list-style: none; padding: 0; margin: 0"
    >
      <li
        v-for="(pair, i) in pairs"
        :key="i"
        class="pair-matrix__row"
        :class="{
          'pair-matrix__row--over-budget': isOverBudget(pair),
          'pair-matrix__row--selected': isSelected(pair),
        }"
        @click="emit('select', pair)"
      >
        <span class="publisher-subscriber-matrix__topic">{{ pair.topic }}</span>
        <span class="publisher-subscriber-matrix__path">
          {{ pair.publisherNode }} → {{ pair.subscriberNode }}
        </span>
        <span class="publisher-subscriber-matrix__p99">{{ pair.p99Ms.toFixed(1) }} ms</span>
        <span class="publisher-subscriber-matrix__count">n={{ pair.sampleCount }}</span>
      </li>
    </ul>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue';
import type { LatencyPairSummaryDto, LatencyBudgetDto } from '@/api/tracerApiClient';

const props = defineProps<{
  pairs: LatencyPairSummaryDto[];
  budgets: LatencyBudgetDto[];
  selectedPair: LatencyPairSummaryDto | null;
}>();

const emit = defineEmits<{
  (e: 'select', pair: LatencyPairSummaryDto): void;
}>();

const budgetByTopic = computed(() => {
  const map = new Map<string, LatencyBudgetDto>();
  for (const b of props.budgets) map.set(b.topic, b);
  return map;
});

function isOverBudget(pair: LatencyPairSummaryDto): boolean {
  const b = budgetByTopic.value.get(pair.topic);
  if (!b || b.p99BudgetMs == null) return false;
  return pair.p99Ms > b.p99BudgetMs;
}

function isSelected(pair: LatencyPairSummaryDto): boolean {
  if (!props.selectedPair) return false;
  return (
    props.selectedPair.topic === pair.topic &&
    props.selectedPair.publisherNode === pair.publisherNode &&
    props.selectedPair.subscriberNode === pair.subscriberNode
  );
}
</script>

<style lang="scss">
.publisher-subscriber-matrix {
  &__heading {
    font-size: 0.95rem;
    font-weight: 600;
    margin-bottom: 0.5rem;
  }
}

.pair-matrix__row {
  display: flex;
  flex-direction: column;
  gap: 0.1rem;
  padding: 0.45rem 0.6rem;
  border-radius: 4px;
  cursor: pointer;
  border-bottom: 1px solid var(--c-bg-subtle, #333);

  &:hover {
    background: var(--c-hover-bg, rgba(255,255,255,0.05));
  }

  &--over-budget {
    background: rgba(232, 92, 92, 0.12);
    border-left: 3px solid #e85c5c;
  }

  &--selected {
    background: rgba(91, 157, 255, 0.18);
    border-left: 3px solid #5b9dff;
  }
}

.publisher-subscriber-matrix {
  &__topic {
    font-family: monospace;
    font-size: 0.85rem;
    color: var(--c-text-muted, #888);
  }

  &__path {
    font-size: 0.88rem;
  }

  &__p99 {
    font-weight: 600;
    font-size: 0.88rem;
  }

  &__count {
    font-size: 0.8rem;
    color: var(--c-text-muted, #888);
  }
}
</style>
