<!-- src/views/EntityPickerView.vue -->
<script setup lang="ts">
import { ref, computed, onMounted } from 'vue';
import { useRouter } from 'vue-router';
import { api } from '@/api/tracerApiClient';
import type { EntitySummaryDto } from '@/api/tracerApiClient';
import LoadingSpinner from '@/components/LoadingSpinner.vue';
import ErrorMessage from '@/components/ErrorMessage.vue';

const props = defineProps<{ sessionId: string }>();
const router = useRouter();

const entities = ref<EntitySummaryDto[]>([]);
const loading = ref(false);
const error = ref<string | null>(null);
const filterText = ref('');

const filteredEntities = computed(() => {
  if (!filterText.value) return entities.value;
  const q = filterText.value.toLowerCase();
  return entities.value.filter(e =>
    e.entityId.toLowerCase().includes(q) ||
    (e.samplePlayerId?.toLowerCase().includes(q) ?? false) ||
    e.topics.some(t => t.toLowerCase().includes(q)),
  );
});

function openEntity(entityId: string) {
  void router.push({
    name: 'entity-history',
    params: { entityId },
    query: { session: props.sessionId },
  });
}

onMounted(async () => {
  loading.value = true;
  error.value = null;
  try {
    const result = await api.listEntities(props.sessionId);
    entities.value = result.entities;
  } catch (err: unknown) {
    error.value = err instanceof Error ? err.message : 'Failed to load entities';
  } finally {
    loading.value = false;
  }
});
</script>

<template>
  <div class="entity-picker">
    <h1>Entities — {{ sessionId }}</h1>
    <input
      v-model="filterText"
      placeholder="Filter entities..."
      class="entity-picker__filter"
    />

    <LoadingSpinner v-if="loading" />
    <ErrorMessage v-else-if="error" :message="error" />
    <div
      v-else-if="filteredEntities.length === 0"
      class="entity-picker__empty"
    >
      No entities found.
    </div>
    <ul
      v-else
      class="entity-picker__list"
    >
      <li
        v-for="entity in filteredEntities"
        :key="entity.entityId"
        class="entity-picker__item"
        @click="openEntity(entity.entityId)"
      >
        <span class="entity-picker__entity-id">{{ entity.entityId }}</span>
        <span class="entity-picker__event-count">{{ entity.eventCount.toLocaleString() }} events</span>
        <span
          v-if="entity.samplePlayerId"
          class="entity-picker__player"
        >{{ entity.samplePlayerId }}</span>
        <span class="entity-picker__topics">
          {{ entity.topics.slice(0, 5).join(', ') }}
          <template v-if="entity.topics.length > 5">+{{ entity.topics.length - 5 }} more</template>
        </span>
      </li>
    </ul>
  </div>
</template>

<style lang="scss">
.entity-picker {
  max-width: 1200px;
  margin: 0 auto;
  padding: 2rem;

  &__filter {
    display: block;
    width: 100%;
    max-width: 400px;
    padding: 0.5rem 0.75rem;
    margin-bottom: 1.5rem;
    border: 1px solid var(--c-border);
    border-radius: 6px;
    background: var(--c-bg-surface);
    color: var(--c-text);
    font-size: 0.875rem;
  }

  &__list {
    list-style: none;
    padding: 0;
    margin: 0;
    display: flex;
    flex-direction: column;
    gap: 0.5rem;
  }

  &__item {
    display: flex;
    align-items: center;
    gap: 1rem;
    padding: 0.75rem 1rem;
    background: var(--c-bg-surface);
    border-radius: 6px;
    border: 1px solid transparent;
    cursor: pointer;
    transition: border-color 150ms ease;

    &:hover {
      border-color: var(--c-accent);
    }
  }

  &__entity-id {
    font-weight: 600;
    font-family: monospace;
    font-size: 0.875rem;
    flex: 0 0 auto;
  }

  &__event-count {
    font-size: 0.8125rem;
    color: var(--c-text-muted);
    flex: 0 0 auto;
  }

  &__player {
    font-size: 0.8125rem;
    color: var(--c-text-muted);
    flex: 0 0 auto;
  }

  &__topics {
    font-size: 0.75rem;
    color: var(--c-text-muted);
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }

  &__empty {
    padding: 3rem;
    text-align: center;
    color: var(--c-text-muted);
    background: var(--c-bg-subtle);
    border-radius: 8px;
  }
}
</style>
