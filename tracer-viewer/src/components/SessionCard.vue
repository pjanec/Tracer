<script setup lang="ts">
import type { SessionDto } from '@/api/tracerApiClient';
import { formatTime } from '@/utils/time';

defineProps<{ session: SessionDto }>();
</script>

<template>
  <article class="session-card">
    <header class="session-card__header">
      <span class="session-card__scenario">{{ session.scenarioId }}</span>
      <span
        class="session-card__status"
        :class="`session-card__status--${session.status.toLowerCase()}`"
      >
        {{ session.status }}
      </span>
    </header>
    <div class="session-card__meta">
      <span
        v-if="session.label"
        class="session-card__label"
      >{{ session.label }}</span>
      <span class="session-card__time">{{ formatTime(session.startUtc) }}</span>
    </div>
    <footer class="session-card__footer">
      <span>{{ session.eventCount.toLocaleString() }} events</span>
      <span>{{ session.participatingNodes.length }} node(s)</span>
    </footer>
  </article>
</template>

<style lang="scss">
.session-card {
  background: var(--c-bg-surface);
  border-radius: 8px;
  padding: 1.25rem;
  cursor: pointer;
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
  border: 1px solid transparent;
  transition: border-color 150ms ease;

  &:hover {
    border-color: var(--c-accent);
  }

  &__header {
    display: flex;
    justify-content: space-between;
    align-items: center;
  }

  &__scenario {
    font-weight: 600;
    font-size: 1rem;
  }

  &__status {
    font-size: 0.75rem;
    padding: 0.125rem 0.5rem;
    border-radius: 999px;
    background: var(--c-bg-subtle);

    &--active { color: var(--c-success); }
    &--completed { color: var(--c-text-muted); }
  }

  &__meta {
    display: flex;
    flex-direction: column;
    gap: 0.25rem;
    font-size: 0.875rem;
    color: var(--c-text-muted);
  }

  &__footer {
    display: flex;
    gap: 1rem;
    font-size: 0.8125rem;
    color: var(--c-text-muted);
  }
}
</style>
