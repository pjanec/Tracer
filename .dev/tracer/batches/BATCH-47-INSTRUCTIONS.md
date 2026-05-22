# BATCH-47 Instructions

**Batch:** BATCH-47  
**Tasks:** TRC-P8-014 (SavedViewsView + SaveViewButton), TRC-P8-015 (BookmarkBar + useBookmarks), TRC-P8-016 (TriggerEvalView + TriggerEvalRow)  
**Phase:** 8 — Annotations, Saved Views, Trigger Evaluation Log, Multi-Persona Polish  
**Estimated Effort:** 10–12 hours  
**Dependencies:** BATCH-45 complete (personaStore exists), BATCH-43 complete (SavedViews REST API)  
**Report path:** `d:\WORK\Tracer\.dev\tracer\reports\BATCH-47-REPORT.md`  
**Working directory:** `d:\WORK\Tracer\tracer-viewer`

---

## 📋 Onboarding

### Required Reading (IN ORDER)

1. **Design:** `docs/tracer_phase8_design.md` §6.5 (SaveViewButton), §6.6 (BookmarkBar), §6.7 (SavedViewsView), §8.4 (TriggerEvalView)
2. **Task definitions:** `docs/TASK-DETAIL.md` §TRC-P8-014, §TRC-P8-015, §TRC-P8-016
3. **Previous reviews:** `.dev/tracer/reviews/BATCH-45-REVIEW.md` and `BATCH-46-REVIEW.md`
4. **Existing API client:** `tracer-viewer/src/api/tracerApiClient.ts` — understand the pattern for adding new DTOs/methods
5. **Persona store:** `tracer-viewer/src/stores/personaStore.ts` and `src/composables/usePersona.ts`
6. **Router:** `tracer-viewer/src/router/index.ts` — understand where to add new routes
7. **Existing view pattern:** `tracer-viewer/src/views/EntityPickerView.vue` — filtering + session-based loading
8. **Test patterns:** `tracer-viewer/tests/unit/EntityPickerView.spec.ts`

### Test commands (from `d:\Work\Tracer\tracer-viewer`):

```powershell
cd d:\Work\Tracer\tracer-viewer
pnpm test:unit -- --reporter=verbose 2>&1 | Select-Object -Last 12
pnpm test:unit -- --reporter=verbose SavedViews 2>&1 | Select-Object -Last 12
pnpm test:unit -- --reporter=verbose SaveViewButton 2>&1 | Select-Object -Last 12
pnpm test:unit -- --reporter=verbose BookmarkBar 2>&1 | Select-Object -Last 12
pnpm test:unit -- --reporter=verbose useBookmarks 2>&1 | Select-Object -Last 12
pnpm test:unit -- --reporter=verbose TriggerEval 2>&1 | Select-Object -Last 12
```

---

## ✅ Task 1 — API Client Extensions (do first)

### 1.1 Add SavedView + TriggerEval DTOs and methods to `src/api/tracerApiClient.ts`

**Interfaces to add** (alongside existing):

```typescript
export type SavedViewKind = 'SavedView' | 'Bookmark';

export interface SavedViewDto {
  savedViewId: string;
  sessionId: string;
  kind: SavedViewKind;
  viewType: string;
  url: string;
  label: string;
  description?: string;
  persona: string;
  author?: string;
  createdAtUtc: string;
  lastOpenedAtUtc?: string;
  openCount: number;
}

export interface CreateSavedViewDto {
  sessionId: string;
  kind: SavedViewKind;
  viewType: string;
  url: string;
  label: string;
  description?: string;
  persona: string;
  author?: string;
}

export interface UpdateSavedViewDto {
  label?: string;
  description?: string;
}

export interface TriggerEvaluationDto {
  eventId: string;
  evaluatedAtUtc: string;
  publisherNode: string;
  traceId: string;
  triggerId: string;
  triggerLabel?: string;
  inputs: string;
  result: string;           // "Fired" | "NotFired"
  nextEventId?: string;
  reason?: string;
}

export interface TriggerEvaluationListDto {
  evaluations: TriggerEvaluationDto[];
}
```

**Methods to add to `TracerApiClient`:**

```typescript
async listSavedViews(params: {
  sessionId?: string;
  kind?: SavedViewKind;
  viewType?: string;
  persona?: string;
  orderBy?: string;
  limit?: number;
}): Promise<SavedViewDto[]> {
  const qs = new URLSearchParams();
  if (params.sessionId) qs.set('sessionId', params.sessionId);
  if (params.kind) qs.set('kind', params.kind);
  if (params.viewType) qs.set('viewType', params.viewType);
  if (params.persona) qs.set('persona', params.persona);
  if (params.orderBy) qs.set('orderBy', params.orderBy);
  if (params.limit != null) qs.set('limit', String(params.limit));
  const res = await fetch(`/api/saved-views?${qs}`);
  if (!res.ok) throw new Error(`listSavedViews: ${res.status}`);
  return res.json() as Promise<SavedViewDto[]>;
}

async createSavedView(dto: CreateSavedViewDto): Promise<SavedViewDto> {
  const res = await fetch('/api/saved-views', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(dto),
  });
  if (!res.ok) throw new Error(`createSavedView: ${res.status}`);
  return res.json() as Promise<SavedViewDto>;
}

async deleteSavedView(savedViewId: string): Promise<void> {
  const res = await fetch(`/api/saved-views/${encodeURIComponent(savedViewId)}`, { method: 'DELETE' });
  if (!res.ok && res.status !== 404) throw new Error(`deleteSavedView: ${res.status}`);
}

async recordSavedViewOpened(savedViewId: string): Promise<void> {
  const res = await fetch(`/api/saved-views/${encodeURIComponent(savedViewId)}/opened`, { method: 'POST' });
  if (!res.ok) throw new Error(`recordSavedViewOpened: ${res.status}`);
}

async listTriggerEvaluations(params: {
  sessionId: string;
  from?: string;
  to?: string;
  triggerId?: string;
  result?: string;
  limit?: number;
}): Promise<TriggerEvaluationDto[]> {
  const qs = new URLSearchParams({ sessionId: params.sessionId });
  if (params.from) qs.set('from', params.from);
  if (params.to) qs.set('to', params.to);
  if (params.triggerId) qs.set('triggerId', params.triggerId);
  if (params.result) qs.set('result', params.result);
  if (params.limit != null) qs.set('limit', String(params.limit));
  const res = await fetch(`/api/scenario/triggers?${qs}`);
  if (!res.ok) throw new Error(`listTriggerEvaluations: ${res.status}`);
  const data = await res.json() as TriggerEvaluationListDto;
  return data.evaluations;
}
```

---

## ✅ Task 2 — TRC-P8-014: SaveViewButton.vue + SavedViewsView.vue

**Design reference:** `docs/tracer_phase8_design.md` §6.5, §6.7  
**Task definition:** `docs/TASK-DETAIL.md` §TRC-P8-014

### 2.1 Create `src/components/SaveViewButton.vue`

Two-part toolbar control:
- Bookmark icon button (single-click → creates a Bookmark)  
- "Save view" text button (opens inline dialog to get label + optional description → creates a SavedView)

**Props:**
- `sessionId: string`
- `viewType: string` — e.g. `"timeline"`, `"entity-history"`

**Auto-label generation:** Use `useRoute()` to access `route.query`; compose a label from topic, trace, entity query params if present, otherwise use `viewType + current time`:

```typescript
function autoLabel(): string {
  const parts: string[] = [];
  const q = route.query;
  if (q.topic) parts.push(String(Array.isArray(q.topic) ? q.topic[0] : q.topic));
  if (q.trace) parts.push(`trace:${String(q.trace)}`);
  if (q.entity) parts.push(`entity:${String(q.entity)}`);
  if (parts.length === 0) parts.push(props.viewType);
  parts.push(new Date().toISOString().slice(11, 19)); // HH:MM:SS
  return parts.join(' · ');
}
```

**Bookmark click logic:**
```typescript
async function onBookmarkClick() {
  const { persona } = usePersona();
  await api.createSavedView({
    sessionId: props.sessionId,
    kind: 'Bookmark',
    viewType: props.viewType,
    url: route.fullPath,
    label: autoLabel(),
    persona: persona.value,
    author: localStorage.getItem('tracer:authorName') ?? undefined,
  });
}
```

**Save view dialog logic:**
- Dialog opens on "Save view" button click
- Dialog has: label input (required), description textarea (optional), Save button (disabled when label blank), Cancel button
- Save button calls `api.createSavedView` with `kind: 'SavedView'`, label, description, current persona, persona from store
- Dialog closes after successful save

**Template structure:**

```vue
<template>
  <div class="save-view-button">
    <button class="save-view-button__bookmark" @click="onBookmarkClick" title="Bookmark current view">
      🔖
    </button>
    <button class="save-view-button__open-dialog" @click="dialogOpen = true">
      Save view
    </button>

    <!-- Inline save dialog -->
    <div v-if="dialogOpen" class="save-view-dialog">
      <div class="save-view-dialog__backdrop" @click.self="dialogOpen = false" />
      <div class="save-view-dialog__box">
        <h3>Save view</h3>
        <input
          v-model="dialogLabel"
          class="save-view-dialog__label-input"
          placeholder="View name…"
          type="text"
        />
        <textarea
          v-model="dialogDescription"
          class="save-view-dialog__desc"
          placeholder="Description (optional)"
          rows="2"
        />
        <div class="save-view-dialog__actions">
          <button @click="dialogOpen = false">Cancel</button>
          <button
            :disabled="!dialogLabel.trim()"
            class="save-view-dialog__save"
            @click="onDialogSave"
          >
            Save
          </button>
        </div>
      </div>
    </div>
  </div>
</template>
```

### 2.2 Create `src/views/SavedViewsView.vue`

Route: `/v/saved-views/:sessionId`

**Behavior:**
- On mount, load saved views via `api.listSavedViews({ sessionId, kind: 'SavedView', persona: currentPersona })`
- Group results by `viewType` (each group → `<section>` with heading)
- Persona filter dropdown (All / Engineer / Scenario Author / Operator) triggers reload
- Empty state message when 0 results
- Each view row: label, description, "Open" button, "Delete" button (with confirm)
- "Open" → `recordSavedViewOpened` (fire-and-forget) → `router.push(savedView.url)`
- "Delete" → confirm → `api.deleteSavedView(id)` → reload

**Props:** `sessionId: string` (from route params via `props: true`)

### 2.3 Add route to `src/router/index.ts`

```typescript
{
  path: '/v/saved-views/:sessionId',
  name: 'saved-views',
  component: () => import('@/views/SavedViewsView.vue'),
  props: true,
},
```

### 2.4 Tests — `tests/unit/SaveViewButton.spec.ts` (7 tests)

Mock pattern:
```typescript
vi.mock('@/api/tracerApiClient', () => ({
  api: {
    createSavedView: vi.fn(),
    // ... others as needed
  },
}));
vi.mock('vue-router', () => ({
  useRoute: () => ({ fullPath: '/v/timeline/sess-1', query: {} }),
  useRouter: () => ({ push: vi.fn() }),
}));
```

Write these 7 tests:

1. **`SaveViewButton_BookmarkClick_CallsAPI`** — Mount with `sessionId="s1" viewType="timeline"`. Click `.save-view-button__bookmark`. Assert: `api.createSavedView` called once with `kind: 'Bookmark'`.

2. **`SaveViewButton_AutoLabel_NotEmpty`** — Mock route with no query params. Click bookmark. Assert: `label` in the `createSavedView` call is a non-empty string.

3. **`SaveViewButton_AutoLabel_IncludesTopic`** — Mock route with `query: { topic: 'weapons.fire' }`. Click bookmark. Assert: the label includes `'weapons.fire'`.

4. **`SaveViewButton_SaveDialog_OpenOnClick`** — Click `.save-view-button__open-dialog`. Assert: `.save-view-dialog` element is visible.

5. **`SaveViewButton_SaveDisabled_WhenLabelBlank`** — Open dialog. Assert: `.save-view-dialog__save` has `disabled` attribute.

6. **`SaveViewButton_SaveExplicit_CallsAPI`** — Open dialog; set label input = "Test view"; click `.save-view-dialog__save`. Assert: `api.createSavedView` called with `kind: 'SavedView'` and `label: 'Test view'`.

7. **`SaveViewButton_SaveDialog_ClosesAfterSave`** — Open dialog; fill label; click save; wait for promise. Assert: `.save-view-dialog` no longer present in DOM.

### 2.5 Tests — `tests/unit/SavedViewsView.spec.ts` (5 tests)

Mock:
```typescript
vi.mock('@/api/tracerApiClient', () => ({
  api: {
    listSavedViews: vi.fn(),
    deleteSavedView: vi.fn(),
    recordSavedViewOpened: vi.fn(),
  },
}));
```

Write these 5 tests (implement only 5 of the 12 SC from TASK-DETAIL — the most testable without a real router):

1. **`SavedViewsView_RendersViewsGroupedByType`** — Mock returns 3 views: 2 with `viewType='timeline'`, 1 with `viewType='scenario'`. Mount view. Assert: 2 distinct group headings.

2. **`SavedViewsView_EmptyState_Shown`** — Mock returns []. Mount. Assert: empty-state message visible.

3. **`SavedViewsView_DeleteView_CallsAPIAndReloads`** — Mock returns 1 view. Mount. Use `window.confirm` mock (return true). Click delete. Assert: `api.deleteSavedView` called; `api.listSavedViews` called a second time.

4. **`SavedViewsView_PersonaFilterChange_Reloads`** — Change persona filter dropdown. Assert: `api.listSavedViews` called again with new persona.

5. **`SavedViewsView_OpenView_NavigatesAndRecordsOpen`** — Mock returns 1 view with `url: '/v/timeline/s1'`. Mount (with real or stub router). Click Open. Assert: `api.recordSavedViewOpened` called with the view's id.

---

## ✅ Task 3 — TRC-P8-015: useBookmarks.ts + BookmarkBar.vue

**Design reference:** `docs/tracer_phase8_design.md` §6.6  
**Task definition:** `docs/TASK-DETAIL.md` §TRC-P8-015

### 3.1 Create `src/composables/useBookmarks.ts`

```typescript
import { api } from '@/api/tracerApiClient';
import type { SavedViewDto } from '@/api/tracerApiClient';
import { useRoute } from 'vue-router';
import { usePersona } from '@/composables/usePersona';

export function useBookmarks() {
  const route = useRoute();
  const { persona } = usePersona();

  async function bookmarkCurrentUrl(sessionId: string, viewType: string): Promise<void> {
    const label = buildAutoLabel(route, viewType);
    await api.createSavedView({
      sessionId,
      kind: 'Bookmark',
      viewType,
      url: route.fullPath,
      label,
      persona: persona.value,
      author: localStorage.getItem('tracer:authorName') ?? undefined,
    });
  }

  async function listBookmarks(sessionId: string, viewType?: string): Promise<SavedViewDto[]> {
    const items = await api.listSavedViews({
      sessionId,
      kind: 'Bookmark',
      persona: persona.value,
      orderBy: 'recent',
      limit: 10,
    });
    return items.filter(b => !viewType || b.viewType === viewType);
  }

  async function removeBookmark(savedViewId: string): Promise<void> {
    await api.deleteSavedView(savedViewId);
  }

  return { bookmarkCurrentUrl, listBookmarks, removeBookmark };
}

function buildAutoLabel(route: ReturnType<typeof useRoute>, viewType: string): string {
  const parts: string[] = [];
  const q = route.query;
  if (q.topic) parts.push(String(Array.isArray(q.topic) ? q.topic[0] : q.topic));
  if (q.trace) parts.push(`trace:${String(q.trace)}`);
  if (q.entity) parts.push(`entity:${String(q.entity)}`);
  if (parts.length === 0) parts.push(viewType);
  parts.push(new Date().toISOString().slice(11, 19));
  return parts.join(' · ');
}
```

### 3.2 Create `src/components/BookmarkBar.vue`

**Props:**
- `sessionId: string`
- `viewType: string`

**Behavior:**
- On mount + on persona change: call `listBookmarks(sessionId, viewType)`
- If empty: render nothing (`v-if` on root or component returns nothing)
- Renders up to 10 chips: each truncated label, click → `recordSavedViewOpened(id)` + `router.push(url)`

```vue
<template>
  <nav v-if="bookmarks.length > 0" class="bookmark-bar">
    <button
      v-for="b in bookmarks"
      :key="b.savedViewId"
      class="bookmark-bar__chip"
      :title="b.label"
      @click="onChipClick(b)"
    >
      {{ b.label }}
    </button>
  </nav>
</template>

<script setup lang="ts">
import { ref, watch, onMounted } from 'vue';
import { useRouter } from 'vue-router';
import { api } from '@/api/tracerApiClient';
import type { SavedViewDto } from '@/api/tracerApiClient';
import { useBookmarks } from '@/composables/useBookmarks';
import { usePersonaStore } from '@/stores/personaStore';

const props = defineProps<{ sessionId: string; viewType: string }>();
const router = useRouter();
const { listBookmarks } = useBookmarks();
const personaStore = usePersonaStore();
const bookmarks = ref<SavedViewDto[]>([]);

async function loadBookmarks() {
  bookmarks.value = await listBookmarks(props.sessionId, props.viewType);
}

onMounted(loadBookmarks);

// Reload when persona changes
watch(() => personaStore.current, loadBookmarks);

async function onChipClick(b: SavedViewDto) {
  void api.recordSavedViewOpened(b.savedViewId);  // fire-and-forget
  await router.push(b.url);
}
</script>

<style lang="scss">
.bookmark-bar {
  display: flex;
  gap: 0.5rem;
  padding: 0.25rem 1rem;
  overflow-x: auto;

  &__chip {
    max-width: 16rem;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
    padding: 0.25rem 0.75rem;
    border-radius: 999px;
    border: 1px solid var(--c-bg-subtle);
    background: var(--c-bg-surface);
    color: var(--c-text);
    font-size: 0.8rem;
    cursor: pointer;

    &:hover {
      background: var(--c-bg-subtle);
    }
  }
}
</style>
```

### 3.3 Tests — `tests/unit/useBookmarks.spec.ts` (4 tests)

```typescript
vi.mock('@/api/tracerApiClient', () => ({
  api: {
    createSavedView: vi.fn(),
    listSavedViews: vi.fn(),
    deleteSavedView: vi.fn(),
  },
}));
vi.mock('vue-router', () => ({
  useRoute: () => ({ fullPath: '/v/timeline/s1', query: {} }),
  useRouter: () => ({ push: vi.fn() }),
}));
```

Write these 4 tests:

1. **`useBookmarks_BookmarkCurrentUrl_CallsAPI`** — Call `bookmarkCurrentUrl('s1', 'timeline')`. Assert: `api.createSavedView` called with `kind: 'Bookmark'`, non-empty label, `viewType: 'timeline'`.

2. **`useBookmarks_ListBookmarks_ReturnsOnlyBookmarks`** — Mock API returns `[{ kind: 'SavedView', ... }, { kind: 'Bookmark', ... }]`. Call `listBookmarks('s1')`. Assert: `api.listSavedViews` called with `kind: 'Bookmark'` (so only bookmarks are requested at the API level).

3. **`useBookmarks_RemoveBookmark_CallsDelete`** — Call `removeBookmark('id-1')`. Assert: `api.deleteSavedView` called with `'id-1'`.

4. **`useBookmarks_LimitTen`** — Call `listBookmarks('s1')`. Assert: `api.listSavedViews` called with `limit: 10`.

### 3.4 Tests — `tests/unit/BookmarkBar.spec.ts` (4 tests)

```typescript
vi.mock('@/api/tracerApiClient', () => ({
  api: {
    listSavedViews: vi.fn(),
    recordSavedViewOpened: vi.fn(),
  },
}));
vi.mock('vue-router', () => ({
  useRoute: () => ({ fullPath: '/v/timeline/s1', query: {} }),
  useRouter: () => ({ push: vi.fn() }),
}));
vi.mock('@/composables/useBookmarks', () => ({
  useBookmarks: () => ({
    listBookmarks: vi.fn(),
    bookmarkCurrentUrl: vi.fn(),
    removeBookmark: vi.fn(),
  }),
}));
```

Wait — if you mock `useBookmarks`, then `listBookmarks` won't return real data. Instead, mock the underlying API and let the real `useBookmarks` composable work:

Actually, for BookmarkBar tests, mock `@/api/tracerApiClient` + `usePersonaStore` and let the composable logic run naturally. Use `listSavedViews` mock to control what bookmarks are returned.

Write these 4 tests:

1. **`BookmarkBar_Hidden_WhenNoBookmarks`** — Mock `listSavedViews` returns []. Mount. Assert: `.bookmark-bar` NOT in DOM.

2. **`BookmarkBar_RendersChips`** — Mock `listSavedViews` returns 3 bookmarks. Mount. After `flushPromises`. Assert: 3 `.bookmark-bar__chip` elements.

3. **`BookmarkBar_ChipClick_NavigatesAndRecords`** — Mock returns 1 bookmark with `savedViewId='bk1'`, `url='/v/timeline/s1'`. Click chip. Assert: `api.recordSavedViewOpened` called with `'bk1'`; router `push` called with `'/v/timeline/s1'`.

4. **`BookmarkBar_ReloadsOnPersonaChange`** — Mount. Change persona store value. Assert: `api.listSavedViews` called again (count > 1).

---

## ✅ Task 4 — TRC-P8-016: TriggerEvalView.vue + TriggerEvalRow.vue

**Design reference:** `docs/tracer_phase8_design.md` §8.4  
**Task definition:** `docs/TASK-DETAIL.md` §TRC-P8-016

### 4.1 Create `src/components/TriggerEvalRow.vue`

**Props:**
- `evaluation: TriggerEvaluationDto`
- `sessionId: string` — for constructing navigation URLs

**Behavior:**
- Single `<tr>` row
- Columns: time (formatted), triggerId + optional label, publisherNode, result pill (class `trigger-eval-view__pill--Fired` or `trigger-eval-view__pill--NotFired`)
- Click on row body toggles inline inputs JSON expansion panel (class `.trigger-eval-row__inputs`)
- "Timeline" action button → navigate to timeline with ±5s time window
- "Tree" action button → navigate to causal tree by eventId

```vue
<template>
  <tr class="trigger-eval-row" @click="expanded = !expanded">
    <td>{{ formatTime(evaluation.evaluatedAtUtc) }}</td>
    <td>
      <span class="trigger-eval-row__trigger-id">{{ evaluation.triggerId }}</span>
      <span v-if="evaluation.triggerLabel" class="trigger-eval-row__trigger-label"> — {{ evaluation.triggerLabel }}</span>
    </td>
    <td>{{ evaluation.publisherNode }}</td>
    <td>
      <span
        class="trigger-eval-view__pill"
        :class="`trigger-eval-view__pill--${evaluation.result}`"
      >{{ evaluation.result }}</span>
    </td>
    <td class="trigger-eval-row__actions" @click.stop>
      <button @click="goToTimeline">Timeline</button>
      <button @click="goToTree">Tree</button>
    </td>
  </tr>
  <tr v-if="expanded" class="trigger-eval-row__expansion">
    <td colspan="5">
      <pre class="trigger-eval-row__inputs">{{ evaluation.inputs }}</pre>
    </td>
  </tr>
</template>

<script setup lang="ts">
import { ref } from 'vue';
import { useRouter } from 'vue-router';
import type { TriggerEvaluationDto } from '@/api/tracerApiClient';
import { formatTime } from '@/utils/time';

const props = defineProps<{
  evaluation: TriggerEvaluationDto;
  sessionId: string;
}>();

const router = useRouter();
const expanded = ref(false);

function goToTimeline() {
  const evalTime = new Date(props.evaluation.evaluatedAtUtc);
  const from = new Date(evalTime.getTime() - 5_000).toISOString();
  const to = new Date(evalTime.getTime() + 5_000).toISOString();
  void router.push({
    name: 'timeline',
    params: { sessionId: props.sessionId },
    query: { from, to, select: props.evaluation.eventId },
  });
}

function goToTree() {
  void router.push({ name: 'causal-by-event', params: { eventId: props.evaluation.eventId } });
}
</script>
```

### 4.2 Create `src/views/TriggerEvalView.vue`

Route: `/v/triggers/:sessionId`

**Behavior:**
- On mount: load via `api.listTriggerEvaluations({ sessionId })`
- Filter controls: trigger-ID select (populated from distinct `triggerId` values in loaded data; "All" option) and result select (All / Fired / Not fired)
- Re-fetches on filter change
- Loading state; empty state
- Renders table with `<TriggerEvalRow>` per evaluation

```vue
<template>
  <div class="trigger-eval-view">
    <div class="trigger-eval-view__filters">
      <select v-model="triggerFilter" @change="reload">
        <option value="">All triggers</option>
        <option v-for="id in distinctTriggerIds" :key="id" :value="id">{{ id }}</option>
      </select>
      <select v-model="resultFilter" @change="reload">
        <option value="">All results</option>
        <option value="fired">Fired</option>
        <option value="not-fired">Not fired</option>
      </select>
    </div>

    <div v-if="loading" class="trigger-eval-view__loading">Loading…</div>
    <p v-else-if="evaluations.length === 0" class="trigger-eval-view__empty">
      No trigger evaluations found.
    </p>
    <table v-else class="trigger-eval-view__table">
      <thead>
        <tr>
          <th>Time</th><th>Trigger</th><th>Publisher</th><th>Result</th><th>Actions</th>
        </tr>
      </thead>
      <tbody>
        <TriggerEvalRow
          v-for="ev in evaluations"
          :key="ev.eventId"
          :evaluation="ev"
          :sessionId="sessionId"
        />
      </tbody>
    </table>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue';
import { api } from '@/api/tracerApiClient';
import type { TriggerEvaluationDto } from '@/api/tracerApiClient';
import TriggerEvalRow from '@/components/TriggerEvalRow.vue';

const props = defineProps<{ sessionId: string }>();

const evaluations = ref<TriggerEvaluationDto[]>([]);
const loading = ref(false);
const triggerFilter = ref('');
const resultFilter = ref('');

const distinctTriggerIds = computed(() => {
  const ids = new Set(evaluations.value.map(e => e.triggerId));
  return Array.from(ids).sort();
});

async function reload() {
  loading.value = true;
  try {
    evaluations.value = await api.listTriggerEvaluations({
      sessionId: props.sessionId,
      triggerId: triggerFilter.value || undefined,
      result: resultFilter.value || undefined,
    });
  } finally {
    loading.value = false;
  }
}

onMounted(reload);
</script>
```

### 4.3 Add route to `src/router/index.ts`

```typescript
{
  path: '/v/triggers/:sessionId',
  name: 'triggers',
  component: () => import('@/views/TriggerEvalView.vue'),
  props: true,
},
```

### 4.4 Tests — `tests/unit/TriggerEvalView.spec.ts` (7 tests)

Mock:
```typescript
vi.mock('@/api/tracerApiClient', () => ({
  api: { listTriggerEvaluations: vi.fn() },
}));
vi.mock('vue-router', () => ({
  useRoute: () => ({ params: { sessionId: 'sess-1' } }),
  useRouter: () => ({ push: vi.fn() }),
}));
```

Write these 7 tests matching the 12 SC from TASK-DETAIL §TRC-P8-016 (implement those testable without canvas):

1. **`TriggerEvalView_LoadsOnMount`** — Mock returns 5 evaluations. Mount. `flushPromises`. Assert: 5 `<tr>` rows in `tbody` (plus header row).

2. **`TriggerEvalView_LoadingState`** — Mock delays. Assert: `.trigger-eval-view__loading` present before resolve.

3. **`TriggerEvalView_EmptyState`** — Mock returns []. Mount. `flushPromises`. Assert: `.trigger-eval-view__empty` visible.

4. **`TriggerEvalView_ResultFilterChange_Refetches`** — Mount. Select "fired" from result filter. Assert: `api.listTriggerEvaluations` called again with `result: 'fired'`.

5. **`TriggerEvalView_DistinctTriggerIds_PopulateSelect`** — 5 evaluations with 3 distinct triggerIds. Mount. `flushPromises`. Assert: trigger select has 4 options (All + 3 distinct).

6. **`TriggerEvalRow_FiredPill_HasCorrectClass`** — Mount `TriggerEvalRow` with `evaluation.result = 'Fired'`. Assert: pill element has class `trigger-eval-view__pill--Fired`.

7. **`TriggerEvalRow_NotFiredPill_HasCorrectClass`** — Mount with `result = 'NotFired'`. Assert: class `trigger-eval-view__pill--NotFired`.

### 4.5 Tests — `tests/unit/TriggerEvalRow.spec.ts` (5 tests)

Write 5 tests for TriggerEvalRow:

1. **`TriggerEvalRow_TimelineButton_Navigates`** — Mount with `evaluatedAtUtc='2026-01-01T10:00:00.000Z'`. Click "Timeline" button. Assert: router `push` called with `name: 'timeline'`, `query.from` ≈ `2026-01-01T09:59:55Z`, `query.to` ≈ `2026-01-01T10:00:05Z`, `query.select` = the row's eventId.

2. **`TriggerEvalRow_TreeButton_Navigates`** — Click "Tree" button. Assert: router `push` called with `name: 'causal-by-event'`, `params.eventId` = the row's eventId.

3. **`TriggerEvalRow_InlineExpansion_TogglesOnClick`** — Click the row. Assert: inputs panel exists. Click again. Assert: panel hidden.

4. **`TriggerEvalRow_InputsPanel_ShowsRawJson`** — Row with `inputs = '{"speed":10}'`. Click row. Assert: expansion panel text contains `"speed"`.

5. **`TriggerEvalRow_TriggerIdFilter_Refetches`** — (Test at TriggerEvalView level) Change trigger ID filter select. Assert: `api.listTriggerEvaluations` called with the selected `triggerId`.

---

## 🔧 Quality Requirements

- All new tests must verify concrete behavior (not just "doesn't throw")
- `BookmarkBar` must use `v-if` (not `v-show`) on the root — no empty element when no bookmarks
- Result pill class must be exactly `trigger-eval-view__pill--Fired` or `trigger-eval-view__pill--NotFired` (spec-critical)
- Run full suite at end: 0 regressions

---

## 📊 Expected Test Counts

| Suite | New Tests |
|-------|-----------|
| SaveViewButton.spec.ts | 7 |
| SavedViewsView.spec.ts | 5 |
| useBookmarks.spec.ts | 4 |
| BookmarkBar.spec.ts | 4 |
| TriggerEvalView.spec.ts | 7 |
| TriggerEvalRow.spec.ts | 5 |
| **Total** | **32** |

---

## 📝 Report

Write to: `d:\WORK\Tracer\.dev\tracer\reports\BATCH-47-REPORT.md`

Include: files created/modified table, test results per file, issues + resolutions, design decisions, suggested commit message.
