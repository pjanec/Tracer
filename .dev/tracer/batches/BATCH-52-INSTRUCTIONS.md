# BATCH-52 — Phase 10 Frontend: SQL Console, Saved Queries, Bundle Library

**Tasks:** TRC-P10-011 through TRC-P10-018  
**Depends on:** BATCH-51 (committed — Phase 10 backend complete)

---

## Context

BATCH-51 delivered the full Phase 10 backend: `SqlGuardrails`, `SqlExecutorService`, `SqlSchemaService`, `Tracer.Storage.SavedQueries`, `BundleLibraryService`, `BundleExportService`, `BundleImportService`, `ViewSqlTemplateService`, and all associated endpoints. The frontend is now needed.

Read before starting:
- `docs/tracer_phase10_design.md` §5 (SQL Console frontend), §6.5 (parameter defaults), §7 (Bundle Library), §8 (Show SQL affordance)
- `docs/TASK-DETAIL.md` sections TRC-P10-011 through TRC-P10-018

---

## Step 0 — Install CodeMirror 6 packages

Run in `tracer-viewer/`:
```
cd d:\Work\Tracer\tracer-viewer
pnpm add @codemirror/lang-sql @codemirror/state @codemirror/view @codemirror/autocomplete @codemirror/commands @codemirror/search @codemirror/theme-one-dark
```

Verify packages appear in `package.json` dependencies.

---

## Step 1 — Type Files

### `tracer-viewer/src/types/sql.ts` (NEW)

```typescript
// tracer-viewer/src/types/sql.ts

export interface SqlColumnInfoDto {
  name: string;
  duckType: string;
}

export interface SqlTableInfoDto {
  name: string;
  columns: SqlColumnInfoDto[];
}

export interface SqlSchemaDto {
  tables: SqlTableInfoDto[];
  refreshedAtUtc: string;
  dialectNotes: string[];
}

export interface SqlExecuteResultDto {
  state: 'Succeeded' | 'Failed' | 'Timeout' | 'Rejected';
  columns?: SqlColumnInfoDto[];
  rows?: (unknown | null)[][];
  errorMessage?: string;
  elapsedMs: number;
  truncated: boolean;
}

export interface SqlExplainResultDto {
  planText: string;
}

export interface ViewSqlTemplateResultDto {
  viewType: string;
  sql: string;
}

export interface SqlExecuteRequestDto {
  sql: string;
  parameters?: Record<string, unknown>;
  timeoutSeconds?: number;
  maxRows?: number;
}

export interface SqlExplainRequestDto {
  sql: string;
}
```

### `tracer-viewer/src/types/savedQuery.ts` (NEW)

```typescript
// tracer-viewer/src/types/savedQuery.ts

export interface SavedQueryParameterDto {
  name: string;
  duckType: string;
  defaultValueText: string;
  description?: string;
}

export interface SavedQueryDto {
  savedQueryId: string;
  label: string;
  description?: string;
  sql: string;
  parameters: SavedQueryParameterDto[];
  tags: string[];
  isBuiltIn: boolean;
  isFavorite: boolean;
  author?: string;
  createdAtUtc: string;
  lastRunAtUtc?: string;
  runCount: number;
}

export interface CreateSavedQueryDto {
  label: string;
  description?: string;
  sql: string;
  parameters?: SavedQueryParameterDto[];
  tags?: string[];
  author?: string;
}

export interface UpdateSavedQueryDto {
  label?: string;
  description?: string;
  sql?: string;
  parameters?: SavedQueryParameterDto[];
  tags?: string[];
}

export interface SavedQueryListDto {
  queries: SavedQueryDto[];
}
```

### Extend `tracer-viewer/src/types/bundle.ts` (or create if it doesn't exist)

Check if `src/types/bundle.ts` already exists. If it does, append the new types. If not, create it:

```typescript
// tracer-viewer/src/types/bundle.ts

export interface BundleLibraryEntryDto {
  bundleId: string;
  sessionId: string;
  label?: string;
  description?: string;
  tags: string[];
  isArchived: boolean;
  sessionStartUtc: string;
  sessionEndUtc: string;
  builtAtUtc: string;
  lastOpenedAtUtc?: string;
  sizeBytes: number;
}

export interface BundleLibraryListDto {
  entries: BundleLibraryEntryDto[];
}

export interface UpdateBundleMetadataDto {
  label?: string;
  description?: string;
  tags?: string[];
  isArchived?: boolean;
}
```

---

## Step 2 — Add Phase 10 API Methods to `tracerApiClient.ts`

Append the following methods to the `TracerApiClient` class. Find the end of the class before the closing `}` and insert before it:

```typescript
  // ── Phase 10: SQL Console ──────────────────────────────────────────────────

  async executeSql(req: SqlExecuteRequestDto, signal?: AbortSignal): Promise<SqlExecuteResultDto> {
    const res = await fetch('/api/sql/execute', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(req),
      signal,
    });
    if (!res.ok) throw new Error(`executeSql: ${res.status}`);
    return res.json() as Promise<SqlExecuteResultDto>;
  }

  async getSqlSchema(signal?: AbortSignal): Promise<SqlSchemaDto> {
    const res = await fetch('/api/sql/schema', { signal });
    if (!res.ok) throw new Error(`getSqlSchema: ${res.status}`);
    return res.json() as Promise<SqlSchemaDto>;
  }

  async explainSql(req: SqlExplainRequestDto, signal?: AbortSignal): Promise<SqlExplainResultDto> {
    const res = await fetch('/api/sql/explain', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(req),
      signal,
    });
    if (!res.ok) throw new Error(`explainSql: ${res.status}`);
    return res.json() as Promise<SqlExplainResultDto>;
  }

  async getViewSqlTemplate(viewType: string, params: Record<string, string> = {}): Promise<ViewSqlTemplateResultDto> {
    const qs = new URLSearchParams({ viewType, ...params });
    const res = await fetch(`/api/sql/view-template?${qs}`);
    if (!res.ok) throw new Error(`getViewSqlTemplate: ${res.status}`);
    return res.json() as Promise<ViewSqlTemplateResultDto>;
  }

  // ── Phase 10: Saved Queries ────────────────────────────────────────────────

  async listSavedQueries(opts?: {
    tag?: string; author?: string; favorite?: boolean; builtIn?: boolean; signal?: AbortSignal;
  }): Promise<SavedQueryDto[]> {
    const qs = new URLSearchParams();
    if (opts?.tag) qs.set('tag', opts.tag);
    if (opts?.author) qs.set('author', opts.author);
    if (opts?.favorite !== undefined) qs.set('favorite', String(opts.favorite));
    if (opts?.builtIn !== undefined) qs.set('builtIn', String(opts.builtIn));
    const res = await fetch(`/api/saved-queries?${qs}`, { signal: opts?.signal });
    if (!res.ok) throw new Error(`listSavedQueries: ${res.status}`);
    const data = await res.json() as SavedQueryListDto;
    return data.queries;
  }

  async getSavedQuery(id: string): Promise<SavedQueryDto | null> {
    const res = await fetch(`/api/saved-queries/${encodeURIComponent(id)}`);
    if (res.status === 404) return null;
    if (!res.ok) throw new Error(`getSavedQuery: ${res.status}`);
    return res.json() as Promise<SavedQueryDto>;
  }

  async createSavedQuery(dto: CreateSavedQueryDto): Promise<SavedQueryDto> {
    const res = await fetch('/api/saved-queries', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(dto),
    });
    if (!res.ok) throw new Error(`createSavedQuery: ${res.status}`);
    return res.json() as Promise<SavedQueryDto>;
  }

  async updateSavedQuery(id: string, dto: UpdateSavedQueryDto): Promise<SavedQueryDto | null> {
    const res = await fetch(`/api/saved-queries/${encodeURIComponent(id)}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(dto),
    });
    if (res.status === 404 || res.status === 405) return null;
    if (!res.ok) throw new Error(`updateSavedQuery: ${res.status}`);
    return res.json() as Promise<SavedQueryDto>;
  }

  async deleteSavedQuery(id: string): Promise<void> {
    const res = await fetch(`/api/saved-queries/${encodeURIComponent(id)}`, { method: 'DELETE' });
    if (!res.ok && res.status !== 404) throw new Error(`deleteSavedQuery: ${res.status}`);
  }

  async toggleSavedQueryFavorite(id: string): Promise<SavedQueryDto | null> {
    const res = await fetch(`/api/saved-queries/${encodeURIComponent(id)}/favorite`, { method: 'POST' });
    if (res.status === 404) return null;
    if (!res.ok) throw new Error(`toggleSavedQueryFavorite: ${res.status}`);
    return res.json() as Promise<SavedQueryDto>;
  }

  async cloneSavedQuery(id: string, label: string): Promise<SavedQueryDto> {
    const res = await fetch(`/api/saved-queries/${encodeURIComponent(id)}/clone`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ label }),
    });
    if (!res.ok) throw new Error(`cloneSavedQuery: ${res.status}`);
    return res.json() as Promise<SavedQueryDto>;
  }

  async recordSavedQueryRun(id: string): Promise<void> {
    const res = await fetch(`/api/saved-queries/${encodeURIComponent(id)}/run`, { method: 'POST' });
    if (!res.ok) throw new Error(`recordSavedQueryRun: ${res.status}`);
  }

  // ── Phase 10: Bundle Library ───────────────────────────────────────────────

  async listBundleLibrary(opts?: {
    showArchived?: boolean; tag?: string; sortBy?: string; sortDesc?: boolean; signal?: AbortSignal;
  }): Promise<BundleLibraryListDto> {
    const qs = new URLSearchParams();
    if (opts?.showArchived !== undefined) qs.set('showArchived', String(opts.showArchived));
    if (opts?.tag) qs.set('tag', opts.tag);
    if (opts?.sortBy) qs.set('sortBy', opts.sortBy);
    if (opts?.sortDesc !== undefined) qs.set('sortDesc', String(opts.sortDesc));
    const res = await fetch(`/api/bundles/library?${qs}`, { signal: opts?.signal });
    if (!res.ok) throw new Error(`listBundleLibrary: ${res.status}`);
    return res.json() as Promise<BundleLibraryListDto>;
  }

  async updateBundleMetadata(bundleId: string, dto: UpdateBundleMetadataDto): Promise<void> {
    const res = await fetch(`/api/bundles/${encodeURIComponent(bundleId)}/metadata`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(dto),
    });
    if (!res.ok) throw new Error(`updateBundleMetadata: ${res.status}`);
  }

  async recordBundleOpened(bundleId: string): Promise<void> {
    const res = await fetch(`/api/bundles/${encodeURIComponent(bundleId)}/opened`, { method: 'POST' });
    if (!res.ok) throw new Error(`recordBundleOpened: ${res.status}`);
  }

  async deleteBundle(bundleId: string): Promise<void> {
    const res = await fetch(`/api/bundles/${encodeURIComponent(bundleId)}`, { method: 'DELETE' });
    if (!res.ok && res.status !== 404) throw new Error(`deleteBundle: ${res.status}`);
  }

  getBundleDownloadUrl(bundleId: string): string {
    return `/api/bundles/${encodeURIComponent(bundleId)}/download`;
  }
```

Also add the import types at the top of `tracerApiClient.ts`. After the existing type imports, add:
```typescript
import type { SqlExecuteRequestDto, SqlExecuteResultDto, SqlExplainRequestDto, SqlExplainResultDto, SqlSchemaDto, ViewSqlTemplateResultDto } from '@/types/sql';
import type { SavedQueryDto, SavedQueryListDto, CreateSavedQueryDto, UpdateSavedQueryDto } from '@/types/savedQuery';
import type { BundleLibraryListDto, UpdateBundleMetadataDto } from '@/types/bundle';
```

---

## Step 3 — Composables

### `tracer-viewer/src/composables/useSqlExecution.ts` (NEW)

```typescript
// tracer-viewer/src/composables/useSqlExecution.ts
import { ref } from 'vue';
import { api } from '@/api/tracerApiClient';
import type { SqlExecuteResultDto } from '@/types/sql';

export function useSqlExecution() {
  const result = ref<SqlExecuteResultDto | null>(null);
  const loading = ref(false);
  const error = ref<string | null>(null);
  let abortController: AbortController | null = null;

  async function run(sql: string, opts?: { timeoutSeconds?: number; maxRows?: number; parameters?: Record<string, unknown> }) {
    abortController?.abort();
    abortController = new AbortController();
    loading.value = true;
    error.value = null;
    result.value = null;
    try {
      result.value = await api.executeSql(
        { sql, timeoutSeconds: opts?.timeoutSeconds, maxRows: opts?.maxRows, parameters: opts?.parameters },
        abortController.signal,
      );
    } catch (e: unknown) {
      if (e instanceof Error && e.name === 'AbortError') return;
      error.value = e instanceof Error ? e.message : String(e);
    } finally {
      loading.value = false;
    }
  }

  function cancel() {
    abortController?.abort();
    abortController = null;
    loading.value = false;
  }

  return { result, loading, error, run, cancel };
}
```

### `tracer-viewer/src/composables/useSqlSchema.ts` (NEW)

```typescript
// tracer-viewer/src/composables/useSqlSchema.ts
import { ref, onMounted } from 'vue';
import { api } from '@/api/tracerApiClient';
import type { SqlSchemaDto } from '@/types/sql';

export function useSqlSchema() {
  const schema = ref<SqlSchemaDto | null>(null);
  const loading = ref(false);

  async function refresh() {
    loading.value = true;
    try {
      schema.value = await api.getSqlSchema();
    } catch {
      // schema stays null
    } finally {
      loading.value = false;
    }
  }

  onMounted(refresh);

  return { schema, loading, refresh };
}
```

### `tracer-viewer/src/composables/useSavedQueries.ts` (NEW)

```typescript
// tracer-viewer/src/composables/useSavedQueries.ts
import { ref } from 'vue';
import { api } from '@/api/tracerApiClient';
import type { SavedQueryDto, CreateSavedQueryDto } from '@/types/savedQuery';

export function useSavedQueries() {
  const queries = ref<SavedQueryDto[]>([]);
  const loading = ref(false);

  async function load(opts?: { tag?: string; favorite?: boolean; builtIn?: boolean }) {
    loading.value = true;
    try {
      queries.value = await api.listSavedQueries(opts);
    } finally {
      loading.value = false;
    }
  }

  async function create(dto: CreateSavedQueryDto): Promise<SavedQueryDto> {
    const q = await api.createSavedQuery(dto);
    queries.value = [...queries.value, q];
    return q;
  }

  async function remove(id: string) {
    await api.deleteSavedQuery(id);
    queries.value = queries.value.filter(q => q.savedQueryId !== id);
  }

  async function toggleFavorite(id: string) {
    const updated = await api.toggleSavedQueryFavorite(id);
    if (updated) {
      queries.value = queries.value.map(q => q.savedQueryId === id ? updated : q);
    }
  }

  async function clone(id: string, label: string): Promise<SavedQueryDto> {
    const cloned = await api.cloneSavedQuery(id, label);
    queries.value = [...queries.value, cloned];
    return cloned;
  }

  return { queries, loading, load, create, remove, toggleFavorite, clone };
}
```

### `tracer-viewer/src/composables/useBundleLibrary.ts` (NEW)

```typescript
// tracer-viewer/src/composables/useBundleLibrary.ts
import { ref } from 'vue';
import { api } from '@/api/tracerApiClient';
import type { BundleLibraryEntryDto, UpdateBundleMetadataDto } from '@/types/bundle';

export function useBundleLibrary() {
  const bundles = ref<BundleLibraryEntryDto[]>([]);
  const loading = ref(false);
  const error = ref<string | null>(null);

  async function load(opts?: { showArchived?: boolean; tag?: string }) {
    loading.value = true;
    error.value = null;
    try {
      const data = await api.listBundleLibrary(opts);
      bundles.value = data.entries;
    } catch (e: unknown) {
      error.value = e instanceof Error ? e.message : String(e);
    } finally {
      loading.value = false;
    }
  }

  async function updateMetadata(bundleId: string, dto: UpdateBundleMetadataDto) {
    await api.updateBundleMetadata(bundleId, dto);
    await load();
  }

  async function deleteBundle(bundleId: string) {
    await api.deleteBundle(bundleId);
    bundles.value = bundles.value.filter(b => b.bundleId !== bundleId);
  }

  async function recordOpened(bundleId: string) {
    await api.recordBundleOpened(bundleId);
  }

  return { bundles, loading, error, load, updateMetadata, deleteBundle, recordOpened };
}
```

---

## Step 4 — Components

### `tracer-viewer/src/components/SqlEditor.vue` (NEW)

Full CodeMirror 6 wrapper. Follow the design in `docs/tracer_phase10_design.md §5.2` closely. Key requirements:
- `modelValue: string` prop and `update:modelValue` emit
- `schema: SqlSchemaDto | null` prop for autocomplete
- `run` event emitted on Cmd+Enter (Mod+Enter in CodeMirror)
- Uses `sql({ dialect: SQLite })` from `@codemirror/lang-sql` 
- Uses `oneDark` theme from `@codemirror/theme-one-dark`
- Uses `autocompletion({ override: [customCompletions] })` to suggest table/column names
- `history()` and `historyKeymap` from `@codemirror/commands`
- `lineNumbers()`, `highlightActiveLine()` from `@codemirror/view`
- `searchKeymap` from `@codemirror/search`
- `defineExpose({ focus, getSelection })`
- `onBeforeUnmount` destroys editor
- watches `modelValue` to sync external changes

### `tracer-viewer/src/components/SqlResultTable.vue` (NEW)

Scrollable result table with pivot affordances. Follow `docs/tracer_phase10_design.md §5.4`:
- Props: `result: SqlExecuteResultDto`, `sessionId: string`
- Columns header with sort on click (toggleSort)
- `sortedRows` computed
- Pivot columns: `event_id` → timeline, `entity_id` → entity-history, `trace_id` → causal-by-trace, `publish_wallclock` → timeline ±2s
- Export CSV button (builds blob URL, triggers download, revokes)
- Null values show as `∅`
- Row count in header

### `tracer-viewer/src/components/SqlResultChart.vue` (NEW)

Simple chart view using the existing canvas renderer pattern OR simple inline Chart (no new charting library — use a simple canvas bar chart similar to Phase 9's histogram renderer, OR just render a simple table-based chart with CSS bars). 

Requirements:
- Props: `result: SqlExecuteResultDto`
- If first column is string-ish (VARCHAR) and second is numeric → bar chart (label vs value)
- If first column is timestamp → time series line (simple canvas)
- Falls back to "Cannot chart this result shape" message
- Must NOT require any new npm package

Simplest approach: inline SVG bar chart based on computed data. Example:
```vue
<script setup lang="ts">
import { computed } from 'vue';
import type { SqlExecuteResultDto } from '@/types/sql';

const props = defineProps<{ result: SqlExecuteResultDto }>();

const chartData = computed(() => {
  if (!props.result.columns || !props.result.rows || props.result.columns.length < 2) return null;
  const labelCol = 0;
  const valueCol = props.result.columns.findIndex((c, i) => i > 0 && isNumeric(c.duckType));
  if (valueCol < 0) return null;
  
  const items = (props.result.rows ?? []).slice(0, 30).map(r => ({
    label: String(r[labelCol] ?? ''),
    value: Number(r[valueCol] ?? 0),
  }));
  const maxVal = Math.max(...items.map(i => i.value), 1);
  return { items, maxVal, valueLabel: props.result.columns[valueCol].name };
});

function isNumeric(t: string): boolean {
  return /double|float|int|decimal|bigint|hugeint/i.test(t);
}
</script>

<template>
  <div class="sql-chart">
    <div v-if="!chartData" class="sql-chart__empty">Cannot chart this result shape. Try a query with a label column and a numeric column.</div>
    <div v-else class="sql-chart__bars">
      <div v-for="item in chartData.items" :key="item.label" class="sql-chart__row">
        <span class="sql-chart__label" :title="item.label">{{ item.label }}</span>
        <div class="sql-chart__bar-wrap">
          <div class="sql-chart__bar" :style="{ width: `${(item.value / chartData.maxVal) * 100}%` }" />
        </div>
        <span class="sql-chart__value">{{ item.value.toLocaleString() }}</span>
      </div>
    </div>
  </div>
</template>

<style lang="scss">
.sql-chart {
  &__empty { color: var(--c-text-muted); padding: 2rem; text-align: center; }
  &__bars { display: flex; flex-direction: column; gap: 0.25rem; }
  &__row { display: grid; grid-template-columns: 180px 1fr 80px; align-items: center; gap: 0.5rem; font-size: 0.8rem; }
  &__label { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; color: var(--c-text-muted); }
  &__bar-wrap { background: var(--c-bg-subtle); border-radius: 2px; height: 16px; overflow: hidden; }
  &__bar { height: 100%; background: var(--c-accent); border-radius: 2px; transition: width 0.2s; }
  &__value { font-family: var(--font-mono); text-align: right; }
}
</style>
```

### `tracer-viewer/src/components/SchemaPanel.vue` (NEW)

Left sidebar showing tables and columns from SqlSchemaDto:
- Props: `schema: SqlSchemaDto | null`
- Emits: `insert: string` (text to insert at cursor)
- Collapsible table nodes (show/hide columns on click)
- Click table name → emits `FROM table_name`
- Click column name → emits the column name
- Shows DuckType in muted text
- Skeleton/placeholder when schema is null

```vue
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
          <li v-for="c in t.columns" :key="c.name" class="schema-panel__column" @click="emit('insert', c.name)">
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
  &__title { margin: 0 0 0.5rem; font-size: 0.85rem; font-weight: 600; }
  &__empty { color: var(--c-text-muted); }
  &__tables { list-style: none; padding: 0; margin: 0; }
  &__table { margin-bottom: 0.25rem; }
  &__table-row { display: flex; align-items: center; gap: 0.25rem; cursor: pointer; padding: 2px 4px; border-radius: 4px;
    &:hover { background: var(--c-bg-subtle); } }
  &__expand { color: var(--c-text-muted); width: 12px; }
  &__table-name { font-weight: 600; color: var(--c-accent);
    &:hover { text-decoration: underline; } }
  &__columns { list-style: none; padding: 0 0 0 1.25rem; margin: 0; }
  &__column { display: flex; gap: 0.5rem; align-items: baseline; padding: 1px 4px; border-radius: 4px; cursor: pointer;
    &:hover { background: var(--c-bg-subtle); } }
  &__col-name { color: var(--c-text); }
  &__col-type { color: var(--c-text-muted); font-size: 0.7rem; }
  &__notes { margin-top: 1rem; border-top: 1px solid var(--c-border); padding-top: 0.5rem;
    h5 { font-size: 0.75rem; margin: 0 0 0.25rem; font-weight: 600; }
    ul { list-style: disc; padding-left: 1rem; margin: 0; color: var(--c-text-muted); line-height: 1.5; font-size: 0.75rem; } }
}
</style>
```

### `tracer-viewer/src/components/SavedQueryPicker.vue` (NEW)

Modal dialog for browsing and selecting a saved query:
- No props beyond emit
- Emits: `select: { sql: string; savedQueryId: string }`, `cancel: []`
- On mount: loads all saved queries via `useSavedQueries().load()`
- Search input filtering by label
- Tab bar: All / Built-in / Favorites
- Each row: label, tags (badges), run count, favorite icon
- Click row → emits `select`
- "Clone" button on built-in rows
- Keyboard: Escape → cancel

### `tracer-viewer/src/components/BundleCard.vue` (NEW)

Bundle card for the library grid. Follow `docs/tracer_phase10_design.md §7.1` closely:
- Props: `bundle: BundleLibraryEntryDto`
- Emits: `open`, `edit`, `delete`, `archive`, `export`
- Shows label, description, session range (formatRelative from utils), size (formatBytes from utils), tags, lastOpenedAtUtc
- Stale badge if not opened in 30+ days
- Archived badge if `isArchived`
- Footer action buttons: Open, Edit, Export, Archive/Unarchive, Delete

Add helper utilities to `tracer-viewer/src/utils/format.ts` (create if not exists, or append):
```typescript
export function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  if (bytes < 1024 * 1024 * 1024) return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  return `${(bytes / (1024 * 1024 * 1024)).toFixed(2)} GB`;
}

export function formatRelative(utcString: string | undefined | null): string {
  if (!utcString) return '—';
  const delta = Date.now() - new Date(utcString).getTime();
  const sec = Math.floor(delta / 1000);
  if (sec < 60) return `${sec}s ago`;
  const min = Math.floor(sec / 60);
  if (min < 60) return `${min}m ago`;
  const hr = Math.floor(min / 60);
  if (hr < 24) return `${hr}h ago`;
  const days = Math.floor(hr / 24);
  return `${days}d ago`;
}

export function formatDateRange(from: string, to: string): string {
  const f = new Date(from);
  const t = new Date(to);
  const date = f.toLocaleDateString();
  const fromTime = f.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
  const toTime = t.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
  return `${date} ${fromTime}–${toTime}`;
}
```

Check if `tracer-viewer/src/utils/format.ts` already exists. If yes, append the new helpers only if not already present.

### `tracer-viewer/src/components/BundleFilterPanel.vue` (NEW)

Left filter panel for BundleLibraryView:
- Props: `tags: string[]`
- v-model:filter (type: `{ tags: string[]; showArchived: boolean; query: string; fromDate: Date | null; toDate: Date | null }`)
- Tag checkboxes
- "Show archived" checkbox  
- Date range pickers (simple `<input type="date">`)
- "Clear filters" button

### `tracer-viewer/src/components/BundleMetadataEditor.vue` (NEW)

Modal for editing bundle label/description/tags:
- Props: `bundle: BundleLibraryEntryDto`
- Emits: `save: { label?: string; description?: string; tags?: string[]; isArchived?: boolean }`, `cancel`
- Pre-fills from bundle
- Tags: comma-separated text input
- Save / Cancel buttons

### `tracer-viewer/src/components/ShowSqlButton.vue` (NEW)

Small affordance button. Follow `docs/tracer_phase10_design.md §8.1`:
```vue
<script setup lang="ts">
import { useRouter } from 'vue-router';

const props = defineProps<{ sql: string; sessionId: string }>();
const router = useRouter();

function open() {
  router.push({
    name: 'sql-console',
    params: { sessionId: props.sessionId },
    query: { sql: props.sql },
  });
}
</script>

<template>
  <button class="show-sql-btn" @click="open" title="Open the current filter as SQL in the SQL Console">
    Show SQL
  </button>
</template>

<style lang="scss">
.show-sql-btn {
  padding: 0.25rem 0.6rem;
  font-size: 0.75rem;
  background: var(--c-bg-subtle);
  border: 1px solid var(--c-border);
  border-radius: 4px;
  color: var(--c-text-muted);
  cursor: pointer;
  &:hover { color: var(--c-text); background: var(--c-bg-surface); }
}
</style>
```

---

## Step 5 — Views

### `tracer-viewer/src/views/SqlConsoleView.vue` (NEW)

Full SQL Console view. Follow `docs/tracer_phase10_design.md §5.3` closely:
- Route: `/v/sql/:sessionId`
- Route name: `sql-console`
- Props: `sessionId: string`
- Layout: toolbar + 3-column grid (schema panel left, editor+results center, history right)
- Uses `useSqlExecution`, `useSqlSchema`
- `loadInitialSql()`: reads `?sql=` query param first, then last localStorage history, then placeholder
- History: last 50 queries, persisted to localStorage key `tracer:sqlHistory`
- Result tabs: Table / Chart (chart tab disabled if result is not chartable)
- Explains via `api.explainSql` → alert (acceptable for MVP)
- ShowSqlButton NOT needed in this view (it IS the SQL view)
- "Save query" button: opens a small inline form (label input + Save/Cancel), calls `api.createSavedQuery`
- "Saved queries…" button → shows `<SavedQueryPicker>` modal

### `tracer-viewer/src/views/SavedQueriesView.vue` (NEW)

Route: `/saved-queries`  
Route name: `saved-queries`  
No props (not session-scoped — queries are global)

- Loads all saved queries via `useSavedQueries`
- Filter bar: text search (by label), tag filter, "Favorites only" toggle, "Built-in only" toggle  
- Each query row: label, description, tags (badges), author, runCount, lastRunAtUtc, isFavorite (star icon), isBuiltIn (lock icon)
- Actions per row: Run (opens SqlConsole with that SQL), Edit (inline form, disabled for built-ins), Delete (disabled for built-ins), Favorite (toggle), Clone (for built-ins)
- "New query" button → opens inline create form

### `tracer-viewer/src/views/BundleLibraryView.vue` (NEW)

Route: `/bundles/library`  
Route name: `bundle-library`  
No props

Follow `docs/tracer_phase10_design.md §7.2` closely:
- Header with title "Bundle library", search input, sort select + direction toggle
- Left: `<BundleFilterPanel>` 
- Main: grid of `<BundleCard>` components
- Empty state messages ("No bundles yet" vs "No bundles match filter")
- `<BundleMetadataEditor>` modal when `editing !== null`
- `openBundle` → calls `recordBundleOpened`, navigates to `/scenario/:sessionId`
- `exportBundle` → navigates to `api.getBundleDownloadUrl(bundleId)`
- `archiveBundle` → calls `updateBundleMetadata({ isArchived: true })`
- `deleteBundle` → confirm dialog → delete
- Loads on mount

---

## Step 6 — Utils

### `tracer-viewer/src/utils/showSqlGenerators.ts` (NEW)

SQL generators for "Show SQL" affordances on each analytical view. Follow `docs/tracer_phase10_design.md §8.1`:

```typescript
// tracer-viewer/src/utils/showSqlGenerators.ts

function sqlEscape(s: string): string { return s.replace(/'/g, "''"); }

export interface TimelineFilterForSql {
  from: string; to: string;
  topic?: string; publisherNode?: string; subscriberNode?: string;
  traceId?: string; entityId?: string;
}

export function timelineFilterToSql(f: TimelineFilterForSql): string {
  const clauses = [
    `publish_wallclock >= TIMESTAMP '${f.from}'`,
    `publish_wallclock < TIMESTAMP '${f.to}'`,
  ];
  if (f.topic) clauses.push(`topic = '${sqlEscape(f.topic)}'`);
  if (f.publisherNode) clauses.push(`publisher_node = '${sqlEscape(f.publisherNode)}'`);
  if (f.subscriberNode) clauses.push(`subscriber_node = '${sqlEscape(f.subscriberNode)}'`);
  if (f.entityId) clauses.push(`entity_id = '${sqlEscape(f.entityId)}'`);
  return `SELECT publish_wallclock, publisher_node, topic, event_id\nFROM events\nWHERE ${clauses.join('\n  AND ')}\nORDER BY publish_wallclock\nLIMIT 1000;`;
}

export function entityHistoryFilterToSql(entityId: string, from: string, to: string): string {
  return `SELECT event_id, topic, publisher_node, publish_wallclock\nFROM events\nWHERE entity_id = '${sqlEscape(entityId)}'\n  AND publish_wallclock >= TIMESTAMP '${from}'\n  AND publish_wallclock < TIMESTAMP '${to}'\nORDER BY publish_wallclock;`;
}

export function latencyFilterToSql(from: string, to: string, topic?: string): string {
  const clauses = [`publish_wallclock >= TIMESTAMP '${from}'`, `publish_wallclock < TIMESTAMP '${to}'`, `publisher_node != subscriber_node`];
  if (topic) clauses.push(`topic = '${sqlEscape(topic)}'`);
  return `SELECT topic, publisher_node, subscriber_node,\n  APPROX_QUANTILE((EXTRACT(EPOCH FROM receive_wallclock - publish_wallclock) * 1000), 0.5) AS p50_ms,\n  APPROX_QUANTILE((EXTRACT(EPOCH FROM receive_wallclock - publish_wallclock) * 1000), 0.99) AS p99_ms\nFROM events\nWHERE ${clauses.join('\n  AND ')}\nGROUP BY topic, publisher_node, subscriber_node\nORDER BY p99_ms DESC;`;
}

export function gapFilterToSql(from: string, to: string, topic?: string): string {
  const topicClause = topic ? `\n  AND topic = '${sqlEscape(topic)}'` : '';
  return `SELECT topic, publisher_node, subscriber_node, sequence_number\nFROM events\nWHERE publish_wallclock >= TIMESTAMP '${from}'\n  AND publish_wallclock < TIMESTAMP '${to}'${topicClause}\nORDER BY topic, publisher_node, subscriber_node, sequence_number;`;
}

export function topologyFilterToSql(from: string, to: string): string {
  return `SELECT publisher_node, subscriber_node, topic, COUNT(*) AS message_count\nFROM events\nWHERE publish_wallclock >= TIMESTAMP '${from}'\n  AND publish_wallclock < TIMESTAMP '${to}'\nGROUP BY publisher_node, subscriber_node, topic\nORDER BY message_count DESC;`;
}
```

---

## Step 7 — Update Router

In `tracer-viewer/src/router/index.ts`, add these routes to the routes array:

```typescript
{
  path: '/v/sql/:sessionId',
  name: 'sql-console',
  component: () => import('@/views/SqlConsoleView.vue'),
  props: true,
},
{
  path: '/saved-queries',
  name: 'saved-queries',
  component: () => import('@/views/SavedQueriesView.vue'),
},
{
  path: '/bundles/library',
  name: 'bundle-library',
  component: () => import('@/views/BundleLibraryView.vue'),
},
```

---

## Step 8 — Add ShowSqlButton to Analytical Views

Add `ShowSqlButton` to the toolbar of these existing views. In each, import `ShowSqlButton` and `showSqlGenerators`, and insert the button:

### `TimelineView.vue`
- Import `ShowSqlButton` and `timelineFilterToSql`
- Add after existing toolbar buttons: `<ShowSqlButton :sql="currentSql" :session-id="sessionId" />`
- Compute `currentSql` from the current filter (from/to/topic/node etc.) using `timelineFilterToSql`

### `ReplicationLatencyView.vue`
- Import `ShowSqlButton` and `latencyFilterToSql`
- Add `<ShowSqlButton :sql="currentSql" :session-id="sessionId" />` in toolbar
- `currentSql` computed from current filter

### `GapDetectionView.vue`
- Import `ShowSqlButton` and `gapFilterToSql`
- Add `<ShowSqlButton :sql="currentSql" :session-id="sessionId" />` in toolbar

### `NetworkTopologyView.vue`
- Import `ShowSqlButton` and `topologyFilterToSql`
- Add `<ShowSqlButton :sql="currentSql" :session-id="sessionId" />` in toolbar

### `EntityHistoryView.vue`
- Import `ShowSqlButton` and `entityHistoryFilterToSql`
- Add `<ShowSqlButton :sql="currentSql" :session-id="sessionId" />` in toolbar

For views where the current filter details are available in a composable, compute the SQL from those. If filter details are not readily available, use the view's route params and a simple time range (session start to now).

---

## Step 9 — Tests (TRC-P10-018)

### `tracer-viewer/tests/unit/useSqlExecution.spec.ts` (NEW)

Minimum 8 tests:
- `run` sets `loading` to true then false
- successful result sets `result.value` with state 'Succeeded'
- `cancel()` aborts in-flight request (mock AbortController)
- error from fetch sets `error.value`
- `run` clears previous result before new fetch
- result with state 'Rejected' is still set (no throw)
- result with state 'Timeout' is set
- `loading` is false after error

Pattern: mock `api.executeSql` using `vi.spyOn`. Use `vi.fn()` for the fetch stub.

### `tracer-viewer/tests/unit/useSqlSchema.spec.ts` (NEW)

Minimum 5 tests:
- `schema` initially null
- after `refresh()`, schema is set
- `loading` transitions to false after refresh
- error during fetch leaves schema null
- calling `refresh()` twice only fetches once (deduplicated by loading guard) — or just verify it can be called twice without crashing

### `tracer-viewer/tests/unit/useSavedQueries.spec.ts` (NEW)

Minimum 6 tests:
- `load()` populates `queries`
- `create()` appends the new query
- `remove(id)` removes from list
- `toggleFavorite(id)` updates the matching query
- `clone(id, label)` appends the cloned query
- `load()` with builtIn filter calls API with builtIn param

### `tracer-viewer/tests/unit/useBundleLibrary.spec.ts` (NEW)

Minimum 6 tests:
- `load()` populates `bundles`
- `load()` with `showArchived: false` filters correctly (or passes param to API)
- `updateMetadata()` calls API then reloads
- `deleteBundle(id)` removes from list
- `recordOpened()` calls API
- error from API sets `error.value`

### `tracer-viewer/tests/unit/SqlResultTable.spec.ts` (NEW)

Minimum 7 tests:
- renders column headers
- renders correct row count
- null values show as `∅`
- clicking column header sorts ascending
- clicking same header sorts descending
- pivot column shown when result has event_id column
- export CSV button present when result has rows

Use `@vue/test-utils` mount with a fake `result` prop. Mock `useRouter` if needed.

### `tracer-viewer/tests/unit/SqlResultChart.spec.ts` (NEW)

Minimum 5 tests:
- no-data shows empty message
- result with string + numeric columns renders chart
- chart shows correct number of bars
- result with only numeric column (no label) shows empty message
- top-30 limit applied (only 30 bars max)

### `tracer-viewer/tests/unit/showSqlGenerators.spec.ts` (NEW)

Minimum 6 tests:
- `timelineFilterToSql` includes FROM/TO timestamps
- `timelineFilterToSql` includes topic clause when provided
- `timelineFilterToSql` single-quote-escapes topic with apostrophe
- `entityHistoryFilterToSql` includes entity_id
- `latencyFilterToSql` includes APPROX_QUANTILE
- `topologyFilterToSql` includes GROUP BY

### `tracer-viewer/tests/unit/SchemaPanel.spec.ts` (NEW)

Minimum 4 tests:
- shows "Loading…" when schema is null
- shows table names when schema provided
- click on table name emits insert
- toggle expands columns

### E2E Stubs `tracer-viewer/tests/e2e/sql-console.spec.ts` (NEW)

```typescript
import { test } from '@playwright/test';
test.skip('sql console e2e - requires running server', () => {});
test.describe('SQL Console E2E (stub)', () => {
  test.skip('execute simple query', async () => {});
  test.skip('rejected query shows error', async () => {});
  test.skip('Show SQL from timeline', async () => {});
});
```

```typescript
// tracer-viewer/tests/e2e/bundle-library.spec.ts
import { test } from '@playwright/test';
test.describe('Bundle Library E2E (stub)', () => {
  test.skip('library shows bundles', async () => {});
  test.skip('edit metadata persists', async () => {});
});
```

```typescript
// tracer-viewer/tests/e2e/saved-queries.spec.ts
import { test } from '@playwright/test';
test.describe('Saved Queries E2E (stub)', () => {
  test.skip('list shows built-in queries', async () => {});
  test.skip('run opens sql console', async () => {});
});
```

---

## Verification

```
cd d:\Work\Tracer\tracer-viewer
pnpm test:unit -- --reporter=verbose 2>&1 | Select-Object -Last 8
```

All existing tests must still pass. New Phase 10 frontend tests must pass.

Also verify TypeScript compilation:
```
cd d:\Work\Tracer\tracer-viewer
npx vue-tsc --noEmit 2>&1 | Select-Object -Last 10
```

---

## Report

Write report to: `d:\WORK\Tracer\.dev\tracer\reports\BATCH-52-REPORT.md`

Report must include:
- Files created/modified with brief description
- Test count (total, new Phase 10 tests)
- Any deviations from instructions
- Build/test output (last lines)
