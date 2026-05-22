<script setup lang="ts">
import { ref, computed } from 'vue';
import { useRouter } from 'vue-router';
import type { SqlExecuteResultDto } from '@/types/sql';

const props = defineProps<{
  result: SqlExecuteResultDto;
  sessionId: string;
}>();

const router = useRouter();

type SortDir = 'asc' | 'desc';
const sortCol = ref<number | null>(null);
const sortDir = ref<SortDir>('asc');

function toggleSort(colIndex: number) {
  if (sortCol.value === colIndex) {
    sortDir.value = sortDir.value === 'asc' ? 'desc' : 'asc';
  } else {
    sortCol.value = colIndex;
    sortDir.value = 'asc';
  }
}

const sortedRows = computed(() => {
  const rows = props.result.rows ?? [];
  if (sortCol.value === null) return rows;
  const col = sortCol.value;
  const dir = sortDir.value === 'asc' ? 1 : -1;
  return [...rows].sort((a, b) => {
    const av = a[col];
    const bv = b[col];
    if (av === null && bv === null) return 0;
    if (av === null) return dir;
    if (bv === null) return -dir;
    if (typeof av === 'number' && typeof bv === 'number') return (av - bv) * dir;
    return String(av).localeCompare(String(bv)) * dir;
  });
});

const columns = computed(() => props.result.columns ?? []);
const rowCount = computed(() => (props.result.rows ?? []).length);

function hasPivotColumn(name: string): boolean {
  return columns.value.some(c => c.name === name);
}

function pivotToTimeline(row: (unknown | null)[]) {
  const idx = columns.value.findIndex(c => c.name === 'publish_wallclock');
  if (idx < 0) return;
  const ts = row[idx];
  if (!ts) return;
  const t = new Date(String(ts)).getTime();
  void router.push({
    name: 'timeline',
    params: { sessionId: props.sessionId },
    query: {
      from: new Date(t - 2000).toISOString(),
      to: new Date(t + 2000).toISOString(),
    },
  });
}

function pivotToEntity(row: (unknown | null)[]) {
  const idx = columns.value.findIndex(c => c.name === 'entity_id');
  if (idx < 0) return;
  const entityId = row[idx];
  if (!entityId) return;
  void router.push({ name: 'entity-history', params: { entityId: String(entityId) } });
}

function pivotToCausal(row: (unknown | null)[]) {
  const idx = columns.value.findIndex(c => c.name === 'trace_id');
  if (idx < 0) return;
  const traceId = row[idx];
  if (!traceId) return;
  void router.push({ name: 'causal-by-trace', params: { traceId: String(traceId) } });
}

function exportCsv() {
  const cols = columns.value.map(c => c.name);
  const rows = props.result.rows ?? [];
  const csvContent = [
    cols.join(','),
    ...rows.map(r =>
      r.map(cell => {
        if (cell === null) return '';
        const s = String(cell);
        if (s.includes(',') || s.includes('"') || s.includes('\n')) {
          return `"${s.replace(/"/g, '""')}"`;
        }
        return s;
      }).join(',')
    ),
  ].join('\n');

  const blob = new Blob([csvContent], { type: 'text/csv;charset=utf-8;' });
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = 'query-result.csv';
  link.click();
  URL.revokeObjectURL(url);
}
</script>

<template>
  <div class="sql-result-table">
    <div class="sql-result-table__toolbar">
      <span class="sql-result-table__count">{{ rowCount }} row{{ rowCount !== 1 ? 's' : '' }}</span>
      <span v-if="result.truncated" class="sql-result-table__truncated">(truncated)</span>
      <span class="sql-result-table__elapsed">{{ result.elapsedMs }}ms</span>
      <button
        v-if="rowCount > 0"
        class="sql-result-table__export-btn"
        @click="exportCsv"
      >
        Export CSV
      </button>
    </div>

    <div class="sql-result-table__scroll">
      <table class="sql-result-table__table">
        <thead>
          <tr>
            <th
              v-for="(col, i) in columns"
              :key="col.name"
              class="sql-result-table__th"
              :class="{ 'sql-result-table__th--sorted': sortCol === i }"
              @click="toggleSort(i)"
            >
              {{ col.name }}
              <span v-if="sortCol === i" class="sql-result-table__sort-icon">
                {{ sortDir === 'asc' ? '▲' : '▼' }}
              </span>
            </th>
            <th v-if="hasPivotColumn('event_id') || hasPivotColumn('entity_id') || hasPivotColumn('trace_id') || hasPivotColumn('publish_wallclock')" class="sql-result-table__th sql-result-table__th--actions">
              Actions
            </th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="(row, ri) in sortedRows" :key="ri" class="sql-result-table__row">
            <td v-for="(cell, ci) in row" :key="ci" class="sql-result-table__td">
              <span v-if="cell === null" class="sql-result-table__null">∅</span>
              <span v-else>{{ cell }}</span>
            </td>
            <td v-if="hasPivotColumn('event_id') || hasPivotColumn('entity_id') || hasPivotColumn('trace_id') || hasPivotColumn('publish_wallclock')" class="sql-result-table__td sql-result-table__td--actions">
              <button v-if="hasPivotColumn('publish_wallclock')" class="sql-result-table__pivot-btn" @click="pivotToTimeline(row)">Timeline</button>
              <button v-if="hasPivotColumn('entity_id')" class="sql-result-table__pivot-btn" @click="pivotToEntity(row)">Entity</button>
              <button v-if="hasPivotColumn('trace_id')" class="sql-result-table__pivot-btn" @click="pivotToCausal(row)">Causal</button>
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>

<style lang="scss">
.sql-result-table {
  display: flex;
  flex-direction: column;
  height: 100%;

  &__toolbar {
    display: flex;
    align-items: center;
    gap: 0.75rem;
    padding: 0.25rem 0.5rem;
    border-bottom: 1px solid var(--c-border);
    font-size: 0.8rem;
  }

  &__count { font-weight: 600; }
  &__truncated { color: var(--c-warning, orange); }
  &__elapsed { color: var(--c-text-muted); margin-left: auto; }

  &__export-btn {
    padding: 0.2rem 0.5rem;
    font-size: 0.75rem;
    background: var(--c-bg-subtle);
    border: 1px solid var(--c-border);
    border-radius: 4px;
    cursor: pointer;
    &:hover { background: var(--c-bg-surface); }
  }

  &__scroll {
    flex: 1;
    overflow: auto;
  }

  &__table {
    width: 100%;
    border-collapse: collapse;
    font-size: 0.8rem;
  }

  &__th {
    text-align: left;
    padding: 0.3rem 0.5rem;
    border-bottom: 2px solid var(--c-border);
    font-weight: 600;
    white-space: nowrap;
    cursor: pointer;
    user-select: none;

    &:hover { background: var(--c-bg-subtle); }
    &--sorted { color: var(--c-accent); }
    &--actions { cursor: default; }
  }

  &__sort-icon { font-size: 0.65rem; margin-left: 0.25rem; }

  &__row {
    &:nth-child(even) { background: var(--c-bg-alt, rgba(255,255,255,0.03)); }
    &:hover { background: var(--c-bg-hover, rgba(255,255,255,0.06)); }
  }

  &__td {
    padding: 0.25rem 0.5rem;
    border-bottom: 1px solid var(--c-border-subtle, rgba(255,255,255,0.05));
    font-family: var(--font-mono);
    font-size: 0.78rem;
    &--actions { font-family: inherit; }
  }

  &__null { color: var(--c-text-muted); opacity: 0.5; }

  &__pivot-btn {
    padding: 0.1rem 0.4rem;
    font-size: 0.7rem;
    background: var(--c-bg-subtle);
    border: 1px solid var(--c-border);
    border-radius: 3px;
    cursor: pointer;
    margin-right: 0.25rem;
    &:hover { background: var(--c-accent); color: white; }
  }
}
</style>
