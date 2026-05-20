<template>
  <div class="filter-panel">
    <!-- Active filter chips -->
    <div v-if="hasActiveFilters" class="filter-panel__chips">
      <FilterChip
        v-for="chip in activeChips"
        :key="chip.key"
        :label="chip.label"
        :value="chip.value"
        @remove="removeChip(chip)"
      />
    </div>

    <!-- Topic section -->
    <div class="filter-panel__section">
      <button class="filter-panel__section-header" @click="toggleSection('topic')">
        <span>Topic</span>
        <span>{{ sections.topic ? '▲' : '▼' }}</span>
      </button>
      <div v-if="sections.topic" class="filter-panel__section-body">
        <input
          v-model="topicInput"
          class="filter-panel__input"
          placeholder="e.g. weapons.fire"
          @keydown.enter="addTopic"
        />
        <button class="filter-panel__add-btn" @click="addTopic">Add</button>
      </div>
    </div>

    <!-- Notables toggle -->
    <div class="filter-panel__section">
      <label class="filter-panel__notables-toggle">
        <input
          type="checkbox"
          :checked="store.filter.notablesOnly"
          class="filter-panel__notables-checkbox"
          @change="toggleNotablesOnly"
        />
        Notable events only
      </label>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, reactive } from 'vue';
import { useTimelineStore } from '@/stores/timelineStore';
import FilterChip from './FilterChip.vue';
import type { FilterChipValue } from '@/types/filter';

const store = useTimelineStore();

const topicInput = ref('');
const sections = reactive({ topic: false });

function toggleSection(name: keyof typeof sections) {
  sections[name] = !sections[name];
}

const hasActiveFilters = computed(() =>
  (store.filter.topics?.length ?? 0) > 0 ||
  (store.filter.nodes?.length  ?? 0) > 0 ||
  !!store.filter.traceId ||
  !!store.filter.notablesOnly,
);

const activeChips = computed<FilterChipValue[]>(() => {
  const chips: FilterChipValue[] = [];
  store.filter.topics?.forEach((t) => chips.push({ key: `topic:${t}`, label: 'topic', value: t, type: 'topic' }));
  store.filter.nodes?.forEach((n)  => chips.push({ key: `node:${n}`,  label: 'node',  value: n, type: 'node'  }));
  if (store.filter.traceId) chips.push({ key: `trace:${store.filter.traceId}`, label: 'trace', value: store.filter.traceId, type: 'traceId' });
  return chips;
});

function removeChip(chip: FilterChipValue) {
  if (chip.type === 'topic') {
    store.applyFilter({ topics: store.filter.topics?.filter((t) => t !== chip.value) ?? [] });
  } else if (chip.type === 'node') {
    store.applyFilter({ nodes: store.filter.nodes?.filter((n) => n !== chip.value) ?? [] });
  } else if (chip.type === 'traceId') {
    store.applyFilter({ traceId: undefined });
  }
}

function addTopic() {
  const val = topicInput.value.trim();
  if (!val) return;
  const existing = store.filter.topics ?? [];
  if (!existing.includes(val)) {
    store.applyFilter({ topics: [...existing, val] });
  }
  topicInput.value = '';
}

function toggleNotablesOnly(e: Event) {
  store.applyFilter({ notablesOnly: (e.target as HTMLInputElement).checked || undefined });
}
</script>

<style scoped>
.filter-panel { padding: 8px; }
.filter-panel__chips { display: flex; flex-wrap: wrap; gap: 4px; margin-bottom: 8px; }
.filter-panel__section { margin-bottom: 4px; }
.filter-panel__section-header { width: 100%; display: flex; justify-content: space-between; background: none; border: none; cursor: pointer; padding: 4px 0; }
.filter-panel__section-body { padding: 4px 0; display: flex; gap: 4px; }
.filter-panel__input { flex: 1; padding: 2px 6px; border: 1px solid #ccc; border-radius: 3px; }
.filter-panel__add-btn { padding: 2px 8px; cursor: pointer; }
.filter-panel__notables-toggle { display: flex; align-items: center; gap: 4px; cursor: pointer; padding: 4px 0; }
</style>
