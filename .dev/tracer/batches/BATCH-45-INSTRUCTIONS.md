# BATCH-45 Instructions

**Batch:** BATCH-45  
**Tasks:** TRC-P8-013 (useAnnotations.ts + annotationStore.ts), TRC-P8-017 (PersonaSwitcher.vue + usePersona.ts + personaStore.ts)  
**Phase:** 8 — Annotations, Saved Views, Trigger Evaluation Log, Multi-Persona Polish  
**Estimated Effort:** 8–10 hours  
**Dependencies:** BATCH-43 complete (annotation REST API exists), BATCH-44 complete (lifecycle config)  
**Report path:** `d:\WORK\Tracer\.dev\tracer\reports\BATCH-45-REPORT.md`  
**Working directory:** `d:\WORK\Tracer\tracer-viewer`

---

## 📋 Onboarding

### Required Reading (IN ORDER)

1. **Design:** `docs/tracer_phase8_design.md` §5.1 (useAnnotations composable + store), §7 (PersonaSwitcher)
2. **Task definitions:** `docs/TASK-DETAIL.md` — §TRC-P8-013 (8 success conditions), §TRC-P8-017 (12 success conditions)
3. **Previous review:** `.dev/tracer/reviews/BATCH-44-REVIEW.md`
4. **Existing API client:** `tracer-viewer/src/api/tracerApiClient.ts` — understand how to add new DTOs/methods
5. **Existing composable pattern:** `tracer-viewer/src/composables/useEntityHistoryQuery.ts`
6. **Existing store pattern:** `tracer-viewer/src/stores/entityHistoryStore.ts`
7. **Existing component pattern:** `tracer-viewer/src/components/AppHeader.vue` + `SessionCard.vue` (for integration)
8. **Router:** `tracer-viewer/src/router/index.ts` (for route context)
9. **Test pattern:** `tracer-viewer/tests/unit/useEntityHistoryQuery.spec.ts` and `causalTreeStore.spec.ts`
10. **Vitest config:** `tracer-viewer/vite.config.ts` (understand test setup)
11. **Test setup:** `tracer-viewer/tests/setup.ts`

### Frontend test commands

```powershell
# Unit tests (from tracer-viewer directory)
cd d:\Work\Tracer\tracer-viewer
pnpm test -- --run --reporter=verbose 2>&1 | Select-Object -Last 30

# Filter to specific tests
pnpm test -- --run --reporter=verbose annotationStore 2>&1 | Select-Object -Last 20
pnpm test -- --run --reporter=verbose useAnnotations 2>&1 | Select-Object -Last 20
pnpm test -- --run --reporter=verbose personaStore 2>&1 | Select-Object -Last 20
pnpm test -- --run --reporter=verbose usePersona 2>&1 | Select-Object -Last 20
pnpm test -- --run --reporter=verbose PersonaSwitcher 2>&1 | Select-Object -Last 20
```

### DO NOT STOP — implement everything and write the report when all tests pass.

---

## 🔄 MANDATORY WORKFLOW

1. Add annotation DTOs + API client methods
2. TRC-P8-013: Create `annotationStore.ts` → write 4 store tests → pass ✅
3. TRC-P8-013: Create `useAnnotations.ts` → write 4 composable tests → pass ✅
4. TRC-P8-017: Create `personaStore.ts` → write 4 store tests → pass ✅
5. TRC-P8-017: Create `usePersona.ts` + `PersonaSwitcher.vue` → write 4 component tests → pass ✅
6. TRC-P8-017: Integrate `PersonaSwitcher` into `AppHeader.vue` + modify `SessionCard.vue` → write 4 integration tests → pass ✅
7. Run full frontend suite: no regressions ✅

---

## ✅ Task 1 — API Client Extensions

### 1.1 Add annotation DTOs to `src/api/tracerApiClient.ts`

Add the following interfaces (alongside the existing interfaces):

```typescript
export interface AnnotationDto {
  annotationId: string;
  sessionId: string;
  kind: string;          // "Event" | "Entity" | "Trace" | "TimePoint"
  eventId?: string;
  entityId?: string;
  traceId?: string;
  wallclockTimestamp?: string;  // ISO-8601 for TimePoint kind
  body: string;
  title?: string;
  tags: string[];
  author?: string;
  createdAtUtc: string;
  modifiedAtUtc?: string;
}

export interface CreateAnnotationDto {
  sessionId: string;
  kind: string;
  eventId?: string;
  entityId?: string;
  traceId?: string;
  wallclockTimestamp?: string;
  body: string;
  title?: string;
  tags?: string[];
  author?: string;
}

export interface UpdateAnnotationDto {
  body?: string;
  title?: string;
  tags?: string[];
}

export interface AnnotationListDto {
  annotations: AnnotationDto[];
}
```

### 1.2 Add annotation API methods to `TracerApiClient` class

```typescript
async listAnnotations(sessionId: string, opts?: { signal?: AbortSignal }): Promise<AnnotationDto[]> {
  const params = new URLSearchParams({ sessionId });
  const res = await fetch(`/api/annotations?${params}`, { signal: opts?.signal });
  if (!res.ok) throw new Error(`listAnnotations: ${res.status}`);
  const data = await res.json() as AnnotationListDto;
  return data.annotations;
}

async getAnnotation(annotationId: string): Promise<AnnotationDto | null> {
  const res = await fetch(`/api/annotations/${encodeURIComponent(annotationId)}`);
  if (res.status === 404) return null;
  if (!res.ok) throw new Error(`getAnnotation: ${res.status}`);
  return res.json() as Promise<AnnotationDto>;
}

async createAnnotation(dto: CreateAnnotationDto): Promise<AnnotationDto> {
  const res = await fetch('/api/annotations', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(dto),
  });
  if (!res.ok) throw new Error(`createAnnotation: ${res.status}`);
  return res.json() as Promise<AnnotationDto>;
}

async updateAnnotation(annotationId: string, dto: UpdateAnnotationDto): Promise<AnnotationDto | null> {
  const res = await fetch(`/api/annotations/${encodeURIComponent(annotationId)}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(dto),
  });
  if (res.status === 404) return null;
  if (!res.ok) throw new Error(`updateAnnotation: ${res.status}`);
  return res.json() as Promise<AnnotationDto>;
}

async deleteAnnotation(annotationId: string): Promise<void> {
  const res = await fetch(`/api/annotations/${encodeURIComponent(annotationId)}`, {
    method: 'DELETE',
  });
  if (!res.ok && res.status !== 404) throw new Error(`deleteAnnotation: ${res.status}`);
}
```

---

## ✅ Task 2 — TRC-P8-013: annotationStore.ts + useAnnotations.ts

**Design reference:** `docs/tracer_phase8_design.md` §5.1  
**Task definition:** `docs/TASK-DETAIL.md` §TRC-P8-013

### 2.1 Create `src/stores/annotationStore.ts`

```typescript
import { defineStore } from 'pinia';
import type { AnnotationDto } from '@/api/tracerApiClient';

export const useAnnotationStore = defineStore('annotations', {
  state: () => ({
    // keyed by annotationId
    _map: {} as Record<string, AnnotationDto>,
  }),
  getters: {
    all: (state): AnnotationDto[] => Object.values(state._map),
    byEventId: (state) => (eventId: string): AnnotationDto[] =>
      Object.values(state._map).filter(a => a.eventId === eventId),
    byEntityId: (state) => (entityId: string): AnnotationDto[] =>
      Object.values(state._map).filter(a => a.entityId === entityId),
    byTraceId: (state) => (traceId: string): AnnotationDto[] =>
      Object.values(state._map).filter(a => a.traceId === traceId),
  },
  actions: {
    load(annotations: AnnotationDto[]) {
      // Replace map entirely — prevents duplication on double-load
      const next: Record<string, AnnotationDto> = {};
      for (const a of annotations) next[a.annotationId] = a;
      this._map = next;
    },
    upsert(annotation: AnnotationDto) {
      this._map = { ...this._map, [annotation.annotationId]: annotation };
    },
    remove(annotationId: string) {
      const { [annotationId]: _removed, ...rest } = this._map;
      this._map = rest;
    },
    clear() {
      this._map = {};
    },
  },
});
```

### 2.2 Create `src/composables/useAnnotations.ts`

```typescript
import { ref, watch, type Ref } from 'vue';
import { api } from '@/api/tracerApiClient';
import { useAnnotationStore } from '@/stores/annotationStore';
import type { AnnotationDto, CreateAnnotationDto, UpdateAnnotationDto } from '@/api/tracerApiClient';

export interface AnnotationTarget {
  eventId?: string;
  entityId?: string;
  traceId?: string;
}

/**
 * Composable for annotation CRUD. Loads annotations for a given sessionId
 * and optional target filter. Syncs results into annotationStore.
 */
export function useAnnotations(sessionId: Ref<string | null>) {
  const store = useAnnotationStore();
  const loading = ref(false);
  const error = ref<string | null>(null);

  async function load() {
    const sid = sessionId.value;
    if (!sid) {
      store.clear();
      return;
    }
    loading.value = true;
    error.value = null;
    try {
      const items = await api.listAnnotations(sid);
      store.load(items);
    } catch (err: unknown) {
      error.value = err instanceof Error ? err.message : String(err);
    } finally {
      loading.value = false;
    }
  }

  async function create(
    body: string,
    kind: string,
    target: AnnotationTarget,
    title?: string,
    tags?: string[],
  ): Promise<AnnotationDto> {
    const sid = sessionId.value;
    if (!sid) throw new Error('No active session');
    const author = localStorage.getItem('tracer:authorName') ?? 'anonymous';
    const dto: CreateAnnotationDto = {
      sessionId: sid,
      kind,
      body,
      title,
      tags: tags ?? [],
      author,
      ...target,
    };
    const created = await api.createAnnotation(dto);
    store.upsert(created);
    return created;
  }

  async function update(
    annotationId: string,
    body: string,
    title?: string,
    tags?: string[],
  ): Promise<void> {
    const dto: UpdateAnnotationDto = { body, title, tags };
    const updated = await api.updateAnnotation(annotationId, dto);
    if (updated) store.upsert(updated);
  }

  async function remove(annotationId: string): Promise<void> {
    await api.deleteAnnotation(annotationId);
    store.remove(annotationId);
  }

  const stopWatch = watch(sessionId, () => { void load(); }, { immediate: true });

  return { loading, error, annotations: store.all, create, update, remove, stopWatch };
}
```

### 2.3 Tests — `tests/unit/annotationStore.spec.ts` (4 tests)

```typescript
import { describe, it, expect, beforeEach } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';
import { useAnnotationStore } from '../../src/stores/annotationStore';
import type { AnnotationDto } from '../../src/api/tracerApiClient';

function makeAnnotation(override?: Partial<AnnotationDto>): AnnotationDto {
  return {
    annotationId: 'ann-1',
    sessionId: 'sess-1',
    kind: 'Event',
    eventId: 'evt-1',
    body: 'Test body',
    tags: [],
    createdAtUtc: '2026-01-01T00:00:00Z',
    ...override,
  };
}

describe('annotationStore', () => {
  beforeEach(() => setActivePinia(createPinia()));

  it('annotationStore_ByEventId_ReturnsCorrectAnnotations', () => {
    const store = useAnnotationStore();
    store.load([
      makeAnnotation({ annotationId: 'a1', eventId: 'A' }),
      makeAnnotation({ annotationId: 'a2', eventId: 'A' }),
      makeAnnotation({ annotationId: 'a3', eventId: 'B' }),
    ]);
    expect(store.byEventId('A')).toHaveLength(2);
    expect(store.byEventId('B')).toHaveLength(1);
    expect(store.byEventId('C')).toHaveLength(0);
  });

  it('annotationStore_NoDuplicatesOnDoubleLoad', () => {
    const store = useAnnotationStore();
    const data = [
      makeAnnotation({ annotationId: 'a1' }),
      makeAnnotation({ annotationId: 'a2' }),
    ];
    store.load(data);
    store.load(data);
    expect(store.all).toHaveLength(2);
  });

  it('annotationStore_IsEmpty_WhenNoSessionLoaded', () => {
    const store = useAnnotationStore();
    expect(store.byEventId('any-id')).toHaveLength(0);
    expect(store.all).toHaveLength(0);
  });

  it('annotationStore_Remove_DeletesEntry', () => {
    const store = useAnnotationStore();
    store.load([makeAnnotation({ annotationId: 'a1' })]);
    store.remove('a1');
    expect(store.all).toHaveLength(0);
  });
});
```

### 2.4 Tests — `tests/unit/useAnnotations.spec.ts` (4 tests)

```typescript
import { describe, it, expect, beforeEach, vi } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';
import { defineComponent, ref, nextTick } from 'vue';
import { mount, flushPromises } from '@vue/test-utils';
import type { AnnotationDto } from '../../src/api/tracerApiClient';

// Mock the API module — MUST be before importing the composable
vi.mock('@/api/tracerApiClient', () => ({
  api: {
    listAnnotations: vi.fn(),
    createAnnotation: vi.fn(),
    updateAnnotation: vi.fn(),
    deleteAnnotation: vi.fn(),
  },
}));

import { useAnnotations } from '../../src/composables/useAnnotations';

function makeAnnotation(override?: Partial<AnnotationDto>): AnnotationDto {
  return {
    annotationId: 'ann-1',
    sessionId: 'sess-1',
    kind: 'Event',
    eventId: 'evt-1',
    body: 'Test body',
    tags: [],
    createdAtUtc: '2026-01-01T00:00:00Z',
    ...override,
  };
}

describe('useAnnotations', () => {
  let pinia: ReturnType<typeof createPinia>;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
    vi.clearAllMocks();
  });

  it('useAnnotations_LoadsOnMount', async () => {
    const { api } = await import('@/api/tracerApiClient');
    (api.listAnnotations as ReturnType<typeof vi.fn>).mockResolvedValue([
      makeAnnotation({ annotationId: 'a1' }),
      makeAnnotation({ annotationId: 'a2' }),
    ]);

    const sessionId = ref<string | null>('sess-1');
    let annotations: AnnotationDto[] = [];

    mount(defineComponent({
      setup() {
        const result = useAnnotations(sessionId);
        annotations = result.annotations as AnnotationDto[];
        return {};
      },
      template: '<div/>',
    }), { global: { plugins: [pinia] } });

    await flushPromises();
    // Access from store directly since `annotations` is a getter
    const { useAnnotationStore } = await import('../../src/stores/annotationStore');
    const store = useAnnotationStore();
    expect(store.all).toHaveLength(2);
  });

  it('useAnnotations_ReloadsOnSessionIdChange', async () => {
    const { api } = await import('@/api/tracerApiClient');
    (api.listAnnotations as ReturnType<typeof vi.fn>).mockResolvedValue([]);

    const sessionId = ref<string | null>('sess-1');

    mount(defineComponent({
      setup() {
        useAnnotations(sessionId);
        return {};
      },
      template: '<div/>',
    }), { global: { plugins: [pinia] } });

    await flushPromises();
    expect(api.listAnnotations).toHaveBeenCalledWith('sess-1');

    sessionId.value = 'sess-2';
    await nextTick();
    await flushPromises();
    expect(api.listAnnotations).toHaveBeenCalledWith('sess-2');
  });

  it('useAnnotations_Create_AddsToLocalList', async () => {
    const { api } = await import('@/api/tracerApiClient');
    (api.listAnnotations as ReturnType<typeof vi.fn>).mockResolvedValue([]);
    const created = makeAnnotation({ annotationId: 'new-1', body: 'new body' });
    (api.createAnnotation as ReturnType<typeof vi.fn>).mockResolvedValue(created);

    const sessionId = ref<string | null>('sess-1');
    let composable: ReturnType<typeof useAnnotations>;

    mount(defineComponent({
      setup() {
        composable = useAnnotations(sessionId);
        return {};
      },
      template: '<div/>',
    }), { global: { plugins: [pinia] } });

    await flushPromises();

    const { useAnnotationStore } = await import('../../src/stores/annotationStore');
    const store = useAnnotationStore();
    const beforeCount = store.all.length;

    await composable!.create('new body', 'Event', { eventId: 'X' });

    expect(store.all.length).toBe(beforeCount + 1);
    expect(store.all.some(a => a.annotationId === 'new-1')).toBe(true);
  });

  it('useAnnotations_Remove_DeletesLocalEntry', async () => {
    const { api } = await import('@/api/tracerApiClient');
    const ann = makeAnnotation({ annotationId: 'del-1' });
    (api.listAnnotations as ReturnType<typeof vi.fn>).mockResolvedValue([ann]);
    (api.deleteAnnotation as ReturnType<typeof vi.fn>).mockResolvedValue(undefined);

    const sessionId = ref<string | null>('sess-1');
    let composable: ReturnType<typeof useAnnotations>;

    mount(defineComponent({
      setup() {
        composable = useAnnotations(sessionId);
        return {};
      },
      template: '<div/>',
    }), { global: { plugins: [pinia] } });

    await flushPromises();

    const { useAnnotationStore } = await import('../../src/stores/annotationStore');
    const store = useAnnotationStore();
    expect(store.all).toHaveLength(1);

    await composable!.remove('del-1');
    expect(store.all).toHaveLength(0);
  });
});
```

---

## ✅ Task 3 — TRC-P8-017: personaStore.ts + usePersona.ts + PersonaSwitcher.vue

**Design reference:** `docs/tracer_phase8_design.md` §7  
**Task definition:** `docs/TASK-DETAIL.md` §TRC-P8-017

### 3.1 Create `src/stores/personaStore.ts`

```typescript
import { defineStore } from 'pinia';

export type Persona = 'engineer' | 'scenario-author' | 'operator';
const STORAGE_KEY = 'tracer:persona';
const ALL_PERSONAS: Persona[] = ['engineer', 'scenario-author', 'operator'];

export const usePersonaStore = defineStore('persona', {
  state: (): { current: Persona } => ({
    current: (localStorage.getItem(STORAGE_KEY) as Persona | null) ?? 'engineer',
  }),
  actions: {
    set(persona: Persona) {
      this.current = persona;
      localStorage.setItem(STORAGE_KEY, persona);
    },
  },
});

export { ALL_PERSONAS };
```

### 3.2 Create `src/composables/usePersona.ts`

```typescript
import { computed } from 'vue';
import { usePersonaStore, type Persona, ALL_PERSONAS } from '@/stores/personaStore';

export function usePersona() {
  const store = usePersonaStore();
  const persona = computed(() => store.current);

  function setPersona(p: Persona) {
    store.set(p);
  }

  return { persona, setPersona, allPersonas: ALL_PERSONAS };
}
```

### 3.3 Create `src/components/PersonaSwitcher.vue`

```vue
<template>
  <div class="persona-switcher">
    <button
      v-for="p in allPersonas"
      :key="p"
      class="persona-switcher__btn"
      :class="{ 'persona-switcher__btn--active': persona === p }"
      @click="setPersona(p)"
    >
      {{ label(p) }}
    </button>
  </div>
</template>

<script setup lang="ts">
import { usePersona } from '@/composables/usePersona';
import type { Persona } from '@/stores/personaStore';

const { persona, setPersona, allPersonas } = usePersona();

const LABELS: Record<Persona, string> = {
  'engineer': 'Engineer',
  'scenario-author': 'Scenario Author',
  'operator': 'Operator',
};

function label(p: Persona) {
  return LABELS[p];
}
</script>

<style lang="scss">
.persona-switcher {
  display: flex;
  gap: 0.25rem;

  &__btn {
    padding: 0.25rem 0.75rem;
    border-radius: 4px;
    border: 1px solid var(--c-bg-subtle);
    background: transparent;
    color: var(--c-text-muted);
    cursor: pointer;
    font-size: 0.875rem;

    &:hover {
      background: var(--c-bg-subtle);
    }

    &--active {
      background: var(--c-accent);
      border-color: var(--c-accent);
      color: #fff;
    }
  }
}
</style>
```

### 3.4 Integrate `PersonaSwitcher` into `AppHeader.vue`

Modify `src/components/AppHeader.vue` to add `<PersonaSwitcher />`:

```vue
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

<style>
.app-header {
  display: flex;
  align-items: center;
  padding: 0 1.5rem;
  height: 3.5rem;
  background: var(--c-bg-surface);
  border-bottom: 1px solid var(--c-bg-subtle);
}

.app-header__brand {
  display: flex;
  align-items: center;
  gap: 0.75rem;
}

.app-header__title {
  font-size: 1.125rem;
  font-weight: 600;
  color: var(--c-text);
  letter-spacing: 0.02em;
}

.app-header__persona {
  margin-left: auto;
}
</style>
```

### 3.5 Modify `SessionCard.vue` to route based on persona

Add persona-aware routing to session card clicks. When the card is clicked, route to `timeline` for `'engineer'`, `scenario` for `'scenario-author'` and `'operator'`.

In the `<script setup>` section, add:

```typescript
import { useRouter } from 'vue-router';
import { usePersonaStore } from '@/stores/personaStore';

const router = useRouter();
const personaStore = usePersonaStore();

function onCardClick() {
  const sessionId = effectiveSessionId.value;
  if (!sessionId) return;
  const persona = personaStore.current;
  if (persona === 'engineer') {
    void router.push({ name: 'timeline', params: { sessionId } });
  } else {
    void router.push({ name: 'scenario', params: { sessionId } });
  }
}
```

Add `@click="onCardClick"` to the `<article class="session-card">` element.

---

## 🧪 Tests

### 3.6 Tests — `tests/unit/personaStore.spec.ts` (4 tests)

```typescript
import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';

describe('personaStore', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    localStorage.clear();
  });

  afterEach(() => {
    localStorage.clear();
  });

  it('personaStore_DefaultIsEngineer_WhenLocalStorageEmpty', async () => {
    const { usePersonaStore } = await import('../../src/stores/personaStore');
    const store = usePersonaStore();
    expect(store.current).toBe('engineer');
  });

  it('personaStore_RestoresFromLocalStorage', async () => {
    localStorage.setItem('tracer:persona', 'operator');
    const { usePersonaStore } = await import('../../src/stores/personaStore');
    const store = usePersonaStore();
    expect(store.current).toBe('operator');
  });

  it('personaStore_Set_PersistsToLocalStorage', async () => {
    const { usePersonaStore } = await import('../../src/stores/personaStore');
    const store = usePersonaStore();
    store.set('scenario-author');
    expect(localStorage.getItem('tracer:persona')).toBe('scenario-author');
  });

  it('personaStore_Set_UpdatesCurrentReactively', async () => {
    const { usePersonaStore } = await import('../../src/stores/personaStore');
    const store = usePersonaStore();
    const initial = store.current;
    store.set('operator');
    expect(store.current).toBe('operator');
    expect(store.current).not.toBe(initial);
  });
});
```

**Important for localStorage tests:** Pinia caches store instances between tests even with fresh `createPinia()` because the `state()` factory runs only once per store definition. To make the localStorage initialization test work, you must either:
- Import the store dynamically inside each test case (`await import('...')`) after setting localStorage
- Or reset the store's state manually between tests

Use dynamic imports inside each test to guarantee fresh state factory execution.

### 3.7 Tests — `tests/unit/usePersona.spec.ts` (2 tests)

```typescript
import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';

describe('usePersona', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    localStorage.clear();
  });

  afterEach(() => localStorage.clear());

  it('usePersona_Persona_MatchesStore', async () => {
    const { usePersonaStore } = await import('../../src/stores/personaStore');
    const { usePersona } = await import('../../src/composables/usePersona');
    const store = usePersonaStore();
    store.set('operator');
    const { persona } = usePersona();
    expect(persona.value).toBe('operator');
  });

  it('usePersona_AllPersonas_HasThreeItems', async () => {
    const { usePersona } = await import('../../src/composables/usePersona');
    const { allPersonas } = usePersona();
    expect(allPersonas).toHaveLength(3);
    expect(allPersonas).toContain('engineer');
    expect(allPersonas).toContain('scenario-author');
    expect(allPersonas).toContain('operator');
  });
});
```

### 3.8 Tests — `tests/unit/PersonaSwitcher.spec.ts` (4 tests)

```typescript
import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';
import { mount } from '@vue/test-utils';
import PersonaSwitcher from '../../src/components/PersonaSwitcher.vue';

describe('PersonaSwitcher', () => {
  let pinia: ReturnType<typeof createPinia>;

  beforeEach(() => {
    localStorage.clear();
    pinia = createPinia();
    setActivePinia(pinia);
  });

  afterEach(() => localStorage.clear());

  it('PersonaSwitcher_AllThreeButtons_Render', () => {
    const wrapper = mount(PersonaSwitcher, { global: { plugins: [pinia] } });
    const buttons = wrapper.findAll('.persona-switcher__btn');
    expect(buttons).toHaveLength(3);
    const texts = buttons.map(b => b.text());
    expect(texts).toContain('Engineer');
    expect(texts).toContain('Scenario Author');
    expect(texts).toContain('Operator');
  });

  it('PersonaSwitcher_ActiveButton_MatchesCurrent', async () => {
    const { usePersonaStore } = await import('../../src/stores/personaStore');
    const store = usePersonaStore();
    store.set('engineer');
    const wrapper = mount(PersonaSwitcher, { global: { plugins: [pinia] } });
    const active = wrapper.find('.persona-switcher__btn--active');
    expect(active.exists()).toBe(true);
    expect(active.text()).toBe('Engineer');
  });

  it('PersonaSwitcher_Click_SetsPersona', async () => {
    const { usePersonaStore } = await import('../../src/stores/personaStore');
    const store = usePersonaStore();
    const wrapper = mount(PersonaSwitcher, { global: { plugins: [pinia] } });
    const scenarioBtn = wrapper.findAll('.persona-switcher__btn').find(b => b.text() === 'Scenario Author');
    await scenarioBtn!.trigger('click');
    expect(store.current).toBe('scenario-author');
  });

  it('PersonaSwitcher_ActiveButtonChanges_OnStoreUpdate', async () => {
    const { usePersonaStore } = await import('../../src/stores/personaStore');
    const store = usePersonaStore();
    store.set('engineer');
    const wrapper = mount(PersonaSwitcher, { global: { plugins: [pinia] } });
    store.set('operator');
    await wrapper.vm.$nextTick();
    const active = wrapper.find('.persona-switcher__btn--active');
    expect(active.text()).toBe('Operator');
  });
});
```

### 3.9 Tests — `tests/unit/SessionCard.spec.ts` additions (3 new tests)

Add to the **existing** `SessionCard.spec.ts`. Study the existing test file first to understand the setup pattern, then add:

```typescript
// Add to existing describe block (or create a nested describe 'persona-aware routing')

it('SessionCard_Engineer_RoutesToTimeline', async () => {
  const { usePersonaStore } = await import('../../src/stores/personaStore');
  const store = usePersonaStore();
  store.set('engineer');

  const wrapper = mount(SessionCard, {
    props: { session: makeSession({ sessionId: 's1' }) },
    global: {
      plugins: [pinia, router],
      stubs: { RouterLink: RouterLinkStub },
    },
  });

  await wrapper.trigger('click');
  expect(pushSpy).toHaveBeenCalledWith({ name: 'timeline', params: { sessionId: 's1' } });
});

it('SessionCard_ScenarioAuthor_RoutesToScenario', async () => {
  const { usePersonaStore } = await import('../../src/stores/personaStore');
  const store = usePersonaStore();
  store.set('scenario-author');

  const wrapper = mount(SessionCard, {
    props: { session: makeSession({ sessionId: 's2' }) },
    global: {
      plugins: [pinia, router],
      stubs: { RouterLink: RouterLinkStub },
    },
  });

  await wrapper.trigger('click');
  expect(pushSpy).toHaveBeenCalledWith({ name: 'scenario', params: { sessionId: 's2' } });
});

it('SessionCard_Operator_RoutesToScenario', async () => {
  const { usePersonaStore } = await import('../../src/stores/personaStore');
  const store = usePersonaStore();
  store.set('operator');

  const wrapper = mount(SessionCard, {
    props: { session: makeSession({ sessionId: 's3' }) },
    global: {
      plugins: [pinia, router],
      stubs: { RouterLink: RouterLinkStub },
    },
  });

  await wrapper.trigger('click');
  expect(pushSpy).toHaveBeenCalledWith({ name: 'scenario', params: { sessionId: 's3' } });
});
```

**NOTE:** Study the existing `SessionCard.spec.ts` to understand the current test setup. If `router` and `pushSpy` are not yet set up in the current spec file, you'll need to add them. Use `createRouter` / `createMemoryHistory` and spy on `router.push`. If the existing tests use a different pattern, adapt accordingly — do not break existing tests.

### 3.10 Test — `tests/unit/AppHeader.spec.ts` (1 test)

```typescript
import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';
import { mount } from '@vue/test-utils';
import AppHeader from '../../src/components/AppHeader.vue';
import PersonaSwitcher from '../../src/components/PersonaSwitcher.vue';

describe('AppHeader', () => {
  beforeEach(() => {
    localStorage.clear();
    setActivePinia(createPinia());
  });
  afterEach(() => localStorage.clear());

  it('AppHeader_ContainsPersonaSwitcher', () => {
    const wrapper = mount(AppHeader, {
      global: { plugins: [createPinia()] },
    });
    expect(wrapper.findComponent(PersonaSwitcher).exists()).toBe(true);
  });
});
```

---

## ⚠️ Quality Standards

**Pinia localStorage initialization:** The `state()` factory in Pinia stores reads `localStorage` synchronously at construction time. Standard `createPinia()` caches the store module. To test the `localStorage` restoration behavior, you **must** dynamically import the store module *after* setting `localStorage`, because the module may be cached with the old state. Use `vi.resetModules()` before each test that needs fresh localStorage, or use dynamic `await import(...)` after setting the value.

Example pattern for isolated localStorage tests:
```typescript
it('restores from localStorage', async () => {
  localStorage.setItem('tracer:persona', 'operator');
  vi.resetModules();
  const { usePersonaStore } = await import('../../src/stores/personaStore');
  setActivePinia(createPinia());
  const store = usePersonaStore();
  expect(store.current).toBe('operator');
});
```

**No regressions:** Run the full suite after implementation. Previous tests must all still pass.

**API mock pattern:** Always mock `@/api/tracerApiClient` at the top of the test file BEFORE importing composables that use it, to avoid real fetch calls.

---

## 🏗️ Build Validation

No C# changes in this batch. Verify only the frontend builds:

```powershell
cd d:\Work\Tracer\tracer-viewer
pnpm build 2>&1 | Select-Object -Last 10
```

Expected: 0 TypeScript errors.

---

## 📊 Expected Test Counts

| Suite | New Tests |
|-------|-----------|
| annotationStore.spec.ts | 4 |
| useAnnotations.spec.ts | 4 |
| personaStore.spec.ts | 4 |
| usePersona.spec.ts | 2 |
| PersonaSwitcher.spec.ts | 4 |
| SessionCard.spec.ts (additions) | 3 |
| AppHeader.spec.ts | 1 |
| **Total** | **22** |

---

## 📝 Report Format

Write report to: `d:\WORK\Tracer\.dev\tracer\reports\BATCH-45-REPORT.md`

Include:
- Table of files created/modified
- Test results per spec file (counts + pass/fail)
- Issues encountered and resolutions
- Design decisions made
- Suggested git commit message
