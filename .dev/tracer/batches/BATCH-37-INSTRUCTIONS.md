# BATCH-37 Instructions — Phase 7 Entity History: Data Layer, URL State, View Scaffold

**Target:** Coder Sub-agent  
**Batch:** BATCH-37  
**Design reference:** `docs/tracer_phase7_design.md`, `docs/TASK-DETAIL.md`  
**Report path:** `.dev/tracer/reports/BATCH-37-REPORT.md`

---

## 1. Onboarding

**Read before starting:**
1. `docs/tracer_phase7_design.md` §6 (Frontend View Layout), §6.3–§6.4 (store + composable), §11 (URL state)
2. `.dev/tracer/reviews/BATCH-36-REVIEW.md` — BATCH-36 context; understand what was built
3. `.dev/tracer/reviews/BATCH-35-REVIEW.md` — FastStateFileLocator background (DT-026/DT-027 context)
4. `src/Tracer.WebApi/Queries/FastStateFileLocator.cs` — the backend file locator you must fix
5. `tracer-viewer/src/composables/useTimelineQuery.ts` — reference pattern for composables
6. `tracer-viewer/src/composables/useTimelineUrl.ts` — reference pattern for URL composable
7. `tracer-viewer/src/stores/timelineStore.ts` — reference pattern for Pinia stores
8. `tracer-viewer/src/api/tracerApiClient.ts` — existing API client you will extend

**Key Tech Debt you must address:**
- **DT-027 (P2):** `BundleNaming.SafeFileName` is NOT idempotent. It appends a `_xxxx` 4-char SHA hash suffix. `GetAvailableTopicsForEntity` returns safe-encoded names like `"game.tick_ab12"`. When these are passed to `LocateFiles(topic, entityId)`, the method re-encodes them to `"game.tick_ab12_yyyy"` — a path that does not exist. This is a backend bug. Fix it in this batch.

---

## 2. Tasks

### Task 1 — Fix DT-027: `FastStateFileLocator.LocateFilesBySafeTopicName`

**File:** `src/Tracer.WebApi/Queries/FastStateFileLocator.cs`

Add a new method alongside `LocateFiles`:

```csharp
/// <summary>
/// Like <see cref="LocateFiles"/> but accepts a <paramref name="safeTopic"/> that has
/// already been encoded with <see cref="BundleNaming.SafeFileName"/>. Only the entity ID
/// is encoded. Use this when the topic comes from <see cref="GetAvailableTopicsForEntity"/>
/// (which already returns safe-encoded names).
/// </summary>
public IReadOnlyList<string> LocateFilesBySafeTopicName(string safeTopic, string entityId)
{
    var safeEntity = BundleNaming.SafeFileName(entityId);
    var snapshot = _tracker.CurrentSnapshot();
    var paths = new List<string>();

    foreach (var iv in snapshot.Intervals)
    {
        var candidate = Path.Combine(
            iv.Directory.FastStateDirectory, safeTopic, safeEntity, "samples.parquet");
        if (File.Exists(candidate))
            paths.Add(candidate);
    }

    if (_getBundleWorkingDirectory?.Invoke() is { } bundleDir)
    {
        var bundleCandidate = Path.Combine(
            bundleDir, "fast_state", safeTopic, safeEntity, "samples.parquet");
        if (File.Exists(bundleCandidate))
            paths.Add(bundleCandidate);
    }

    return paths;
}
```

**File:** `src/Tracer.WebApi/Queries/EntityFastStateService.cs`

Update `GetSchemaAsync` and `ReadAsync` to call `LocateFilesBySafeTopicName` instead of `LocateFiles`:

- `GetSchemaAsync(entityId, topic, ct)`: replace `locator.LocateFiles(topic, entityId)` → `locator.LocateFilesBySafeTopicName(topic, entityId)`
- `ReadAsync(entityId, topic, columns, from, to, maxSamples, ct)`: replace `locator.LocateFiles(topic, entityId)` → `locator.LocateFilesBySafeTopicName(topic, entityId)`

**Rationale:** `GetAvailableTopics` returns safe-encoded names from `GetAvailableTopicsForEntity`. The frontend passes these names back to schema/read endpoints. The endpoint must NOT re-encode them.

**Test fix:** `tests/Tracer.Tests.Unit/WebApi/EntityFastStateServiceTests.cs`

The `StubTracker` in the test creates temp directories at `fast_state/{safeTopic}/{safeEntity}/samples.parquet`. The topic used in the test is already a simple name (e.g. `"pos"`). `SafeFileName("pos")` = `"pos_b63d"` (or similar). The directories were created with `SafeFileName(topic)` for the topic path. The service now calls `LocateFilesBySafeTopicName(topic, entityId)` which uses the topic string directly as the directory name.

You need to verify the tests still pass. If the tests create directories like `fast_state/pos_b63d/...` (using `BundleNaming.SafeFileName("pos")`) but call the service with `"pos"`, the service now looks for `fast_state/pos/...` — a path mismatch. Update the tests:

Option A: Create test Parquet files at `fast_state/{rawTopic}/{safeEntity}/samples.parquet` (directory name = raw topic, not safe-encoded) since `LocateFilesBySafeTopicName` uses the topic as-is for the directory.

Option B: Keep the test directory structure but call the service with the safe-encoded topic name (mimicking what the frontend would actually send).

**Use Option A** — create test directories with the raw topic name used as the directory. This tests the correct end-to-end behaviour: frontend receives safe name from `GetAvailableTopics` → passes it to schema/read endpoint → service treats it as already-safe.

But wait — in the test, the `StubTracker`'s `IntervalSetSnapshot` contains intervals whose `Directory.FastStateDirectory` points to a temp folder. Within that, the tests create `fast_state/{topic_dir}/{entity_dir}/samples.parquet`. With `LocateFilesBySafeTopicName`, the topic_dir must equal the topic string as passed to the method. With Option A, set `topicDir = safeTopic` (a pre-encoded name like `"pos_abcd"`) and call the service with `"pos_abcd"`. This matches what the real flow does.

**Simplest approach:** in the test, pick topic strings that don't need encoding (alphanumeric only), so `SafeFileName("pos") = "pos_<hash>"`. The safe-encoded name IS the safe name. Create the directory with `BundleNaming.SafeFileName("pos")` as the directory name, then call the service with `BundleNaming.SafeFileName("pos")` as the topic (mimicking the frontend passing back the safe-encoded name). Update the test topic strings accordingly.

---

### Task 2 — TRC-P7-015: `entityHistoryStore.ts` + `useEntityHistoryQuery.ts`

**Design reference:** `docs/TASK-DETAIL.md` § TRC-P7-015, `docs/tracer_phase7_design.md` §6.3–6.4

#### 2a. Add Entity API types to `tracerApiClient.ts`

Add the following interfaces and methods to `TracerApiClient`:

**Interfaces** (add near the existing DTOs):

```typescript
export interface EntitySummaryDto {
  entityId: string;
  firstSeenUtc: string;
  lastSeenUtc: string;
  eventCount: number;
  samplePlayerId?: string;
  topics: string[];
}

export interface EntityListDto {
  entities: EntitySummaryDto[];
  count: number;
}

export interface EntityEventDto {
  eventId: string;
  traceId: string;
  occurredAtUtc: string;
  topic: string;
  severity?: string;
  notableLabel?: string;
  payloadJson?: string;
  publisherNode: string;
}

export interface EntityEventsDto {
  entityId: string;
  events: EntityEventDto[];
  truncated: boolean;
}

export interface SlowStateSampleDto {
  topic: string;
  occurredAtUtc: string;
  payloadJson: string;
  traceId?: string;
}

export interface EntitySlowStateDto {
  entityId: string;
  byTopic: Record<string, SlowStateSampleDto[]>;
}

export interface FastStateColumnDto {
  name: string;
  isNumeric: boolean;
}

export interface FastStateTopicSchemaDto {
  entityId: string;
  topic: string;
  columns: FastStateColumnDto[];
}

export interface FastStateSampleDto {
  ts: string;
  values: Record<string, number | null>;
}

export interface EntityFastStateDto {
  entityId: string;
  topic: string;
  columns: string[];
  samples: FastStateSampleDto[];
  totalSamples: number;
  downsampled: boolean;
}
```

**Methods** (add to `TracerApiClient` class):

```typescript
async listEntities(
  sessionId: string,
  opts?: { topic?: string; playerId?: string; limit?: number; signal?: AbortSignal }
): Promise<EntityListDto> {
  const params = new URLSearchParams({ sessionId });
  if (opts?.topic)  params.set('topic', opts.topic);
  if (opts?.playerId) params.set('playerId', opts.playerId);
  if (opts?.limit != null) params.set('limit', String(opts.limit));
  const res = await fetch(`/api/entities?${params}`, { signal: opts?.signal });
  if (!res.ok) throw new Error(`listEntities: ${res.status}`);
  return res.json() as Promise<EntityListDto>;
}

async getEntitySummary(
  entityId: string,
  sessionId: string,
  opts?: { signal?: AbortSignal }
): Promise<EntitySummaryDto | null> {
  const params = new URLSearchParams({ sessionId });
  const res = await fetch(`/api/entities/${encodeURIComponent(entityId)}/summary?${params}`, { signal: opts?.signal });
  if (res.status === 404) return null;
  if (!res.ok) throw new Error(`getEntitySummary: ${res.status}`);
  return res.json() as Promise<EntitySummaryDto>;
}

async getEntityEvents(
  entityId: string,
  sessionId: string,
  from: Date,
  to: Date,
  opts?: { limit?: number; signal?: AbortSignal }
): Promise<EntityEventsDto> {
  const params = new URLSearchParams({ sessionId, from: from.toISOString(), to: to.toISOString() });
  if (opts?.limit != null) params.set('limit', String(opts.limit));
  const res = await fetch(`/api/entities/${encodeURIComponent(entityId)}/events?${params}`, { signal: opts?.signal });
  if (!res.ok) throw new Error(`getEntityEvents: ${res.status}`);
  return res.json() as Promise<EntityEventsDto>;
}

async getEntitySlowState(
  entityId: string,
  sessionId: string,
  from: Date,
  to: Date,
  opts?: { topics?: string[]; signal?: AbortSignal }
): Promise<EntitySlowStateDto> {
  const params = new URLSearchParams({ sessionId, from: from.toISOString(), to: to.toISOString() });
  opts?.topics?.forEach(t => params.append('topic', t));
  const res = await fetch(`/api/entities/${encodeURIComponent(entityId)}/slow-state?${params}`, { signal: opts?.signal });
  if (!res.ok) throw new Error(`getEntitySlowState: ${res.status}`);
  return res.json() as Promise<EntitySlowStateDto>;
}

async getEntityFastStateTopics(
  entityId: string,
  sessionId: string,
  opts?: { signal?: AbortSignal }
): Promise<string[]> {
  const params = new URLSearchParams({ sessionId });
  const res = await fetch(`/api/entities/${encodeURIComponent(entityId)}/fast-state/topics?${params}`, { signal: opts?.signal });
  if (!res.ok) throw new Error(`getEntityFastStateTopics: ${res.status}`);
  return res.json() as Promise<string[]>;
}

async getEntityFastStateSchema(
  entityId: string,
  topic: string,
  sessionId: string,
  opts?: { signal?: AbortSignal }
): Promise<FastStateTopicSchemaDto | null> {
  const params = new URLSearchParams({ sessionId });
  const res = await fetch(`/api/entities/${encodeURIComponent(entityId)}/fast-state/${encodeURIComponent(topic)}/schema?${params}`, { signal: opts?.signal });
  if (res.status === 404) return null;
  if (!res.ok) throw new Error(`getEntityFastStateSchema: ${res.status}`);
  return res.json() as Promise<FastStateTopicSchemaDto>;
}

async getEntityFastState(
  entityId: string,
  topic: string,
  sessionId: string,
  from: Date,
  to: Date,
  columns: string[],
  opts?: { maxSamples?: number; signal?: AbortSignal }
): Promise<EntityFastStateDto> {
  const params = new URLSearchParams({ sessionId, from: from.toISOString(), to: to.toISOString() });
  columns.forEach(c => params.append('column', c));
  if (opts?.maxSamples != null) params.set('maxSamples', String(opts.maxSamples));
  const res = await fetch(`/api/entities/${encodeURIComponent(entityId)}/fast-state/${encodeURIComponent(topic)}?${params}`, { signal: opts?.signal });
  if (!res.ok) throw new Error(`getEntityFastState: ${res.status}`);
  return res.json() as Promise<EntityFastStateDto>;
}
```

Also add `export const api = new TracerApiClient();` if not already at the bottom of the file (check first; it's already there).

#### 2b. `entityHistoryStore.ts`

**File:** `tracer-viewer/src/stores/entityHistoryStore.ts`

```typescript
import { defineStore } from 'pinia';
import type { EntitySummaryDto, EntityEventsDto, EntitySlowStateDto } from '@/api/tracerApiClient';

export const useEntityHistoryStore = defineStore('entityHistory', {
  state: () => ({
    entityId: null as string | null,
    sessionId: null as string | null,
    timeRange: {
      from: new Date(),
      to: new Date(),
    },
    summary: null as EntitySummaryDto | null,
    events: null as EntityEventsDto | null,
    slowStateByTopic: {} as Record<string, import('@/api/tracerApiClient').SlowStateSampleDto[]>,
    fastStateTopics: [] as string[],
    selectedEventId: null as string | null,
    loading: false,
    error: null as string | null,
  }),

  actions: {
    setEntity(entityId: string, sessionId: string) {
      this.entityId = entityId;
      this.sessionId = sessionId;
      // Clear prior data
      this.summary = null;
      this.events = null;
      this.slowStateByTopic = {};
      this.fastStateTopics = [];
      this.selectedEventId = null;
      this.error = null;
    },

    setSummary(summary: EntitySummaryDto) {
      this.summary = summary;
      // Default timeRange to entity lifespan only if timeRange is not already user-set
      const isDefault = this.timeRange.from.getTime() === this.timeRange.to.getTime();
      if (isDefault) {
        this.timeRange = {
          from: new Date(summary.firstSeenUtc),
          to: new Date(summary.lastSeenUtc),
        };
      }
    },

    setTimeRange(from: Date, to: Date) {
      this.timeRange = { from, to };
    },

    setResults(
      events: EntityEventsDto,
      slowState: EntitySlowStateDto,
      fastStateTopics: string[]
    ) {
      this.events = events;
      this.slowStateByTopic = slowState.byTopic;
      this.fastStateTopics = fastStateTopics;
    },

    retry() {
      // Signals to useEntityHistoryQuery to re-run by clearing error + toggling a sentinel
      this.error = null;
    },
  },
});
```

**Note:** `setSummary` sets `timeRange` from the entity lifespan ONLY when `from === to` (the store's initial "not yet set" state). If the user already set a time range (via URL restore in `useEntityHistoryUrl`), `from !== to` and `setSummary` does not overwrite it.

#### 2c. `useEntityHistoryQuery.ts`

**File:** `tracer-viewer/src/composables/useEntityHistoryQuery.ts`

Pattern: watch `(store.entityId, store.sessionId)` pair; on change, run sequential summary fetch then parallel events + slow-state + topics fetch. Use `AbortController`.

```typescript
import { watch, onUnmounted } from 'vue';
import { useEntityHistoryStore } from '@/stores/entityHistoryStore';
import { useApi } from '@/api/useApi';

export function useEntityHistoryQuery() {
  const store = useEntityHistoryStore();
  const api = useApi();

  let abortController: AbortController | null = null;

  function cancel() {
    if (abortController) {
      abortController.abort();
      abortController = null;
    }
  }

  async function fetchEntity(entityId: string, sessionId: string) {
    cancel();
    abortController = new AbortController();
    const signal = abortController.signal;

    store.loading = true;
    store.error = null;

    try {
      // Step 1: fetch summary (sequential — provides from/to for subsequent queries)
      const summary = await api.getEntitySummary(entityId, sessionId, { signal });
      if (!summary) {
        store.error = `Entity '${entityId}' not found in session '${sessionId}'`;
        return;
      }
      store.setSummary(summary);

      // Step 2: parallel fetch of events, slow-state, and fast-state topics
      const from = store.timeRange.from;
      const to = store.timeRange.to;

      const [events, slowState, fastStateTopics] = await Promise.all([
        api.getEntityEvents(entityId, sessionId, from, to, { signal }),
        api.getEntitySlowState(entityId, sessionId, from, to, { signal }),
        api.getEntityFastStateTopics(entityId, sessionId, { signal }),
      ]);

      store.setResults(events, slowState, fastStateTopics);
    } catch (err: unknown) {
      if (err instanceof Error && err.name === 'AbortError') return; // silently swallow
      store.error = err instanceof Error ? err.message : String(err);
    } finally {
      store.loading = false;
    }
  }

  const stopWatch = watch(
    () => [store.entityId, store.sessionId] as const,
    ([entityId, sessionId]) => {
      if (entityId && sessionId) fetchEntity(entityId, sessionId);
    },
    { immediate: true },
  );

  onUnmounted(() => {
    cancel();
    stopWatch();
  });
}
```

**Test:** `tracer-viewer/tests/unit/useEntityHistoryQuery.spec.ts`

Using Vitest + `@vue/test-utils` + Pinia test helpers. Mock `useApi()` to return controlled `api` mock objects. Use `createTestingPinia` from `@pinia/testing`.

Tests to implement (from TRC-P7-015 success conditions):
1. **Sequential then parallel**: record call order — `getEntitySummary` must resolve before `getEntityEvents` is called
2. **Parallel fetch**: all three (events, slowState, topics) are in-flight simultaneously
3. **AbortController**: switching entity cancels prior fetch — prior stale data not written
4. **Error handling**: non-abort error sets `store.error`, clears `store.loading`
5. **AbortError is swallowed**: `store.error` remains null
6. **Loading flag lifecycle**: true during fetch, false after settle
7. **Time range defaults to entity lifespan**: after summary resolves with non-zero span

---

### Task 3 — TRC-P7-016: `useEntityHistoryUrl.ts`

**Design reference:** `docs/TASK-DETAIL.md` § TRC-P7-016, `docs/tracer_phase7_design.md` §11

**File:** `tracer-viewer/src/composables/useEntityHistoryUrl.ts`

URL schema (from design §11.1):
- Path param: `entityId` (from route params)
- Query params: `session`, `from`, `to`, `select`
- (`fastStateTopic` and `fastStateColumns` are TRC-P7-017 — do NOT include in this task)

Pattern follows `useTimelineUrl.ts` closely:

```typescript
import { watch, onUnmounted } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { useEntityHistoryStore } from '@/stores/entityHistoryStore';

const URL_DEBOUNCE_MS = 250;

export function useEntityHistoryUrl() {
  const store = useEntityHistoryStore();
  const route = useRoute();
  const router = useRouter();
  let urlDebounceTimer: ReturnType<typeof setTimeout> | null = null;

  // URL → Store
  function applyUrlToStore() {
    const entityId = route.params['entityId'] as string | undefined;
    const sessionId = route.query['session'] as string | undefined;
    const fromStr = route.query['from'] as string | undefined;
    const toStr = route.query['to'] as string | undefined;
    const select = route.query['select'] as string | undefined;

    if (entityId && sessionId) {
      store.setEntity(entityId, sessionId);
    }

    if (fromStr && toStr) {
      store.setTimeRange(new Date(fromStr), new Date(toStr));
    }

    if (select) {
      store.selectedEventId = select;
    }
  }

  applyUrlToStore(); // immediate on mount

  const stopRouteWatch = watch(() => route.query, applyUrlToStore);

  // Store → URL (debounced)
  function scheduleUrlUpdate() {
    if (urlDebounceTimer !== null) clearTimeout(urlDebounceTimer);
    urlDebounceTimer = setTimeout(() => {
      const query: Record<string, string> = {};
      if (store.sessionId) query['session'] = store.sessionId;
      query['from'] = store.timeRange.from.toISOString();
      query['to'] = store.timeRange.to.toISOString();
      if (store.selectedEventId) query['select'] = store.selectedEventId;
      router.replace({ query });
    }, URL_DEBOUNCE_MS);
  }

  const stopStoreWatch = watch(
    () => [
      store.timeRange.from.toISOString(),
      store.timeRange.to.toISOString(),
      store.selectedEventId,
    ],
    scheduleUrlUpdate,
  );

  onUnmounted(() => {
    if (urlDebounceTimer !== null) clearTimeout(urlDebounceTimer);
    stopRouteWatch();
    stopStoreWatch();
  });
}
```

**Test:** `tracer-viewer/tests/unit/useEntityHistoryUrl.spec.ts`

Tests from TRC-P7-016 success conditions:
1. URL → store: entityId + sessionId from route
2. URL → store: from/to parsed as Date objects
3. URL → store: select param sets selectedEventId
4. URL → store: missing from/to leaves timeRange unchanged
5. Store → URL: timeRange change triggers debounced router.replace (use `vi.useFakeTimers()`)
6. Store → URL: selectedEventId in URL
7. Round-trip: navigate to URL with all params, assert store reflects them

---

### Task 4 — TRC-P7-010: `EntityHistoryView.vue` + Router Registration

**Design reference:** `docs/TASK-DETAIL.md` § TRC-P7-010, `docs/tracer_phase7_design.md` §6

#### 4a. `EntityHistoryView.vue`

**File:** `tracer-viewer/src/views/EntityHistoryView.vue`

This is the main view container. It uses stub child components for this batch (the actual rendering components are TRC-P7-011 through TRC-P7-014 for the next batch). For now, child components render as empty `<div>` placeholders.

```vue
<script setup lang="ts">
import { useEntityHistoryStore } from '@/stores/entityHistoryStore';
import { useEntityHistoryQuery } from '@/composables/useEntityHistoryQuery';
import { useEntityHistoryUrl } from '@/composables/useEntityHistoryUrl';
// Stub imports — these components will be implemented in BATCH-38
// For now, create minimal placeholder stubs in src/components/
import EntitySummaryStrip from '@/components/EntitySummaryStrip.vue';
import EntityLifecycleRibbon from '@/components/EntityLifecycleRibbon.vue';
import SlowStateChart from '@/components/SlowStateChart.vue';
import EntityEventStrip from '@/components/EntityEventStrip.vue';
import FastStateDrillDown from '@/components/FastStateDrillDown.vue';

const store = useEntityHistoryStore();
useEntityHistoryUrl(); // URL ↔ store sync
useEntityHistoryQuery(); // drives fetches
</script>

<template>
  <div class="entity-history-view">
    <div v-if="store.loading && !store.summary" class="entity-history-view__loading">
      <LoadingSpinner />
    </div>
    <div v-else-if="store.error && !store.summary" class="entity-history-view__error">
      <ErrorMessage :message="store.error" />
      <button class="entity-history-view__retry" @click="store.retry()">Retry</button>
    </div>
    <template v-else>
      <EntitySummaryStrip v-if="store.summary" :summary="store.summary" />
      <EntityLifecycleRibbon
        v-if="store.events"
        :events="store.events"
        :time-range="store.timeRange"
      />
      <SlowStateChart
        v-for="(samples, topic) in store.slowStateByTopic"
        :key="topic"
        :topic="topic"
        :samples="samples"
        :time-range="store.timeRange"
        @select-event="store.selectedEventId = $event"
      />
      <EntityEventStrip
        v-if="store.events"
        :events="store.events"
        :time-range="store.timeRange"
        :selected-event-id="store.selectedEventId"
        @select="store.selectedEventId = $event"
      />
      <FastStateDrillDown
        :entity-id="store.entityId ?? ''"
        :session-id="store.sessionId ?? ''"
        :available-topics="store.fastStateTopics"
        :time-range="store.timeRange"
      />
    </template>
  </div>
</template>
```

**Stub components** — create minimal stubs in `tracer-viewer/src/components/` for components that will be fully implemented in BATCH-38:

- `EntitySummaryStrip.vue` — accepts `:summary` prop, renders `<div class="entity-summary-strip">{{ summary.entityId }}</div>`
- `EntityLifecycleRibbon.vue` — accepts `:events`, `:time-range` props, renders `<div class="entity-lifecycle-ribbon"></div>`
- `SlowStateChart.vue` — accepts `:topic`, `:samples`, `:time-range` props, emits `select-event`, renders `<div class="slow-state-chart"></div>`
- `EntityEventStrip.vue` — accepts `:events`, `:time-range`, `:selected-event-id` props, emits `select`, renders `<div class="entity-event-strip"></div>`
- `FastStateDrillDown.vue` — accepts `:entity-id`, `:session-id`, `:available-topics`, `:time-range` props, renders `<div class="fast-state-drill-down"></div>`

Also create stub `LoadingSpinner.vue` and `ErrorMessage.vue` if they don't already exist (check `src/components/` first):
- `LoadingSpinner.vue` → `<div class="loading-spinner">Loading...</div>`
- `ErrorMessage.vue` → accepts `:message`, renders `<div class="error-message">{{ message }}</div>`

#### 4b. Vue Router Registration

**File:** `tracer-viewer/src/router/index.ts`

Add route after the existing routes:

```typescript
{
  path: '/v/entity/:entityId',
  name: 'entity-history',
  component: () => import('@/views/EntityHistoryView.vue'),
},
```

**Test:** `tracer-viewer/tests/unit/entityHistoryView.spec.ts`

Tests from TRC-P7-010 success conditions:
1. View renders loading state when `store.loading = true` and `store.summary = null`
2. View renders error state when `store.error` is set and `store.summary = null`; retry button present
3. View renders panel stack when `store.summary` populated and `store.slowStateByTopic` has two topics — assert EntitySummaryStrip, EntityLifecycleRibbon, 2x SlowStateChart, EntityEventStrip, FastStateDrillDown present
4. `entityHistoryStore.setEntity` clears prior data
5. `entityHistoryStore.setSummary` defaults timeRange to entity lifespan when from===to
6. `entityHistoryStore.setSummary` does NOT override an explicit timeRange (when from !== to)
7. Vue Router: `router.resolve({ name: 'entity-history', params: { entityId: 'e1' } }).href === '/v/entity/e1'`
8. Smoke: view mounts without errors when `store.slowStateByTopic = {}`

---

## 3. Test-Driven Task Progression

**MANDATORY WORKFLOW. Apply to EVERY task, no exceptions.**

For each task:

1. **Write the test first** (or alongside) — never write implementation code without the accompanying test
2. **Red → Green** — ensure the test fails before your implementation makes it pass
3. **Run after each task** — verify the test(s) for that task pass before moving on
4. **No silent error swallowing** — let failures fail loudly; no empty catch blocks

**Run commands:**

*Backend tests (after Task 1):*
```powershell
dotnet build d:\Work\Tracer\Tracer.sln -c Release -nologo -v q
dotnet test d:\Work\Tracer\tests\Tracer.Tests.Unit -c Release --filter "FullyQualifiedName~Entity" --no-build 2>&1
```

*Frontend tests (after Tasks 2–4):*
```powershell
cd d:\Work\Tracer\tracer-viewer
pnpm test:unit --run
```

*TypeScript check:*
```powershell
cd d:\Work\Tracer\tracer-viewer
pnpm tsc --noEmit
```

---

## 4. Developer Insights Section

In your report, explicitly answer:

1. **What issues were encountered?** Be specific — include error messages, unexpected constraints, and how you resolved them.
2. **What weak points did you spot in the codebase?** Identify any fragile patterns, missing abstractions, or future landmines even if you didn't fix them.
3. **What design decisions were made beyond the spec?** Describe any deviations, additions, or implementation choices not explicit in the instructions.

---

## 5. Report Format

Write your completion report to `.dev/tracer/reports/BATCH-37-REPORT.md`.

**Required sections:**
1. **Summary** — Tasks completed (✅/❌) with one-line description
2. **Files Created / Modified** — Table of all changed files with purpose
3. **Test Results** — Test counts per suite, pass/fail
4. **Build Status** — C# build (errors/warnings), TypeScript check result
5. **Design Decisions Beyond Spec** — Any deviations or additions
6. **Issues Encountered** — Error messages, debugging notes, resolutions
7. **Weak Points Spotted** — Fragile patterns or future debt discovered
8. **Technical Debt Identified** — New P2/P3 items (if any)
9. **Suggested Git Commit Message**

---

## 6. Success Criteria

- [ ] `FastStateFileLocator.LocateFilesBySafeTopicName` added and used by `EntityFastStateService`
- [ ] `EntityFastStateServiceTests` still pass after DT-027 fix (may need test updates)
- [ ] `tracerApiClient.ts` has all entity DTOs and API methods
- [ ] `entityHistoryStore.ts` created with correct state shape and actions
- [ ] `useEntityHistoryQuery.ts` created with sequential → parallel fetch and abort logic
- [ ] `useEntityHistoryUrl.ts` created with bidirectional URL ↔ store sync
- [ ] `EntityHistoryView.vue` created with correct panel layout
- [ ] Vue Router route `entity-history` registered
- [ ] Stub components created (EntitySummaryStrip, EntityLifecycleRibbon, SlowStateChart, EntityEventStrip, FastStateDrillDown, LoadingSpinner, ErrorMessage)
- [ ] Backend C# build: 0 errors, 0 warnings
- [ ] TypeScript check: 0 errors
- [ ] Entity backend tests: 39/39 pass (no regressions)
- [ ] Frontend unit tests: all pass

---

## 7. Do Not

- Do NOT implement the rendering logic inside stub components (canvas drawing, lifecycle markers, etc.) — that is BATCH-38 work
- Do NOT implement `useFastStateChart.ts` — that is TRC-P7-017 (BATCH-38)
- Do NOT implement `EntityPickerView.vue` — that is TRC-P7-019 (BATCH-38/39)
- Do NOT start TRC-P7-011 through TRC-P7-014 — those are BATCH-38
- Do NOT stop for questions unless there is a breaking design contradiction not covered in these instructions
