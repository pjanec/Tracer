<script setup lang="ts">
import { ref } from 'vue';
import type { SqlSchemaDto } from '@/types/sql';

const props = defineProps<{ schema: SqlSchemaDto | null }>();
const emit = defineEmits<{ insert: [text: string] }>();

const expanded = ref(new Set<string>());

function toggle(name: string) {
  if (expanded.value.has(name)) expanded.value.delete(name);
  else expanded.value.add(name);
}
</script>

<template>
  <aside class="schema-panel">
    <h4 class="schema-panel__title">Schema</h4>
    <div v-if="!schema" class="schema-panel__empty">Loading…</div>
    <ul v-else class="schema-panel__tables">
      <li v-for="t in schema.tables" :key="t.name" class="schema-panel__table">
        <div class="schema-panel__table-row" @click="toggle(t.name)">
          <span class="schema-panel__expand">{{ expanded.has(t.name) ? '▾' : '▸' }}</span>
          <span class="schema-panel__table-name" @click.stop="emit('insert', t.name)">{{ t.name }}</span>
        </div>
        <ul v-if="expanded.has(t.name)" class="schema-panel__columns">
          <li
            v-for="c in t.columns"
            :key="c.name"
            class="schema-panel__column"
            @click="emit('insert', c.name)"
          >
            <span class="schema-panel__col-name">{{ c.name }}</span>
            <span class="schema-panel__col-type">{{ c.duckType }}</span>
          </li>
        </ul>
      </li>
    </ul>
    <div v-if="schema?.dialectNotes?.length" class="schema-panel__notes">
      <h5>Hints</h5>
      <ul>
        <li v-for="(n, i) in schema.dialectNotes" :key="i">{{ n }}</li>
      </ul>
    </div>
  </aside>
</template>

<style lang="scss">
.schema-panel {
  font-size: 0.8rem;
  padding: 0.5rem;

  &__title {
    margin: 0 0 0.5rem;
    font-size: 0.85rem;
    font-weight: 600;
  }

  &__empty { color: var(--c-text-muted); }
  &__tables { list-style: none; padding: 0; margin: 0; }
  &__table { margin-bottom: 0.25rem; }

  &__table-row {
    display: flex;
    align-items: center;
    gap: 0.25rem;
    cursor: pointer;
    padding: 2px 4px;
    border-radius: 4px;

    &:hover { background: var(--c-bg-subtle); }
  }

  &__expand { color: var(--c-text-muted); width: 12px; }

  &__table-name {
    font-weight: 600;
    color: var(--c-accent);
    &:hover { text-decoration: underline; }
  }

  &__columns { list-style: none; padding: 0 0 0 1.25rem; margin: 0; }

  &__column {
    display: flex;
    gap: 0.5rem;
    align-items: baseline;
    padding: 1px 4px;
    border-radius: 4px;
    cursor: pointer;
    &:hover { background: var(--c-bg-subtle); }
  }

  &__col-name { color: var(--c-text); }
  &__col-type { color: var(--c-text-muted); font-size: 0.7rem; }

  &__notes {
    margin-top: 1rem;
    border-top: 1px solid var(--c-border);
    padding-top: 0.5rem;

    h5 { font-size: 0.75rem; margin: 0 0 0.25rem; font-weight: 600; }
    ul { list-style: disc; padding-left: 1rem; margin: 0; color: var(--c-text-muted); line-height: 1.5; font-size: 0.75rem; }
  }
}
</style>
