<script setup lang="ts">
interface BundleFilter {
  tags: string[];
  showArchived: boolean;
  query: string;
  fromDate: Date | null;
  toDate: Date | null;
}

const props = defineProps<{
  tags: string[];
  filter: BundleFilter;
}>();

const emit = defineEmits<{
  'update:filter': [value: BundleFilter];
}>();

function updateFilter(partial: Partial<BundleFilter>) {
  emit('update:filter', { ...props.filter, ...partial });
}

function toggleTag(tag: string) {
  const tags = props.filter.tags.includes(tag)
    ? props.filter.tags.filter(t => t !== tag)
    : [...props.filter.tags, tag];
  updateFilter({ tags });
}

function clearFilters() {
  emit('update:filter', {
    tags: [],
    showArchived: false,
    query: '',
    fromDate: null,
    toDate: null,
  });
}
</script>

<template>
  <aside class="bundle-filter-panel">
    <div class="bundle-filter-panel__section">
      <label class="bundle-filter-panel__label">Search</label>
      <input
        :value="filter.query"
        type="text"
        class="bundle-filter-panel__input"
        placeholder="Filter by label…"
        @input="updateFilter({ query: ($event.target as HTMLInputElement).value })"
      />
    </div>

    <div class="bundle-filter-panel__section">
      <label class="bundle-filter-panel__label">Date range</label>
      <input
        type="date"
        class="bundle-filter-panel__input"
        :value="filter.fromDate ? filter.fromDate.toISOString().split('T')[0] : ''"
        @change="updateFilter({ fromDate: ($event.target as HTMLInputElement).value ? new Date(($event.target as HTMLInputElement).value) : null })"
      />
      <input
        type="date"
        class="bundle-filter-panel__input"
        :value="filter.toDate ? filter.toDate.toISOString().split('T')[0] : ''"
        @change="updateFilter({ toDate: ($event.target as HTMLInputElement).value ? new Date(($event.target as HTMLInputElement).value) : null })"
      />
    </div>

    <div v-if="tags.length" class="bundle-filter-panel__section">
      <label class="bundle-filter-panel__label">Tags</label>
      <div class="bundle-filter-panel__tags">
        <label
          v-for="tag in tags"
          :key="tag"
          class="bundle-filter-panel__tag-row"
        >
          <input
            type="checkbox"
            :checked="filter.tags.includes(tag)"
            @change="toggleTag(tag)"
          />
          {{ tag }}
        </label>
      </div>
    </div>

    <div class="bundle-filter-panel__section">
      <label class="bundle-filter-panel__tag-row">
        <input
          type="checkbox"
          :checked="filter.showArchived"
          @change="updateFilter({ showArchived: ($event.target as HTMLInputElement).checked })"
        />
        Show archived
      </label>
    </div>

    <button class="bundle-filter-panel__clear" @click="clearFilters">Clear filters</button>
  </aside>
</template>

<style lang="scss">
.bundle-filter-panel {
  padding: 0.75rem;
  display: flex;
  flex-direction: column;
  gap: 1rem;
  font-size: 0.85rem;
  border-right: 1px solid var(--c-border);

  &__section { display: flex; flex-direction: column; gap: 0.35rem; }
  &__label { font-weight: 600; font-size: 0.8rem; color: var(--c-text-muted); }

  &__input {
    padding: 0.3rem 0.5rem;
    border: 1px solid var(--c-border);
    border-radius: 4px;
    background: var(--c-bg);
    color: var(--c-text);
    font-size: 0.8rem;
  }

  &__tags { display: flex; flex-direction: column; gap: 0.25rem; }
  &__tag-row { display: flex; align-items: center; gap: 0.4rem; cursor: pointer; }

  &__clear {
    padding: 0.3rem 0.5rem;
    font-size: 0.8rem;
    background: var(--c-bg-subtle);
    border: 1px solid var(--c-border);
    border-radius: 4px;
    cursor: pointer;
    &:hover { background: var(--c-bg-surface); }
  }
}
</style>
