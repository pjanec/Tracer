<template>
  <div class="saved-views-view">
    <div class="saved-views-view__filters">
      <label>
        Persona:
        <select v-model="personaFilter" @change="loadViews" class="saved-views-view__persona-select">
          <option value="">All</option>
          <option value="engineer">Engineer</option>
          <option value="scenario-author">Scenario Author</option>
          <option value="operator">Operator</option>
        </select>
      </label>
    </div>

    <div v-if="loading" class="saved-views-view__loading">Loading…</div>
    <p v-else-if="views.length === 0" class="saved-views-view__empty">No saved views found.</p>
    <template v-else>
      <section
        v-for="(group, viewType) in groupedViews"
        :key="viewType"
        class="saved-views-view__group"
      >
        <h2 class="saved-views-view__group-heading">{{ viewType }}</h2>
        <div
          v-for="sv in group"
          :key="sv.savedViewId"
          class="saved-views-view__row"
        >
          <div class="saved-views-view__row-info">
            <span class="saved-views-view__label">{{ sv.label }}</span>
            <span v-if="sv.description" class="saved-views-view__desc">{{ sv.description }}</span>
          </div>
          <div class="saved-views-view__row-actions">
            <button @click="openView(sv)" class="saved-views-view__open">Open</button>
            <button @click="deleteView(sv.savedViewId)" class="saved-views-view__delete">Delete</button>
          </div>
        </div>
      </section>
    </template>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue';
import { useRouter } from 'vue-router';
import { api } from '@/api/tracerApiClient';
import type { SavedViewDto } from '@/api/tracerApiClient';

const props = defineProps<{ sessionId: string }>();
const router = useRouter();

const views = ref<SavedViewDto[]>([]);
const loading = ref(false);
const personaFilter = ref('');

const groupedViews = computed(() => {
  const groups: Record<string, SavedViewDto[]> = {};
  for (const v of views.value) {
    if (!groups[v.viewType]) groups[v.viewType] = [];
    groups[v.viewType].push(v);
  }
  return groups;
});

async function loadViews() {
  loading.value = true;
  try {
    views.value = await api.listSavedViews({
      sessionId: props.sessionId,
      kind: 'SavedView',
      persona: personaFilter.value || undefined,
    });
  } finally {
    loading.value = false;
  }
}

async function openView(sv: SavedViewDto) {
  void api.recordSavedViewOpened(sv.savedViewId);
  await router.push(sv.url);
}

async function deleteView(savedViewId: string) {
  if (!window.confirm('Delete this saved view?')) return;
  await api.deleteSavedView(savedViewId);
  await loadViews();
}

onMounted(loadViews);
</script>

<style lang="scss">
.saved-views-view {
  padding: 1rem;

  &__filters {
    margin-bottom: 1rem;
  }

  &__group {
    margin-bottom: 1.5rem;
  }

  &__group-heading {
    font-size: 1rem;
    font-weight: 600;
    margin-bottom: 0.5rem;
    text-transform: capitalize;
  }

  &__row {
    display: flex;
    align-items: center;
    justify-content: space-between;
    padding: 0.5rem 0;
    border-bottom: 1px solid var(--c-bg-subtle, #eee);
  }

  &__row-info {
    display: flex;
    flex-direction: column;
    gap: 0.2rem;
  }

  &__label {
    font-weight: 500;
  }

  &__desc {
    font-size: 0.85rem;
    color: var(--c-text-muted, #666);
  }

  &__row-actions {
    display: flex;
    gap: 0.5rem;
  }

  &__empty,
  &__loading {
    color: var(--c-text-muted, #666);
  }
}
</style>
