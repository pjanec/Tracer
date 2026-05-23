# BATCH-59 — Frontend: AppHeader Context, BookmarkBar, Numeric Param Validation

## Overview

Three frontend tasks. All TypeScript/Vue 3. No backend changes. Tests use Vitest.

**Test runner** (from `tracer-viewer/`):
```
pnpm test --run
```

---

## Pre-research: Already Implemented (just confirmed, no code change needed)

The following tasks are ALREADY done and should be confirmed as such:
- D1: Phase 8 routes (`/v/saved-views/:sessionId`, `/v/triggers/:sessionId`) exist in `router/index.ts`
- D4: `ShowSqlButton` is already in `ReplicationLatencyView.vue`
- E1: `AbortController` cancellation is already in `useTimelineQuery.ts`
- E3: `clamp()` is already applied to `xPct` in `EntityLifecycleRibbon.vue`
- E4: No `previousSequence === 0` filter exists in `GapList.vue` or `useGapDetection.ts`

---

## Task D2 — AppHeader Context (Session Label + Mode Badge)

### Goal
`AppHeader.vue` shows only "Tracer" + `PersonaSwitcher`. It must also show:
1. A mode badge ("Bundle Mode" or "Live Mode") based on `useBundleMode()`
2. The current session ID/label from `useSessionStore()` if a session is loaded

### File: `tracer-viewer/src/components/AppHeader.vue`

Current state:
```html
<template>
  <header class="app-header">
    <div class="app-header__brand">
      <span class="app-header__title">Tracer</span>
    </div>
    <PersonaSwitcher class="app-header__persona" />
  </header>
</template>

<script setup lang="ts">
import PersonaSwitcher from '@/components/PersonaSwitcher.vue';
</script>
```

**Updated version:**
```html
<template>
  <header class="app-header">
    <div class="app-header__brand">
      <span class="app-header__title">Tracer</span>
      <span
        v-if="bundleMode.isBundle.value"
        class="app-header__badge app-header__badge--bundle"
      >Bundle Mode</span>
      <span
        v-else-if="bundleMode.isNoBundle.value"
        class="app-header__badge app-header__badge--no-bundle"
      >No Bundle</span>
      <span
        v-if="sessionLabel"
        class="app-header__session"
      >{{ sessionLabel }}</span>
    </div>
    <PersonaSwitcher class="app-header__persona" />
  </header>
</template>

<script setup lang="ts">
import { computed } from 'vue';
import PersonaSwitcher from '@/components/PersonaSwitcher.vue';
import { useSessionStore } from '@/stores/sessionStore';
import { useBundleMode } from '@/composables/useBundleMode';

const sessionStore = useSessionStore();
const bundleMode = useBundleMode();

const sessionLabel = computed(() =>
  sessionStore.current?.sessionId ?? null
);
</script>
```

Add style entries to the existing `<style>` block:
```css
.app-header__badge {
  font-size: 0.7rem;
  font-weight: 600;
  padding: 0.15rem 0.5rem;
  border-radius: 99px;
  letter-spacing: 0.04em;
  text-transform: uppercase;
}

.app-header__badge--bundle {
  background: var(--c-accent, #4a9eff);
  color: white;
}

.app-header__badge--no-bundle {
  background: var(--c-bg-subtle, #eee);
  color: var(--c-text-muted, #666);
}

.app-header__session {
  font-size: 0.8rem;
  color: var(--c-text-muted, #888);
  font-family: monospace;
}
```

### Tests

Extend `tests/unit/AppHeader.spec.ts`. Add the following tests (the existing `AppHeader_ContainsPersonaSwitcher` must still pass):

1. **`AppHeader_ShowsBundleBadge_WhenInBundleMode`** — mock `useBundleMode` to return `{ isBundle: computed(() => true), isNoBundle: computed(() => false), isLive: computed(() => false), mode: computed(() => ({...})), refresh: vi.fn() }`; mount; assert `.app-header__badge--bundle` is visible with text "Bundle Mode".

2. **`AppHeader_ShowsSessionId_WhenSessionLoaded`** — mock `useSessionStore` with `current: { sessionId: 'sess-abc' }`; mount; assert `.app-header__session` contains "sess-abc".

3. **`AppHeader_HidesSessionId_WhenNoSession`** — `sessionStore.current = null`; assert `.app-header__session` does not exist.

Mock patterns: use `vi.mock('@/composables/useBundleMode', () => ({ useBundleMode: () => ({...}) }))`.

---

## Task D3 — BookmarkBar Component + App Shell Integration

### Goal
Create `BookmarkBar.vue` that shows persona-filtered bookmark chips for the current session/view. Integrate it in `App.vue` between `AppHeader` and `<main>`.

### Part A: Create `tracer-viewer/src/components/BookmarkBar.vue`

The `BookmarkBar.spec.ts` test file already exists and defines the expected contract:
- Props: `sessionId: string`, `viewType: string`
- On mount: call `api.listSavedViews({ sessionId, kind: 'Bookmark', viewType, persona: personaStore.current })`
- When result is empty: do NOT render `.bookmark-bar` element (return `null`/`v-if` on root)
- When result has items: render `.bookmark-bar` with `.bookmark-bar__chip` for each bookmark
- On chip click: `await api.recordSavedViewOpened(bookmark.savedViewId)` then `router.push(bookmark.url)`
- Watch `personaStore.current` → reload bookmarks when persona changes

```html
<template>
  <nav v-if="bookmarks.length > 0" class="bookmark-bar">
    <button
      v-for="bm in bookmarks"
      :key="bm.savedViewId"
      class="bookmark-bar__chip"
      @click="navigate(bm)"
    >
      {{ bm.label }}
    </button>
  </nav>
</template>

<script setup lang="ts">
import { ref, watch, onMounted } from 'vue';
import { useRouter } from 'vue-router';
import { api } from '@/api/tracerApiClient';
import { usePersonaStore } from '@/stores/personaStore';
import type { SavedViewDto } from '@/api/tracerApiClient';

const props = defineProps<{ sessionId: string; viewType: string }>();
const router = useRouter();
const personaStore = usePersonaStore();
const bookmarks = ref<SavedViewDto[]>([]);

async function load() {
  try {
    bookmarks.value = await api.listSavedViews({
      sessionId: props.sessionId,
      kind: 'Bookmark',
      viewType: props.viewType,
      persona: personaStore.current,
    });
  } catch {
    bookmarks.value = [];
  }
}

onMounted(load);
watch(() => personaStore.current, load);

async function navigate(bm: SavedViewDto) {
  await api.recordSavedViewOpened(bm.savedViewId);
  void router.push(bm.url);
}
</script>

<style>
.bookmark-bar {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.375rem 1.5rem;
  background: var(--c-bg-subtle, #f5f5f5);
  border-bottom: 1px solid var(--c-bg-subtle, #e8e8e8);
  overflow-x: auto;
}

.bookmark-bar__chip {
  background: none;
  border: 1px solid var(--c-accent, #4a9eff);
  border-radius: 99px;
  color: var(--c-accent, #4a9eff);
  cursor: pointer;
  font-size: 0.8rem;
  padding: 0.2rem 0.75rem;
  white-space: nowrap;
}

.bookmark-bar__chip:hover {
  background: var(--c-accent, #4a9eff);
  color: white;
}
</style>
```

### Part B: Update `tracer-viewer/src/App.vue`

Add `BookmarkBar` below `AppHeader`, using the current route's `sessionId` param:

```html
<script setup lang="ts">
import './styles/base.scss';
import { computed } from 'vue';
import { RouterView, useRoute } from 'vue-router';
import AppHeader from './components/AppHeader.vue';
import BookmarkBar from './components/BookmarkBar.vue';

const route = useRoute();
const sessionId = computed(() => route.params.sessionId as string | undefined);
const viewType = computed(() => (route.name as string | undefined) ?? '');
</script>

<template>
  <div class="app">
    <AppHeader />
    <BookmarkBar
      v-if="sessionId"
      :session-id="sessionId"
      :view-type="viewType"
    />
    <main class="app__main">
      <RouterView v-slot="{ Component }">
        <Transition
          mode="out-in"
          name="fade"
        >
          <component :is="Component" />
        </Transition>
      </RouterView>
    </main>
  </div>
</template>
```

Keep the existing `<style>` block unchanged.

### Tests for D3

The `tests/unit/BookmarkBar.spec.ts` already exists with the full test suite:
- `BookmarkBar_Hidden_WhenNoBookmarks`
- `BookmarkBar_RendersChips`
- `BookmarkBar_ChipClick_NavigatesAndRecords`
- `BookmarkBar_ReloadsOnPersonaChange`

These tests must ALL PASS after your implementation. Do not modify the spec file unless a test has a genuine bug (and document any deviation).

---

## Task F4 — Numeric Validation in Saved Query Parameter Dialog

### Goal
When a saved query has `parameters: SavedQueryParameterDto[]`, the Run button should expand an inline parameter form. Numeric-typed parameters (INT, BIGINT, DOUBLE, FLOAT, NUMERIC) must be validated before enabling the Execute button.

### File: `tracer-viewer/src/views/SavedQueriesView.vue`

**Current Run flow** (no parameters):
```js
async function runQuery(q: SavedQueryDto) {
  await api.recordSavedQueryRun(q.savedQueryId);
  void router.push({ name: 'sql-console', params: { sessionId: 'default' }, query: { sql: q.sql } });
}
```

**New Run flow** (with parameters):

1. Add refs:
```ts
const paramQueryId = ref<string | null>(null);
const paramValues = ref<Record<string, string>>({});
```

2. Update `runQuery` to check for parameters first:
```ts
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
```

3. Add a computed for validation:
```ts
const NUMERIC_TYPES = /^(int|bigint|double|float|numeric)/i;

const paramRunDisabled = computed(() => {
  const q = filtered.value.find(q => q.savedQueryId === paramQueryId.value);
  if (!q) return true;
  return q.parameters.some(p => {
    const val = paramValues.value[p.name] ?? '';
    return NUMERIC_TYPES.test(p.duckType) && isNaN(Number(val));
  });
});
```

4. Add a handler for submitting the parameter form:
```ts
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
```

5. In the template, add a parameter form section inside the query `<li>` in view mode, AFTER the `.saved-queries-view__item-actions` div. Display it only when `paramQueryId === q.savedQueryId`:

```html
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
```

6. Add CSS for the new elements (append to the existing `<style>` block's `.saved-queries-view` nested rules):
```scss
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
```

### Tests for F4

Create `tests/unit/SavedQueriesParamValidation.spec.ts` (or extend an existing spec if the pattern fits):

1. **`paramRunDisabled_True_WhenNumericParamIsNaN`** — mount `SavedQueriesView` with a mocked `useSavedQueries` returning a query that has `parameters: [{ name: 'limit', duckType: 'BIGINT', defaultValueText: '10' }]`. Click Run on that query. Set the input to `"abc"`. Assert the Execute button has `disabled` attribute.

2. **`paramRunDisabled_False_WhenNumericParamIsValid`** — same setup; set input to `"42"`. Assert Execute button is NOT disabled.

3. **`paramRunDisabled_False_ForTextParam`** — query has `parameters: [{ name: 'topic', duckType: 'VARCHAR', defaultValueText: '' }]`; set input to `"not-a-number"`. Assert Execute button is NOT disabled (VARCHAR is not numeric).

4. **`runQuery_WithParams_ShowsParamForm`** — click Run on a query with parameters; assert the `.saved-queries-view__param-form` is visible.

5. **`runQuery_WithoutParams_NavigatesDirectly`** — click Run on a query with NO parameters; assert `router.push` was called immediately (no param form shown).

Use `vi.mock` for `api`, `useSavedQueries`, and `vue-router` following the existing patterns in `tests/unit/`.

---

## Build and Test Verification

After all changes:

```bash
cd tracer-viewer
pnpm test --run
```

Expected: all tests passing (no failures in existing or new tests).

---

## Report Format

Return a structured report:
1. Files created/modified with brief description
2. Number of new tests added (and test file names)
3. Any deviations from these instructions (with justification)
4. Test output: pass/fail count
