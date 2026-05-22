<script setup lang="ts">
import { ref, computed, onMounted } from 'vue';
import { useRoute } from 'vue-router';
import { api } from '@/api/tracerApiClient';
import { useSqlExecution } from '@/composables/useSqlExecution';
import { useSqlSchema } from '@/composables/useSqlSchema';
import SqlEditor from '@/components/SqlEditor.vue';
import SqlResultTable from '@/components/SqlResultTable.vue';
import SqlResultChart from '@/components/SqlResultChart.vue';
import SchemaPanel from '@/components/SchemaPanel.vue';
import SavedQueryPicker from '@/components/SavedQueryPicker.vue';
import type { CreateSavedQueryDto } from '@/types/savedQuery';

const props = defineProps<{ sessionId: string }>();
const route = useRoute();

const HISTORY_KEY = 'tracer:sqlHistory';
const MAX_HISTORY = 50;

const sqlText = ref('');
const resultTab = ref<'table' | 'chart'>('table');
const showSavedQueryPicker = ref(false);
const showSaveForm = ref(false);
const saveLabel = ref('');
const saveError = ref<string | null>(null);

const history = ref<string[]>([]);
const selectedHistoryIndex = ref<number | null>(null);

const { result, loading, error, run: execSql, cancel } = useSqlExecution();
const { schema, loading: schemaLoading, refresh: refreshSchema } = useSqlSchema();

function loadHistory() {
  try {
    const raw = localStorage.getItem(HISTORY_KEY);
    history.value = raw ? (JSON.parse(raw) as string[]) : [];
  } catch {
    history.value = [];
  }
}

function saveToHistory(sql: string) {
  const h = [sql, ...history.value.filter(s => s !== sql)].slice(0, MAX_HISTORY);
  history.value = h;
  localStorage.setItem(HISTORY_KEY, JSON.stringify(h));
}

function loadInitialSql() {
  const fromQuery = route.query.sql;
  if (fromQuery && typeof fromQuery === 'string') {
    sqlText.value = fromQuery;
    return;
  }
  if (history.value.length > 0) {
    sqlText.value = history.value[0];
    return;
  }
  sqlText.value = 'SELECT * FROM events LIMIT 100;';
}

onMounted(() => {
  loadHistory();
  loadInitialSql();
});

async function runSql() {
  if (!sqlText.value.trim()) return;
  saveToHistory(sqlText.value);
  await execSql(sqlText.value);
}

async function explainSql() {
  if (!sqlText.value.trim()) return;
  try {
    const res = await api.explainSql({ sql: sqlText.value });
    alert(res.planText);
  } catch (e: unknown) {
    alert(e instanceof Error ? e.message : String(e));
  }
}

async function saveQuery() {
  if (!saveLabel.value.trim()) {
    saveError.value = 'Label is required';
    return;
  }
  const dto: CreateSavedQueryDto = {
    label: saveLabel.value.trim(),
    sql: sqlText.value,
  };
  try {
    await api.createSavedQuery(dto);
    showSaveForm.value = false;
    saveLabel.value = '';
    saveError.value = null;
  } catch (e: unknown) {
    saveError.value = e instanceof Error ? e.message : String(e);
  }
}

function onSelectSavedQuery(payload: { sql: string; savedQueryId: string }) {
  sqlText.value = payload.sql;
  showSavedQueryPicker.value = false;
}

function onEditorInsert(text: string) {
  sqlText.value += text;
}

const canChart = computed(() => {
  if (!result.value?.columns || !result.value.rows) return false;
  const cols = result.value.columns;
  if (cols.length < 2) return false;
  return cols.some((c, i) => i > 0 && /double|float|int|decimal|bigint|hugeint/i.test(c.duckType));
});

function selectHistory(idx: number) {
  selectedHistoryIndex.value = idx;
  sqlText.value = history.value[idx];
}
</script>

<template>
  <div class="sql-console-view">
    <!-- Toolbar -->
    <div class="sql-console-view__toolbar">
      <h2 class="sql-console-view__title">SQL Console</h2>
      <button
        class="sql-console-view__btn sql-console-view__btn--primary"
        :disabled="loading"
        @click="runSql"
        title="Run (Ctrl+Enter)"
      >
        ▶ Run
      </button>
      <button
        class="sql-console-view__btn"
        :disabled="loading"
        @click="explainSql"
      >
        Explain
      </button>
      <button
        v-if="loading"
        class="sql-console-view__btn sql-console-view__btn--danger"
        @click="cancel"
      >
        Cancel
      </button>
      <button class="sql-console-view__btn" @click="showSaveForm = !showSaveForm">Save query</button>
      <button class="sql-console-view__btn" @click="showSavedQueryPicker = true">Saved queries…</button>
      <button class="sql-console-view__btn" :disabled="schemaLoading" @click="refreshSchema">Refresh schema</button>
    </div>

    <!-- Save form -->
    <div v-if="showSaveForm" class="sql-console-view__save-form">
      <input
        v-model="saveLabel"
        type="text"
        class="sql-console-view__save-input"
        placeholder="Query label…"
        @keydown.enter="saveQuery"
        @keydown.escape="showSaveForm = false"
      />
      <span v-if="saveError" class="sql-console-view__save-error">{{ saveError }}</span>
      <button class="sql-console-view__btn sql-console-view__btn--primary" @click="saveQuery">Save</button>
      <button class="sql-console-view__btn" @click="showSaveForm = false">Cancel</button>
    </div>

    <div class="sql-console-view__body">
      <!-- Schema panel -->
      <SchemaPanel
        class="sql-console-view__schema"
        :schema="schema"
        @insert="onEditorInsert"
      />

      <!-- Editor + results -->
      <div class="sql-console-view__center">
        <div class="sql-console-view__editor">
          <SqlEditor
            v-model="sqlText"
            :schema="schema"
            @run="runSql"
          />
        </div>

        <div class="sql-console-view__results">
          <div v-if="loading" class="sql-console-view__loading">Running…</div>
          <div v-else-if="error" class="sql-console-view__error">{{ error }}</div>
          <template v-else-if="result">
            <div v-if="result.state !== 'Succeeded'" class="sql-console-view__state-msg">
              {{ result.state }}{{ result.errorMessage ? ': ' + result.errorMessage : '' }}
            </div>
            <template v-else>
              <div class="sql-console-view__result-tabs">
                <button
                  class="sql-console-view__tab"
                  :class="{ 'sql-console-view__tab--active': resultTab === 'table' }"
                  @click="resultTab = 'table'"
                >
                  Table
                </button>
                <button
                  class="sql-console-view__tab"
                  :class="{ 'sql-console-view__tab--active': resultTab === 'chart', 'sql-console-view__tab--disabled': !canChart }"
                  :disabled="!canChart"
                  @click="if (canChart) resultTab = 'chart'"
                >
                  Chart
                </button>
              </div>
              <SqlResultTable v-if="resultTab === 'table'" :result="result" :session-id="sessionId" />
              <SqlResultChart v-else :result="result" />
            </template>
          </template>
        </div>
      </div>

      <!-- History panel -->
      <aside class="sql-console-view__history">
        <h4 class="sql-console-view__history-title">History</h4>
        <ul class="sql-console-view__history-list">
          <li
            v-for="(item, i) in history"
            :key="i"
            class="sql-console-view__history-item"
            :class="{ 'sql-console-view__history-item--active': selectedHistoryIndex === i }"
            :title="item"
            @click="selectHistory(i)"
          >
            {{ item.slice(0, 60) }}{{ item.length > 60 ? '…' : '' }}
          </li>
        </ul>
      </aside>
    </div>

    <!-- Saved Query Picker Modal -->
    <SavedQueryPicker
      v-if="showSavedQueryPicker"
      @select="onSelectSavedQuery"
      @cancel="showSavedQueryPicker = false"
    />
  </div>
</template>

<style lang="scss">
.sql-console-view {
  display: flex;
  flex-direction: column;
  height: 100%;
  overflow: hidden;

  &__toolbar {
    display: flex;
    align-items: center;
    gap: 0.5rem;
    padding: 0.5rem 1rem;
    border-bottom: 1px solid var(--c-border);
    flex-shrink: 0;
  }

  &__title { margin: 0; font-size: 1rem; margin-right: 0.5rem; }

  &__btn {
    padding: 0.3rem 0.7rem;
    font-size: 0.8rem;
    background: var(--c-bg-subtle);
    border: 1px solid var(--c-border);
    border-radius: 4px;
    cursor: pointer;
    &:hover:not(:disabled) { background: var(--c-bg-surface); }
    &:disabled { opacity: 0.5; cursor: not-allowed; }
    &--primary { background: var(--c-accent); color: white; border-color: var(--c-accent); &:hover:not(:disabled) { opacity: 0.85; } }
    &--danger { color: var(--c-danger, #f87171); }
  }

  &__save-form {
    display: flex;
    align-items: center;
    gap: 0.5rem;
    padding: 0.4rem 1rem;
    background: var(--c-bg-subtle);
    border-bottom: 1px solid var(--c-border);
    flex-shrink: 0;
  }

  &__save-input {
    flex: 1;
    padding: 0.3rem 0.5rem;
    border: 1px solid var(--c-border);
    border-radius: 4px;
    background: var(--c-bg);
    color: var(--c-text);
    font-size: 0.85rem;
  }

  &__save-error { color: var(--c-danger, #f87171); font-size: 0.8rem; }

  &__body {
    display: grid;
    grid-template-columns: 200px 1fr 220px;
    flex: 1;
    overflow: hidden;
  }

  &__schema {
    overflow-y: auto;
    border-right: 1px solid var(--c-border);
  }

  &__center {
    display: flex;
    flex-direction: column;
    overflow: hidden;
  }

  &__editor {
    height: 220px;
    flex-shrink: 0;
    border-bottom: 1px solid var(--c-border);
    overflow: hidden;
  }

  &__results {
    flex: 1;
    overflow: hidden;
    display: flex;
    flex-direction: column;
  }

  &__loading, &__error, &__state-msg {
    padding: 1rem;
    font-size: 0.85rem;
  }

  &__error, &__state-msg { color: var(--c-danger, #f87171); }

  &__result-tabs {
    display: flex;
    gap: 0.25rem;
    padding: 0.25rem 0.5rem;
    border-bottom: 1px solid var(--c-border);
    flex-shrink: 0;
  }

  &__tab {
    padding: 0.2rem 0.6rem;
    font-size: 0.8rem;
    background: none;
    border: 1px solid transparent;
    border-radius: 4px;
    cursor: pointer;
    &--active { background: var(--c-bg-subtle); border-color: var(--c-border); }
    &--disabled { opacity: 0.4; cursor: not-allowed; }
  }

  &__history {
    border-left: 1px solid var(--c-border);
    overflow-y: auto;
    padding: 0.5rem;
  }

  &__history-title { margin: 0 0 0.5rem; font-size: 0.8rem; font-weight: 600; }

  &__history-list { list-style: none; padding: 0; margin: 0; }

  &__history-item {
    font-size: 0.72rem;
    font-family: var(--font-mono);
    padding: 0.3rem 0.4rem;
    border-radius: 3px;
    cursor: pointer;
    overflow: hidden;
    white-space: nowrap;
    text-overflow: ellipsis;
    color: var(--c-text-muted);

    &:hover { background: var(--c-bg-subtle); color: var(--c-text); }
    &--active { background: var(--c-bg-subtle); color: var(--c-text); }
  }
}
</style>
