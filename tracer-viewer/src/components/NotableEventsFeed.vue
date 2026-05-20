<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { useApi } from '@/api/useApi';
import type { NotableEventDto } from '@/api/tracerApiClient';
import NotableEventCard from './NotableEventCard.vue';

const props = defineProps<{
  sessionId: string;
  liveEvents: NotableEventDto[];
}>();

const initialEvents = ref<NotableEventDto[]>([]);
const loading = ref(false);

const loadInitial = async () => {
  loading.value = true;
  try {
    const api = useApi();
    initialEvents.value = await api.getScenarioNotables(props.sessionId, 100);
  } finally {
    loading.value = false;
  }
};
watch(() => props.sessionId, loadInitial, { immediate: true });

const allEvents = computed(() => {
  const seen = new Set<string>();
  const merged: NotableEventDto[] = [];
  for (const ev of props.liveEvents) {
    if (seen.has(ev.eventId)) continue;
    seen.add(ev.eventId);
    merged.push(ev);
  }
  for (const ev of initialEvents.value) {
    if (seen.has(ev.eventId)) continue;
    seen.add(ev.eventId);
    merged.push(ev);
  }
  return merged;
});
</script>

<template>
  <section class="notables-feed">
    <header class="notables-feed__header">
      <h2>Notable events</h2>
      <span class="notables-feed__count">{{ allEvents.length }}</span>
    </header>

    <div
      v-if="loading && allEvents.length === 0"
      class="notables-feed__loading"
    >
      Loading&hellip;
    </div>
    <div
      v-else-if="allEvents.length === 0"
      class="notables-feed__empty"
    >
      No notable events yet.
    </div>
    <TransitionGroup
      v-else
      name="notable"
      tag="div"
      class="notables-feed__items"
    >
      <NotableEventCard
        v-for="ev in allEvents"
        :key="ev.eventId"
        :event="ev"
      />
    </TransitionGroup>
  </section>
</template>

<style lang="scss">
.notables-feed {
  background: var(--c-bg-surface);
  border-radius: 12px;
  padding: 1.5rem;
  display: flex;
  flex-direction: column;

  &__header {
    display: flex;
    align-items: center;
    gap: 1rem;
    margin-bottom: 1rem;

    h2 {
      margin: 0;
      font-size: 1.125rem;
    }
  }

  &__count {
    padding: 0.125rem 0.5rem;
    background: var(--c-bg-subtle);
    border-radius: 999px;
    font-size: 0.875rem;
    color: var(--c-text-muted);
  }

  &__items {
    display: flex;
    flex-direction: column;
    gap: 0.5rem;
    overflow-y: auto;
    max-height: 70vh;
  }
}

.notable-enter-active { transition: all 250ms ease; }
.notable-enter-from   { opacity: 0; transform: translateY(-10px); }
</style>
