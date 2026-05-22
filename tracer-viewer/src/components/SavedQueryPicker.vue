<script setup lang="ts">
import { ref, computed, onMounted } from 'vue';
import { useSavedQueries } from '@/composables/useSavedQueries';
import type { SavedQueryDto } from '@/types/savedQuery';

const emit = defineEmits<{
  select: [payload: { sql: string; savedQueryId: string }];
  cancel: [];
}>();

type Tab = 'all' | 'builtin' | 'favorites';
const activeTab = ref<Tab>('all');
const search = ref('');
const { queries, loading, load, clone } = useSavedQueries();

onMounted(() => load());

const filtered = computed(() => {
  let list = queries.value;
  if (activeTab.value === 'builtin') list = list.filter(q => q.isBuiltIn);
  else if (activeTab.value === 'favorites') list = list.filter(q => q.isFavorite);
  if (search.value.trim()) {
    const q = search.value.toLowerCase();
    list = list.filter(q2 => q2.label.toLowerCase().includes(q));
  }
  return list;
});

function selectQuery(q: SavedQueryDto) {
  emit('select', { sql: q.sql, savedQueryId: q.savedQueryId });
}

async function cloneQuery(q: SavedQueryDto) {
  await clone(q.savedQueryId, `${q.label} (copy)`);
}

function onKeydown(e: KeyboardEvent) {
  if (e.key === 'Escape') emit('cancel');
}
</script>

<template>
  <div class="saved-query-picker" @keydown="onKeydown" tabindex="-1">
    <div class="saved-query-picker__overlay" @click="emit('cancel')" />
    <div class="saved-query-picker__dialog">
      <div class="saved-query-picker__header">
        <h3 class="saved-query-picker__title">Saved queries</h3>
        <button class="saved-query-picker__close" @click="emit('cancel')">×</button>
      </div>

      <div class="saved-query-picker__toolbar">
        <input
          v-model="search"
          class="saved-query-picker__search"
          type="text"
          placeholder="Search by label…"
          autofocus
        />
        <div class="saved-query-picker__tabs">
          <button
            v-for="tab in (['all', 'builtin', 'favorites'] as Tab[])"
            :key="tab"
            class="saved-query-picker__tab"
            :class="{ 'saved-query-picker__tab--active': activeTab === tab }"
            @click="activeTab = tab"
          >
            {{ tab === 'all' ? 'All' : tab === 'builtin' ? 'Built-in' : 'Favorites' }}
          </button>
        </div>
      </div>

      <div v-if="loading" class="saved-query-picker__loading">Loading…</div>
      <div v-else-if="filtered.length === 0" class="saved-query-picker__empty">No queries found.</div>
      <ul v-else class="saved-query-picker__list">
        <li
          v-for="q in filtered"
          :key="q.savedQueryId"
          class="saved-query-picker__item"
          @click="selectQuery(q)"
        >
          <div class="saved-query-picker__item-header">
            <span class="saved-query-picker__item-label">{{ q.label }}</span>
            <span v-if="q.isBuiltIn" class="saved-query-picker__badge saved-query-picker__badge--builtin">built-in</span>
            <span v-if="q.isFavorite" class="saved-query-picker__badge saved-query-picker__badge--fav">★</span>
          </div>
          <div v-if="q.tags.length" class="saved-query-picker__tags">
            <span v-for="tag in q.tags" :key="tag" class="saved-query-picker__tag">{{ tag }}</span>
          </div>
          <div class="saved-query-picker__meta">
            <span>Runs: {{ q.runCount }}</span>
            <button
              v-if="q.isBuiltIn"
              class="saved-query-picker__clone-btn"
              @click.stop="cloneQuery(q)"
            >
              Clone
            </button>
          </div>
        </li>
      </ul>
    </div>
  </div>
</template>

<style lang="scss">
.saved-query-picker {
  position: fixed;
  inset: 0;
  z-index: 1000;
  display: flex;
  align-items: center;
  justify-content: center;

  &__overlay {
    position: absolute;
    inset: 0;
    background: rgba(0, 0, 0, 0.5);
  }

  &__dialog {
    position: relative;
    background: var(--c-bg-surface);
    border: 1px solid var(--c-border);
    border-radius: 8px;
    width: 600px;
    max-height: 80vh;
    display: flex;
    flex-direction: column;
    overflow: hidden;
    z-index: 1;
  }

  &__header {
    display: flex;
    align-items: center;
    padding: 0.75rem 1rem;
    border-bottom: 1px solid var(--c-border);
  }

  &__title { margin: 0; font-size: 1rem; flex: 1; }
  &__close { background: none; border: none; font-size: 1.25rem; cursor: pointer; color: var(--c-text-muted); }

  &__toolbar {
    padding: 0.5rem 1rem;
    display: flex;
    flex-direction: column;
    gap: 0.5rem;
    border-bottom: 1px solid var(--c-border);
  }

  &__search {
    width: 100%;
    padding: 0.35rem 0.5rem;
    border: 1px solid var(--c-border);
    border-radius: 4px;
    background: var(--c-bg);
    color: var(--c-text);
    font-size: 0.85rem;
  }

  &__tabs { display: flex; gap: 0.25rem; }

  &__tab {
    padding: 0.25rem 0.6rem;
    font-size: 0.8rem;
    background: var(--c-bg-subtle);
    border: 1px solid var(--c-border);
    border-radius: 4px;
    cursor: pointer;
    &--active { background: var(--c-accent); color: white; border-color: var(--c-accent); }
  }

  &__loading, &__empty {
    padding: 2rem;
    text-align: center;
    color: var(--c-text-muted);
  }

  &__list { list-style: none; padding: 0; margin: 0; overflow-y: auto; flex: 1; }

  &__item {
    padding: 0.6rem 1rem;
    border-bottom: 1px solid var(--c-border-subtle, rgba(255,255,255,0.05));
    cursor: pointer;
    &:hover { background: var(--c-bg-subtle); }
  }

  &__item-header { display: flex; align-items: center; gap: 0.5rem; }
  &__item-label { font-weight: 500; flex: 1; }

  &__badge {
    font-size: 0.65rem;
    padding: 0.1rem 0.3rem;
    border-radius: 3px;
    &--builtin { background: var(--c-info-bg, #1a3a5a); color: var(--c-info, #6fb3f7); }
    &--fav { background: none; color: gold; }
  }

  &__tags { display: flex; gap: 0.25rem; flex-wrap: wrap; margin-top: 0.25rem; }

  &__tag {
    font-size: 0.7rem;
    padding: 0.1rem 0.35rem;
    background: var(--c-bg-subtle);
    border: 1px solid var(--c-border);
    border-radius: 3px;
    color: var(--c-text-muted);
  }

  &__meta {
    display: flex;
    align-items: center;
    gap: 0.75rem;
    margin-top: 0.25rem;
    font-size: 0.75rem;
    color: var(--c-text-muted);
  }

  &__clone-btn {
    padding: 0.1rem 0.4rem;
    font-size: 0.7rem;
    background: var(--c-bg-subtle);
    border: 1px solid var(--c-border);
    border-radius: 3px;
    cursor: pointer;
    &:hover { background: var(--c-accent); color: white; }
  }
}
</style>
