<script setup lang="ts">
import type { NotableEventDto } from '@/api/tracerApiClient';
import { formatTime } from '@/utils/time';

defineProps<{ event: NotableEventDto }>();
</script>

<template>
  <article class="notable-event-card">
    <span class="notable-event-card__label">{{ event.notableLabel }}</span>
    <span class="notable-event-card__type">{{ event.topic }}</span>
    <span class="notable-event-card__time">{{ formatTime(event.occurredAtUtc) }}</span>
  </article>
</template>

<style lang="scss">
.notable-event-card {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  padding: 0.625rem 0.875rem;
  background: var(--c-bg-subtle);
  border-radius: 6px;
  font-size: 0.875rem;

  &__label {
    font-weight: 600;
    flex: 1;
  }

  &__type {
    font-family: var(--font-mono, monospace);
    color: var(--c-text-muted);
    font-size: 0.8125rem;
  }

  &__time {
    color: var(--c-text-muted);
    font-size: 0.75rem;
    white-space: nowrap;
  }
}
</style>
