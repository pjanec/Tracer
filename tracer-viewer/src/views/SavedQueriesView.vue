<script setup lang="ts">
import { ref, computed, onMounted } from 'vue';
import { useRouter } from 'vue-router';
import { useSavedQueries } from '@/composables/useSavedQueries';
import type { SavedQueryDto, CreateSavedQueryDto, UpdateSavedQueryDto } from '@/types/savedQuery';
import { api } from '@/api/tracerApiClient';

const router = useRouter();
const { queries, loading, load, create, remove, toggleFavorite, clone } = useSavedQueries();

const searchText = ref('');
const tagFilter = ref('');
const favoritesOnly = ref(false);
const builtInOnly = ref(false);

const showCreateForm = ref(false);
const createLabel = ref('');
const createSql = ref('');
const createDesc = ref('');

const editingId = ref<string | null>(null);
const editLabel = ref('');
const editSql = ref('');
const editDesc = ref('');

const paramQueryId = ref<string | null>(null);
const paramValues = ref<Record<string, string>>({});

onMounted(() => load());

const allTags = computed(() => {
  const set = new Set<string>();
  for (const q of queries.value) q.tags.forEach(t => set.add(t));
  return Array.from(set).sort();
});

const filtered = computed(() => {
  let list = queries.value;
  if (favoritesOnly.value) list = list.filter(q => q.isFavorite);
  if (builtInOnly.value) list = list.filter(q => q.isBuiltIn);
  if (tagFilter.value) list = list.filter(q => q.tags.includes(tagFilter.value));
  if (searchText.value.trim()) {
    const q = searchText.value.toLowerCase();
    list = list.filter(r => r.label.toLowerCase().includes(q));
  }
  return list;
});

async function createQuery() {
  if (!createLabel.value.trim()) return;
  const dto: CreateSavedQueryDto = {
    label: createLabel.value,
    sql: createSql.value,
    description: createDesc.value || undefined,
  };
  await create(dto);
  showCreateForm.value = false;
  createLabel.value = '';
  createSql.value = '';
  createDesc.value = '';
}

function startEdit(q: SavedQueryDto) {
  editingId.value = q.savedQueryId;
  editLabel.value = q.label;
  editSql.value = q.sql;
  editDesc.value = q.description ?? '';
}

async function saveEdit() {
  if (!editingId.value) return;
  const dto: UpdateSavedQueryDto = {
    label: editLabel.value || undefined,
    sql: editSql.value || undefined,
    description: editDesc.value || undefined,
  };
  await api.updateSavedQuery(editingId.value, dto);
  await load();
  editingId.value = null;
}

function cancelEdit() {
  editingId.value = null;
}

function runQuery(q: SavedQueryDto) {
  if (q.parameters.length > 0) {
    // Initialize param values from defaults
    paramValues.value = Object.fromEntries(
      q.parameters.map(p => [p.name, p.defaultValueText ?? ''])
    );
    paramQueryId.value = q.savedQueryId;
    return;
  }
  void executeQuery(q.sql, q.savedQueryId);
}

async function executeQuery(sql: string, savedQueryId: string) {
  await api.recordSavedQueryRun(savedQueryId);
  void router.push({ name: 'sql-console', params: { sessionId: 'default' }, query: { sql } });
}

const NUMERIC_TYPES = /^(int|bigint|double|float|numeric)/i;

const paramRunDisabled = computed(() => {
  const q = filtered.value.find(q => q.savedQueryId === paramQueryId.value);
  if (!q) return true;
  return q.parameters.some(p => {
    const val = paramValues.value[p.name] ?? '';
    return NUMERIC_TYPES.test(p.duckType) && isNaN(Number(val));
  });
});

async function submitParamRun() {
  const q = filtered.value.find(q => q.savedQueryId === paramQueryId.value);
  if (!q) return;
  // Substitute param values into the SQL
  let sql = q.sql;
  for (const p of q.parameters) {
    const val = paramValues.value[p.name] ?? p.defaultValueText ?? '';
    sql = sql.replaceAll(`$${p.name}`, val);
  }
  paramQueryId.value = null;
  await executeQuery(sql, q.savedQueryId);
}

async function cloneQuery(q: SavedQueryDto) {
  await clone(q.savedQueryId, `${q.label} (copy)`);
}

async function deleteQuery(q: SavedQueryDto) {
  if (!confirm(`Delete "${q.label}"?`)) return;
  await remove(q.savedQueryId);
}
</script>

<template>
  <div class="saved-queries-view">
    <div class="saved-queries-view__header">
      <h1 class="saved-queries-view__title">Saved queries</h1>
      <button class="saved-queries-view__new-btn" @click="showCreateForm = !showCreateForm">+ New query</button>
    </div>

    <!-- Filters -->
    <div class="saved-queries-view__filters">
      <input
        v-model="searchText"
        type="text"
        class="saved-queries-view__filter-input"
        placeholder="Search by label…"
      />
      <select v-model="tagFilter" class="saved-queries-view__filter-select">
        <option value="">All tags</option>
        <option v-for="tag in allTags" :key="tag" :value="tag">{{ tag }}</option>
      </select>
      <label class="saved-queries-view__filter-toggle">
        <input v-model="favoritesOnly" type="checkbox" />
        Favorites only
      </label>
      <label class="saved-queries-view__filter-toggle">
        <input v-model="builtInOnly" type="checkbox" />
        Built-in only
      </label>
    </div>

    <!-- Create form -->
    <div v-if="showCreateForm" class="saved-queries-view__create-form">
      <h3>New query</h3>
      <input v-model="createLabel" type="text" placeholder="Label" class="saved-queries-view__form-input" />
      <input v-model="createDesc" type="text" placeholder="Description (optional)" class="saved-queries-view__form-input" />
      <textarea v-model="createSql" placeholder="SQL…" class="saved-queries-view__form-textarea" rows="4" />
      <div class="saved-queries-view__form-actions">
        <button class="saved-queries-view__btn saved-queries-view__btn--primary" @click="createQuery">Create</button>
        <button class="saved-queries-view__btn" @click="showCreateForm = false">Cancel</button>
      </div>
    </div>

    <div v-if="loading" class="saved-queries-view__loading">Loading…</div>
    <div v-else-if="filtered.length === 0" class="saved-queries-view__empty">No queries found.</div>

    <ul v-else class="saved-queries-view__list">
      <li v-for="q in filtered" :key="q.savedQueryId" class="saved-queries-view__item">
        <!-- View mode -->
        <template v-if="editingId !== q.savedQueryId">
          <div class="saved-queries-view__item-header">
            <span class="saved-queries-view__item-label">
              {{ q.label }}
              <span v-if="q.isBuiltIn" class="saved-queries-view__badge saved-queries-view__badge--builtin" title="Built-in">🔒</span>
            </span>
            <button
              class="saved-queries-view__fav-btn"
              :title="q.isFavorite ? 'Remove from favorites' : 'Add to favorites'"
              @click="toggleFavorite(q.savedQueryId)"
            >
              {{ q.isFavorite ? '★' : '☆' }}
            </button>
          </div>
          <p v-if="q.description" class="saved-queries-view__item-desc">{{ q.description }}</p>
          <div v-if="q.tags.length" class="saved-queries-view__tags">
            <span v-for="tag in q.tags" :key="tag" class="saved-queries-view__tag">{{ tag }}</span>
          </div>
          <div class="saved-queries-view__meta">
            <span v-if="q.author">{{ q.author }}</span>
            <span>Runs: {{ q.runCount }}</span>
            <span v-if="q.lastRunAtUtc">Last run: {{ new Date(q.lastRunAtUtc).toLocaleDateString() }}</span>
          </div>
          <div class="saved-queries-view__item-actions">
            <button class="saved-queries-view__btn saved-queries-view__btn--primary" @click="runQuery(q)">Run</button>
            <button
              class="saved-queries-view__btn"
              :disabled="q.isBuiltIn"
              :title="q.isBuiltIn ? 'Cannot edit built-in queries' : 'Edit'"
              @click="startEdit(q)"
            >
              Edit
            </button>
            <button class="saved-queries-view__btn" @click="cloneQuery(q)">Clone</button>
            <button
              class="saved-queries-view__btn saved-queries-view__btn--danger"
              :disabled="q.isBuiltIn"
              :title="q.isBuiltIn ? 'Cannot delete built-in queries' : 'Delete'"
              @click="deleteQuery(q)"
            >
              Delete
            </button>
          </div>
          <!-- Parameter form (shown when Run was clicked on a query with parameters) -->
          <div v-if="paramQueryId === q.savedQueryId && q.parameters.length > 0" class="saved-queries-view__param-form">
            <div
              v-for="param in q.parameters"
              :key="param.name"
              class="saved-queries-view__param-row"
            >
              <label class="saved-queries-view__param-label">
                {{ param.name }}
                <span class="saved-queries-view__param-type">{{ param.duckType }}</span>
              </label>
              <input
                v-model="paramValues[param.name]"
                type="text"
                class="saved-queries-view__form-input"
                :placeholder="param.defaultValueText"
              />
            </div>
            <div class="saved-queries-view__form-actions">
              <button
                class="saved-queries-view__btn saved-queries-view__btn--primary"
                :disabled="paramRunDisabled"
                @click="submitParamRun"
              >
                Execute
              </button>
              <button class="saved-queries-view__btn" @click="paramQueryId = null">Cancel</button>
            </div>
          </div>
        </template>

        <!-- Edit mode -->
        <template v-else>
          <input v-model="editLabel" type="text" class="saved-queries-view__form-input" />
          <input v-model="editDesc" type="text" placeholder="Description" class="saved-queries-view__form-input" />
          <textarea v-model="editSql" rows="4" class="saved-queries-view__form-textarea" />
          <div class="saved-queries-view__form-actions">
            <button class="saved-queries-view__btn saved-queries-view__btn--primary" @click="saveEdit">Save</button>
            <button class="saved-queries-view__btn" @click="cancelEdit">Cancel</button>
          </div>
        </template>
      </li>
    </ul>
  </div>
</template>

<style lang="scss">
.saved-queries-view {
  padding: 1rem;
  max-width: 900px;

  &__header { display: flex; align-items: center; gap: 1rem; margin-bottom: 1rem; }
  &__title { margin: 0; }

  &__new-btn {
    padding: 0.35rem 0.8rem;
    font-size: 0.85rem;
    background: var(--c-accent);
    color: white;
    border: none;
    border-radius: 4px;
    cursor: pointer;
    &:hover { opacity: 0.85; }
  }

  &__filters {
    display: flex;
    flex-wrap: wrap;
    gap: 0.5rem;
    margin-bottom: 1rem;
    align-items: center;
  }

  &__filter-input, &__filter-select {
    padding: 0.3rem 0.5rem;
    border: 1px solid var(--c-border);
    border-radius: 4px;
    background: var(--c-bg);
    color: var(--c-text);
    font-size: 0.85rem;
  }

  &__filter-toggle { display: flex; align-items: center; gap: 0.3rem; font-size: 0.85rem; cursor: pointer; }

  &__create-form {
    background: var(--c-bg-subtle);
    border: 1px solid var(--c-border);
    border-radius: 6px;
    padding: 1rem;
    margin-bottom: 1rem;
    display: flex;
    flex-direction: column;
    gap: 0.5rem;
  }

  &__form-input {
    padding: 0.35rem 0.5rem;
    border: 1px solid var(--c-border);
    border-radius: 4px;
    background: var(--c-bg);
    color: var(--c-text);
    font-size: 0.85rem;
  }

  &__form-textarea {
    padding: 0.35rem 0.5rem;
    border: 1px solid var(--c-border);
    border-radius: 4px;
    background: var(--c-bg);
    color: var(--c-text);
    font-size: 0.85rem;
    font-family: var(--font-mono);
    resize: vertical;
  }

  &__form-actions { display: flex; gap: 0.5rem; }

  &__loading, &__empty { padding: 1rem; color: var(--c-text-muted); }

  &__list { list-style: none; padding: 0; margin: 0; display: flex; flex-direction: column; gap: 0.75rem; }

  &__item {
    background: var(--c-bg-surface);
    border: 1px solid var(--c-border);
    border-radius: 6px;
    padding: 0.75rem 1rem;
    display: flex;
    flex-direction: column;
    gap: 0.4rem;
  }

  &__item-header { display: flex; align-items: center; gap: 0.5rem; }
  &__item-label { font-weight: 600; flex: 1; }
  &__item-desc { margin: 0; font-size: 0.85rem; color: var(--c-text-muted); }

  &__fav-btn { background: none; border: none; cursor: pointer; font-size: 1rem; color: gold; }

  &__badge {
    font-size: 0.7rem;
    &--builtin { opacity: 0.6; }
  }

  &__tags { display: flex; gap: 0.25rem; flex-wrap: wrap; }

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
    gap: 1rem;
    font-size: 0.75rem;
    color: var(--c-text-muted);
  }

  &__item-actions { display: flex; gap: 0.4rem; flex-wrap: wrap; }

  &__btn {
    padding: 0.25rem 0.6rem;
    font-size: 0.78rem;
    background: var(--c-bg-subtle);
    border: 1px solid var(--c-border);
    border-radius: 4px;
    cursor: pointer;
    &:hover:not(:disabled) { background: var(--c-bg-surface); }
    &:disabled { opacity: 0.4; cursor: not-allowed; }
    &--primary { background: var(--c-accent); color: white; border-color: var(--c-accent); &:hover:not(:disabled) { opacity: 0.85; } }
    &--danger { color: var(--c-danger, #f87171); }
  }

  &__param-form {
    margin-top: 0.75rem;
    padding: 0.75rem;
    background: var(--c-bg-subtle, #f8f8f8);
    border-radius: 4px;
    border: 1px solid var(--c-bg-subtle, #ddd);
  }

  &__param-row {
    display: flex;
    align-items: center;
    gap: 0.75rem;
    margin-bottom: 0.5rem;
  }

  &__param-label {
    min-width: 10rem;
    font-size: 0.85rem;
    font-weight: 500;
  }

  &__param-type {
    font-size: 0.75rem;
    color: var(--c-text-muted, #888);
    font-family: monospace;
    margin-left: 0.25rem;
  }
}
</style>
