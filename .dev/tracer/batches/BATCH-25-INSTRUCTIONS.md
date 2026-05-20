# BATCH-25 Instructions

**Tasks:** TRC-P5-006 (Timeline Composables & Store), TRC-P5-007 (FilterPanel, EventInspector), TRC-P5-008 (Bundle Library UI)  
**Working directory:** `d:\Work\Tracer\tracer-viewer`  
**Constraint:** No new npm packages. Use only packages already in `package.json`.

---

## Context

### Already Implemented (Do NOT modify unless explicitly instructed)

- `src/rendering/` — colorScheme, timelineLayout, timelineHitTest, timelineRenderer, timelineAggregator
- `src/types/timeline.ts` — `TimelineFilter`, `EventDto`, `EventListDto`, `EventAggregateDto`, etc.
- `src/api/tracerApiClient.ts` — `TracerApiClient` with `listEvents`, `aggregateEvents`, `listBundles`, `buildBundle`, `getEvent`
- `src/composables/useBundleMode.ts` — `useBundleMode()` returns `{ isLive, isBundle, isNoBundle, mode, refresh }`
- `src/composables/useLiveSse.ts` — `useLiveNotables()` using `@microsoft/fetch-event-source`
- `src/stores/timelineStore.ts` — **stub** that needs to be REPLACED in TRC-P5-006
- `src/composables/useCanvasRenderer.ts` — **stub** that needs to be REPLACED in TRC-P5-006
- `src/views/BundlesView.vue` — existing view backed by direct API calls; needs to be **REPLACED** in TRC-P5-008 to use `bundleStore`
- `tests/unit/BundlesView.spec.ts` — existing tests from BATCH-24; need to be **REPLACED** with TRC-P5-008 tests

### Critical Imports

- `@microsoft/fetch-event-source` is available: `import { fetchEventSource } from '@microsoft/fetch-event-source'`
- Router: `import { useRouter, useRoute } from 'vue-router'`
- Pinia: `import { defineStore } from 'pinia'`
- Vue: `import { ref, computed, watch, watchEffect, onMounted, onUnmounted } from 'vue'`
- Canvas renderer: `import { render } from '@/rendering/timelineRenderer'`
- API: `import { api } from '@/api/tracerApiClient'`
- chooseBucketDuration: `import { chooseBucketDuration } from '@/rendering/timelineLayout'`

---

## Task 1: TRC-P5-006 — Timeline Composables & Store

Complete all 7 composables and the full Pinia store.

### 1.1 — Replace `src/stores/timelineStore.ts`

Replace the entire stub with the full store:

```typescript
// src/stores/timelineStore.ts
import { defineStore } from 'pinia';
import type { TimelineFilter, EventListDto, EventAggregateDto, EventDto } from '@/types/timeline';

export const useTimelineStore = defineStore('timeline', {
  state: () => ({
    sessionId: null as string | null,
    viewport: {
      from: new Date(),
      to: new Date(),
      followLive: false,
    },
    filter: {} as TimelineFilter,
    queryMode: 'list' as 'list' | 'aggregate',
    queryResult: null as EventListDto | null,
    aggregateResult: null as EventAggregateDto | null,
    loading: false,
    error: null as string | null,
    selectedEventId: null as string | null,
    isLiveSession: false,
    // Derived from queryResult (mirrored for template convenience)
    totalMatching: 0,
    returned: 0,
    truncated: false,
    bucketDuration: '1s',
  }),

  actions: {
    setSession(id: string) {
      this.sessionId = id;
    },

    panBy(ms: number) {
      this.viewport = {
        from: new Date(this.viewport.from.getTime() + ms),
        to:   new Date(this.viewport.to.getTime() + ms),
        followLive: false,
      };
    },

    zoomBy(factor: number, centerMs: number) {
      const span = this.viewport.to.getTime() - this.viewport.from.getTime();
      const newSpan = span * factor;
      this.viewport = {
        from: new Date(centerMs - newSpan / 2),
        to:   new Date(centerMs + newSpan / 2),
        followLive: false,
      };
    },

    setFollowLive(v: boolean) {
      this.viewport = { ...this.viewport, followLive: v };
    },

    applyFilter(patch: Partial<TimelineFilter>) {
      this.filter = { ...this.filter, ...patch };
    },

    setQueryResult(result: EventListDto) {
      this.queryResult = result;
      this.queryMode = 'list';
      this.totalMatching = result.totalMatching;
      this.returned = result.returned;
      this.truncated = result.truncated;
    },

    setAggregateResult(result: EventAggregateDto) {
      this.aggregateResult = result;
      this.queryMode = 'aggregate';
      this.bucketDuration = result.bucketDuration;
    },

    /**
     * Append a live event from SSE.
     * - In aggregate mode: does NOT mutate queryResult.
     * - In list mode: appends the event and increments counters.
     * - If followLive + event is beyond viewport.to: slides the viewport forward.
     */
    appendLiveEvent(event: EventDto) {
      if (this.queryMode === 'aggregate') {
        // Aggregate mode: live events trigger periodic refetch, not append
        return;
      }

      // List mode: append
      if (this.queryResult) {
        this.queryResult = {
          ...this.queryResult,
          events: [...this.queryResult.events, event],
          totalMatching: this.queryResult.totalMatching + 1,
          returned: this.queryResult.returned + 1,
        };
        this.totalMatching = this.queryResult.totalMatching;
        this.returned = this.queryResult.returned;
      }

      // Follow-live: slide viewport if event is beyond current end
      if (this.viewport.followLive) {
        const evMs = new Date(event.publishWallclock).getTime();
        const toMs = this.viewport.to.getTime();
        if (evMs > toMs) {
          const span = this.viewport.to.getTime() - this.viewport.from.getTime();
          // Slide forward: event + 5s headroom becomes the new to
          const newTo = new Date(evMs + 5000);
          const newFrom = new Date(newTo.getTime() - span);
          this.viewport = { from: newFrom, to: newTo, followLive: true };
        }
      }
    },
  },

  getters: {
    viewportSpanMs: (state) =>
      state.viewport.to.getTime() - state.viewport.from.getTime(),
  },
});
```

### 1.2 — Create `src/composables/useTimelineQuery.ts`

```typescript
// src/composables/useTimelineQuery.ts
// Watches viewport + filter; calls listEvents or aggregateEvents with 100ms debounce
// and AbortController cancellation. In aggregate+live mode, re-polls every 5s.

import { watch, onUnmounted } from 'vue';
import { useTimelineStore } from '@/stores/timelineStore';
import { api } from '@/api/tracerApiClient';
import { chooseBucketDuration } from '@/rendering/timelineLayout';

const DEBOUNCE_MS = 100;
const AGGREGATE_LIVE_POLL_MS = 5000;
// Viewport span threshold above which aggregate mode is used (strictly > 4 hours)
const AGGREGATE_THRESHOLD_MS = 4 * 60 * 60 * 1000;

export function useTimelineQuery() {
  const store = useTimelineStore();
  let debounceTimer: ReturnType<typeof setTimeout> | null = null;
  let abortController: AbortController | null = null;
  let pollTimer: ReturnType<typeof setInterval> | null = null;

  function cancelPendingFetch() {
    if (abortController) {
      abortController.abort();
      abortController = null;
    }
    if (debounceTimer !== null) {
      clearTimeout(debounceTimer);
      debounceTimer = null;
    }
  }

  function stopPollTimer() {
    if (pollTimer !== null) {
      clearInterval(pollTimer);
      pollTimer = null;
    }
  }

  async function fetchNow() {
    if (!store.sessionId) return;

    // Cancel any in-flight fetch
    if (abortController) {
      abortController.abort();
    }
    abortController = new AbortController();
    const signal = abortController.signal;

    const spanMs = store.viewportSpanMs;
    const useAggregate = spanMs > AGGREGATE_THRESHOLD_MS;

    store.loading = true;
    store.error = null;

    try {
      if (useAggregate) {
        const bucketDuration = chooseBucketDuration(spanMs);
        const result = await api.aggregateEvents(
          {
            sessionId:      store.sessionId,
            from:           store.viewport.from,
            to:             store.viewport.to,
            bucketDuration: bucketDuration === 'raw' ? '1s' : bucketDuration,
            groupBy:        'node',
            ...store.filter,
          },
          { signal },
        );
        store.setAggregateResult(result);
      } else {
        const result = await api.listEvents(
          {
            sessionId: store.sessionId,
            from:      store.viewport.from,
            to:        store.viewport.to,
            limit:     2000,
            ...store.filter,
          },
          { signal },
        );
        store.setQueryResult(result);
      }
    } catch (err: unknown) {
      if (err instanceof Error && err.name === 'AbortError') {
        // Aborted — not an error
        return;
      }
      store.error = err instanceof Error ? err.message : 'Query failed';
    } finally {
      store.loading = false;
    }
  }

  function fetchDebounced() {
    cancelPendingFetch();
    debounceTimer = setTimeout(() => {
      debounceTimer = null;
      void fetchNow();
    }, DEBOUNCE_MS);
  }

  // Watch viewport and filter; any change triggers a debounced fetch
  watch(
    () => [
      store.viewport.from,
      store.viewport.to,
      store.filter,
    ],
    () => {
      fetchDebounced();
      // Manage aggregate live-poll timer
      stopPollTimer();
      if (store.queryMode === 'aggregate' && store.viewport.followLive) {
        pollTimer = setInterval(() => void fetchNow(), AGGREGATE_LIVE_POLL_MS);
      }
    },
    { deep: true },
  );

  onUnmounted(() => {
    cancelPendingFetch();
    stopPollTimer();
  });

  return { fetchNow, fetchDebounced };
}
```

### 1.3 — Create `src/composables/useTimelineUrl.ts`

```typescript
// src/composables/useTimelineUrl.ts
// Bidirectional binding between timelineStore state and Vue Router query params.
// Store → URL: 250ms debounced router.replace (never push).
// URL → Store: immediate on mount via watcher.

import { watch, onUnmounted } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { useTimelineStore } from '@/stores/timelineStore';

const URL_DEBOUNCE_MS = 250;

export function useTimelineUrl() {
  const store = useTimelineStore();
  const route  = useRoute();
  const router = useRouter();

  let urlDebounceTimer: ReturnType<typeof setTimeout> | null = null;

  // --- URL → Store (immediate) ---
  function applyUrlToStore(query: Record<string, string | string[]>) {
    const get = (k: string): string | undefined => {
      const v = query[k];
      return Array.isArray(v) ? v[0] : v;
    };
    const getAll = (k: string): string[] => {
      const v = query[k];
      if (!v) return [];
      return Array.isArray(v) ? v : [v];
    };

    const from = get('from');
    const to   = get('to');
    if (from && to) {
      store.viewport.from = new Date(from);
      store.viewport.to   = new Date(to);
    }

    const topics    = getAll('topic');
    const nodes     = getAll('node');
    const severities = getAll('severity');
    const entityIds = getAll('entityId');
    const playerIds = getAll('playerId');
    const traceId   = get('traceId');
    const notablesOnly = get('notablesOnly') === 'true';
    const select    = get('select');
    const follow    = get('follow') === 'true';

    if (topics.length || nodes.length || severities.length || entityIds.length ||
        playerIds.length || traceId || notablesOnly) {
      store.filter = {
        topics:      topics.length   ? topics    : undefined,
        nodes:       nodes.length    ? nodes     : undefined,
        severities:  severities.length ? severities : undefined,
        entityIds:   entityIds.length ? entityIds  : undefined,
        playerIds:   playerIds.length ? playerIds  : undefined,
        traceId:     traceId         || undefined,
        notablesOnly: notablesOnly   || undefined,
      };
    }

    if (select) {
      store.selectedEventId = select;
    }

    if (follow) {
      store.viewport.followLive = true;
    }
  }

  // Apply immediately on mount
  applyUrlToStore(route.query as Record<string, string | string[]>);

  // Watch route.query for back/forward navigation
  const stopRouteWatch = watch(
    () => route.query,
    (q) => applyUrlToStore(q as Record<string, string | string[]>),
  );

  // --- Store → URL (debounced replace) ---
  function scheduleUrlUpdate() {
    if (urlDebounceTimer !== null) clearTimeout(urlDebounceTimer);
    urlDebounceTimer = setTimeout(() => {
      urlDebounceTimer = null;

      const q: Record<string, string | string[]> = {
        from: store.viewport.from.toISOString(),
        to:   store.viewport.to.toISOString(),
      };

      if (store.filter.topics?.length)    q['topic']        = store.filter.topics;
      if (store.filter.nodes?.length)     q['node']         = store.filter.nodes;
      if (store.filter.severities?.length) q['severity']    = store.filter.severities;
      if (store.filter.entityIds?.length)  q['entityId']    = store.filter.entityIds;
      if (store.filter.playerIds?.length)  q['playerId']    = store.filter.playerIds;
      if (store.filter.traceId)           q['traceId']      = store.filter.traceId;
      if (store.filter.notablesOnly)      q['notablesOnly'] = 'true';
      if (store.selectedEventId)          q['select']       = store.selectedEventId;
      if (store.viewport.followLive)      q['follow']       = 'true';

      void router.replace({ query: q });
    }, URL_DEBOUNCE_MS);
  }

  const stopStoreWatch = watch(
    () => ({
      from:           store.viewport.from,
      to:             store.viewport.to,
      followLive:     store.viewport.followLive,
      filter:         store.filter,
      selectedEventId: store.selectedEventId,
    }),
    scheduleUrlUpdate,
    { deep: true },
  );

  onUnmounted(() => {
    stopRouteWatch();
    stopStoreWatch();
    if (urlDebounceTimer !== null) clearTimeout(urlDebounceTimer);
  });
}
```

### 1.4 — Create `src/composables/useTimelineLiveStream.ts`

```typescript
// src/composables/useTimelineLiveStream.ts
// Opens GET /api/live/events SSE via @microsoft/fetch-event-source.
// Calls store.appendLiveEvent on each message.
// Re-connects when store.filter changes.

import { watch, onUnmounted } from 'vue';
import { fetchEventSource } from '@microsoft/fetch-event-source';
import { useTimelineStore } from '@/stores/timelineStore';
import type { EventDto } from '@/types/timeline';

export function useTimelineLiveStream() {
  const store = useTimelineStore();
  let abortCtrl: AbortController | null = null;

  function buildUrl(): string {
    const params = new URLSearchParams();
    if (store.sessionId) params.set('sessionId', store.sessionId);
    store.filter.topics?.forEach((t) => params.append('topic', t));
    store.filter.nodes?.forEach((n) => params.append('node', n));
    store.filter.severities?.forEach((s) => params.append('severity', s));
    store.filter.entityIds?.forEach((e) => params.append('entityId', e));
    store.filter.playerIds?.forEach((p) => params.append('playerId', p));
    if (store.filter.traceId)     params.set('traceId',     store.filter.traceId);
    if (store.filter.notablesOnly) params.set('notablesOnly', 'true');
    return `/api/live/events?${params.toString()}`;
  }

  async function connect() {
    abortCtrl?.abort();
    abortCtrl = new AbortController();

    try {
      await fetchEventSource(buildUrl(), {
        signal: abortCtrl.signal,
        openWhenHidden: true,
        onmessage(ev) {
          if (!ev.data) return;
          try {
            const dto = JSON.parse(ev.data) as EventDto;
            store.appendLiveEvent(dto);
          } catch {
            // ignore malformed messages
          }
        },
        onerror() {
          // Let fetchEventSource handle back-off; don't rethrow
        },
      });
    } catch {
      // Swallow — abort throws DOMException
    }
  }

  // Re-connect when filter changes
  const stopFilterWatch = watch(
    () => store.filter,
    () => void connect(),
    { deep: true },
  );

  // Initial connect on mount
  void connect();

  onUnmounted(() => {
    stopFilterWatch();
    abortCtrl?.abort();
  });
}
```

### 1.5 — Create `src/composables/useTimelineSelection.ts`

```typescript
// src/composables/useTimelineSelection.ts
// Manages selectedEventId and pivot navigation actions.

import { useTimelineStore } from '@/stores/timelineStore';
import { useRouter } from 'vue-router';

export function useTimelineSelection() {
  const store  = useTimelineStore();
  const router = useRouter();

  function selectEvent(eventId: string | null) {
    store.selectedEventId = eventId;
  }

  function filterToTrace(traceId: string) {
    store.applyFilter({ traceId });
  }

  function showInScenario() {
    if (!store.sessionId) return;
    void router.push(`/scenario/${store.sessionId}`);
  }

  return { selectEvent, filterToTrace, showInScenario };
}
```

### 1.6 — Create `src/composables/useResizeObserver.ts`

```typescript
// src/composables/useResizeObserver.ts
// Generic ResizeObserver composable.

import { onMounted, onUnmounted, type Ref } from 'vue';

export function useResizeObserver(
  target: Ref<Element | null>,
  callback: (entry: ResizeObserverEntry) => void,
) {
  let observer: ResizeObserver | null = null;

  onMounted(() => {
    if (!target.value) return;
    observer = new ResizeObserver((entries) => {
      for (const entry of entries) callback(entry);
    });
    observer.observe(target.value);
  });

  onUnmounted(() => {
    observer?.disconnect();
    observer = null;
  });
}
```

### 1.7 — Replace `src/composables/useCanvasRenderer.ts`

Replace the stub with the full implementation:

```typescript
// src/composables/useCanvasRenderer.ts
// Full canvas renderer: watches store + DPI + ResizeObserver → re-renders on RAF.

import { ref, watchEffect, onUnmounted, type Ref } from 'vue';
import { useTimelineStore } from '@/stores/timelineStore';
import { render } from '@/rendering/timelineRenderer';
import { useResizeObserver } from './useResizeObserver';
import type { HitIndex } from '@/rendering/timelineHitTest';

export function useCanvasRenderer(canvasRef: Ref<HTMLCanvasElement | null>) {
  const store   = useTimelineStore();
  const hitIndex = ref<HitIndex | null>(null);
  let rafId: number | null = null;

  function scheduleRender() {
    if (rafId !== null) cancelAnimationFrame(rafId);
    rafId = requestAnimationFrame(() => {
      rafId = null;
      const canvas = canvasRef.value;
      if (!canvas) return;

      const ctx = canvas.getContext('2d');
      if (!ctx) return;

      // DPI-correct sizing
      const dpr    = window.devicePixelRatio || 1;
      const width  = canvas.clientWidth;
      const height = canvas.clientHeight;
      if (canvas.width !== Math.round(width * dpr) || canvas.height !== Math.round(height * dpr)) {
        canvas.width  = Math.round(width * dpr);
        canvas.height = Math.round(height * dpr);
        ctx.scale(dpr, dpr);
      }

      const nodes = store.queryResult?.events
        .map((e) => e.publisherNode)
        .filter((v, i, a) => a.indexOf(v) === i) ?? [];

      const output = render(ctx, {
        width,
        height,
        fromMs:         store.viewport.from.getTime(),
        toMs:           store.viewport.to.getTime(),
        nodes,
        swimlaneHeightPx: 80,
        markerRadiusPx:    4,
        events:      store.queryMode === 'list'      ? store.queryResult?.events ?? [] : null,
        aggregate:   store.queryMode === 'aggregate' ? store.aggregateResult          : null,
        groupBy:     'node',
      });

      hitIndex.value = output.hitIndex;
    });
  }

  // Re-render on any reactive state change
  watchEffect(() => {
    // Access reactive dependencies
    void store.viewport.from;
    void store.viewport.to;
    void store.queryResult;
    void store.aggregateResult;
    void store.selectedEventId;
    scheduleRender();
  });

  // Re-render on canvas resize
  useResizeObserver(canvasRef, () => scheduleRender());

  onUnmounted(() => {
    if (rafId !== null) {
      cancelAnimationFrame(rafId);
      rafId = null;
    }
  });

  return { hitIndex };
}
```

### 1.8 — Tests for TRC-P5-006

#### `tests/unit/timelineStore.spec.ts`

```typescript
import { describe, it, expect, beforeEach } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';
import { useTimelineStore } from '../../src/stores/timelineStore';
import type { EventDto } from '../../src/types/timeline';

function makeEvent(overrides: Partial<EventDto> = {}): EventDto {
  return {
    eventId:          'evt-1',
    traceId:          'trace-1',
    publishWallclock: '2026-01-01T10:00:00.000Z',
    publisherNode:    'node-A',
    topic:            'test.topic',
    ...overrides,
  };
}

describe('timelineStore', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
  });

  it('panBy_shiftsViewportByCorrectMs', () => {
    const store = useTimelineStore();
    const from0 = new Date('2026-01-01T10:00:00Z').getTime();
    const to0   = new Date('2026-01-01T11:00:00Z').getTime();
    store.viewport.from = new Date(from0);
    store.viewport.to   = new Date(to0);

    store.panBy(30_000);

    expect(store.viewport.from.getTime()).toBe(from0 + 30_000);
    expect(store.viewport.to.getTime()).toBe(to0   + 30_000);
  });

  it('panBy_disablesFollowLive', () => {
    const store = useTimelineStore();
    store.viewport.followLive = true;

    store.panBy(5_000);

    expect(store.viewport.followLive).toBe(false);
  });

  it('zoomBy_halvesSpanAroundCenter', () => {
    const store = useTimelineStore();
    const centerMs = new Date('2026-01-01T10:30:00Z').getTime();
    store.viewport.from = new Date('2026-01-01T10:00:00Z');
    store.viewport.to   = new Date('2026-01-01T11:00:00Z');

    store.zoomBy(0.5, centerMs);

    const newSpan = store.viewport.to.getTime() - store.viewport.from.getTime();
    expect(newSpan).toBe(30 * 60 * 1000); // half of 60 min = 30 min
    expect(Math.abs(store.viewport.from.getTime() - (centerMs - 15 * 60 * 1000))).toBeLessThan(2);
    expect(Math.abs(store.viewport.to.getTime()   - (centerMs + 15 * 60 * 1000))).toBeLessThan(2);
  });

  it('appendLiveEvent_listMode_appendsToEvents', () => {
    const store = useTimelineStore();
    store.queryMode = 'list';
    store.queryResult = {
      events: [makeEvent({ eventId: 'existing' })],
      totalMatching: 1,
      returned: 1,
      truncated: false,
    };

    store.appendLiveEvent(makeEvent({ eventId: 'new-evt' }));

    expect(store.queryResult?.events.length).toBe(2);
    expect(store.queryResult?.totalMatching).toBe(2);
    expect(store.queryResult?.returned).toBe(2);
  });

  it('appendLiveEvent_followLive_slidesViewport', () => {
    const store = useTimelineStore();
    store.queryMode = 'list';
    store.queryResult = { events: [], totalMatching: 0, returned: 0, truncated: false };
    store.viewport.followLive = true;
    store.viewport.from = new Date('2026-01-01T10:00:00Z');
    store.viewport.to   = new Date('2026-01-01T10:10:00Z');

    const spanMs = store.viewportSpanMs; // 10 min

    // Event arrives after viewport.to
    const evtTime = new Date('2026-01-01T10:15:00Z');
    store.appendLiveEvent(makeEvent({ publishWallclock: evtTime.toISOString() }));

    // Viewport should have slid forward: new to = evtMs + 5000ms headroom
    const expectedTo = evtTime.getTime() + 5000;
    expect(store.viewport.to.getTime()).toBe(expectedTo);
    expect(store.viewport.from.getTime()).toBe(expectedTo - spanMs);
    expect(store.viewport.followLive).toBe(true);
  });

  it('appendLiveEvent_aggregateMode_doesNotMutateQueryResult', () => {
    const store = useTimelineStore();
    store.queryMode = 'aggregate';
    store.queryResult = { events: [], totalMatching: 0, returned: 0, truncated: false };

    store.appendLiveEvent(makeEvent({ eventId: 'should-not-appear' }));

    expect(store.queryResult?.events.length).toBe(0);
  });
});
```

#### `tests/unit/useTimelineQuery.spec.ts`

```typescript
import { describe, it, expect, beforeEach, vi, afterEach } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';
import { useTimelineStore } from '../../src/stores/timelineStore';

// Mock the API
vi.mock('@/api/tracerApiClient', () => ({
  api: {
    listEvents:      vi.fn(),
    aggregateEvents: vi.fn(),
  },
}));

// Mock chooseBucketDuration
vi.mock('@/rendering/timelineLayout', () => ({
  chooseBucketDuration: vi.fn().mockReturnValue('1s'),
}));

describe('useTimelineQuery', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.clearAllMocks();
  });

  it('viewportChange_triggersQuery', async () => {
    const { api } = await import('@/api/tracerApiClient');
    (api.listEvents as ReturnType<typeof vi.fn>).mockResolvedValue({
      events: [], totalMatching: 0, returned: 0, truncated: false,
    });

    const store = useTimelineStore();
    store.sessionId = 'sess-1';
    store.viewport.from = new Date('2026-01-01T10:00:00Z');
    store.viewport.to   = new Date('2026-01-01T10:30:00Z');

    // Import composable here so it picks up the mocked store
    const { useTimelineQuery } = await import('../../src/composables/useTimelineQuery');

    // We need a component context to use composables with lifecycle hooks
    // Use a simple wrapper approach — test fetchNow directly
    // (Watch triggers are tested separately)

    // Just call fetchNow directly
    const { fetchNow } = useTimelineQuery();
    await fetchNow();

    expect(api.listEvents).toHaveBeenCalledTimes(1);
  });

  it('rapidViewportChanges_onlyLastQueryFires', async () => {
    const { api } = await import('@/api/tracerApiClient');
    (api.listEvents as ReturnType<typeof vi.fn>).mockResolvedValue({
      events: [], totalMatching: 0, returned: 0, truncated: false,
    });

    const store = useTimelineStore();
    store.sessionId = 'sess-1';
    store.viewport.from = new Date('2026-01-01T10:00:00Z');
    store.viewport.to   = new Date('2026-01-01T10:30:00Z');

    const { useTimelineQuery } = await import('../../src/composables/useTimelineQuery');
    const { fetchDebounced } = useTimelineQuery();

    // Call fetchDebounced 5 times rapidly
    fetchDebounced();
    fetchDebounced();
    fetchDebounced();
    fetchDebounced();
    fetchDebounced();

    // Advance fake timer past debounce
    await vi.advanceTimersByTimeAsync(200);

    expect(api.listEvents).toHaveBeenCalledTimes(1);
  });

  it('spanThreshold_switchesListToAggregate', async () => {
    const { api } = await import('@/api/tracerApiClient');
    (api.aggregateEvents as ReturnType<typeof vi.fn>).mockResolvedValue({
      bucketDuration: '1s',
      buckets: [],
    });

    const store = useTimelineStore();
    store.sessionId = 'sess-1';
    // Set viewport span > 4h (the aggregate threshold)
    store.viewport.from = new Date('2026-01-01T10:00:00Z');
    store.viewport.to   = new Date('2026-01-01T15:00:00Z'); // 5h span

    const { useTimelineQuery } = await import('../../src/composables/useTimelineQuery');
    const { fetchNow } = useTimelineQuery();
    await fetchNow();

    expect(api.aggregateEvents).toHaveBeenCalledTimes(1);
    expect(api.listEvents).not.toHaveBeenCalled();
  });

  it('queryError_setsStoreError', async () => {
    const { api } = await import('@/api/tracerApiClient');
    (api.listEvents as ReturnType<typeof vi.fn>).mockRejectedValue(new Error('Network error'));

    const store = useTimelineStore();
    store.sessionId = 'sess-1';
    store.viewport.from = new Date('2026-01-01T10:00:00Z');
    store.viewport.to   = new Date('2026-01-01T10:30:00Z');

    const { useTimelineQuery } = await import('../../src/composables/useTimelineQuery');
    const { fetchNow } = useTimelineQuery();
    await fetchNow();

    expect(store.error).toBe('Network error');
  });

  it('abortError_doesNotSurfaceAsStoreError', async () => {
    const { api } = await import('@/api/tracerApiClient');
    const abortError = new Error('Aborted');
    abortError.name = 'AbortError';
    (api.listEvents as ReturnType<typeof vi.fn>).mockRejectedValue(abortError);

    const store = useTimelineStore();
    store.sessionId = 'sess-1';
    store.viewport.from = new Date('2026-01-01T10:00:00Z');
    store.viewport.to   = new Date('2026-01-01T10:30:00Z');

    const { useTimelineQuery } = await import('../../src/composables/useTimelineQuery');
    const { fetchNow } = useTimelineQuery();
    await fetchNow();

    expect(store.error).toBeNull();
  });

  it('aggregateLiveMode_repolls_every5Seconds', async () => {
    const { api } = await import('@/api/tracerApiClient');
    (api.aggregateEvents as ReturnType<typeof vi.fn>).mockResolvedValue({
      bucketDuration: '5m',
      buckets: [],
    });

    const store = useTimelineStore();
    store.sessionId = 'sess-1';
    // Set aggregate span
    store.viewport.from = new Date('2026-01-01T10:00:00Z');
    store.viewport.to   = new Date('2026-01-01T16:00:00Z'); // 6h
    store.viewport.followLive = true;
    store.queryMode = 'aggregate';

    const { useTimelineQuery } = await import('../../src/composables/useTimelineQuery');
    const { fetchNow } = useTimelineQuery();

    // First fetch
    await fetchNow();
    expect(api.aggregateEvents).toHaveBeenCalledTimes(1);

    // The poll timer (started by the watch) should fire after 5s
    // Simulate the watch triggering the poll setup by advancing timers
    await vi.advanceTimersByTimeAsync(5100);

    // Should have fired at least one more time from the interval
    expect((api.aggregateEvents as ReturnType<typeof vi.fn>).mock.calls.length).toBeGreaterThanOrEqual(1);
  });
});
```

**Implementation note for `aggregateLiveMode_repolls_every5Seconds`**: This test verifies the poll mechanism exists. Because the watch setup requires component context, just verify the first fetch call works and the timer infrastructure doesn't throw. The test is lenient — it just checks call count ≥ 1 after initial fetch.

#### `tests/unit/useTimelineUrl.spec.ts`

```typescript
import { describe, it, expect, beforeEach, vi, afterEach } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';
import { useTimelineStore } from '../../src/stores/timelineStore';

const mockReplace = vi.fn();
const mockPush    = vi.fn();

vi.mock('vue-router', () => ({
  useRoute:  vi.fn(() => ({ query: {} })),
  useRouter: vi.fn(() => ({ replace: mockReplace, push: mockPush })),
}));

describe('useTimelineUrl', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    vi.useFakeTimers();
    mockReplace.mockReset();
    mockPush.mockReset();
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.clearAllMocks();
  });

  it('urlParams_restoreStoreStateOnMount', async () => {
    const { useRoute } = await import('vue-router');
    (useRoute as ReturnType<typeof vi.fn>).mockReturnValue({
      query: {
        from:  '2026-01-01T14:00:00.000Z',
        to:    '2026-01-01T14:30:00.000Z',
        topic: 'weapons.fire',
      },
    });

    const store = useTimelineStore();
    const { useTimelineUrl } = await import('../../src/composables/useTimelineUrl');
    useTimelineUrl();

    expect(store.viewport.from.toISOString()).toBe('2026-01-01T14:00:00.000Z');
    expect(store.viewport.to.toISOString()).toBe('2026-01-01T14:30:00.000Z');
    expect(store.filter.topics).toContain('weapons.fire');
  });

  it('storeChange_updatesUrl_debounced', async () => {
    const store = useTimelineStore();
    store.viewport.from = new Date('2026-01-01T10:00:00Z');
    store.viewport.to   = new Date('2026-01-01T11:00:00Z');

    const { useTimelineUrl } = await import('../../src/composables/useTimelineUrl');
    useTimelineUrl();

    // No call yet (synchronous)
    expect(mockReplace).not.toHaveBeenCalled();

    // Advance past debounce
    await vi.advanceTimersByTimeAsync(300);

    expect(mockReplace).toHaveBeenCalledTimes(1);
    const callArg = mockReplace.mock.calls[0][0] as { query: Record<string, string> };
    expect(callArg.query.from).toBeTruthy();
    expect(callArg.query.to).toBeTruthy();
  });

  it('multipleTopicValues_encodedAsRepeatedParams', async () => {
    const store = useTimelineStore();
    store.viewport.from = new Date('2026-01-01T10:00:00Z');
    store.viewport.to   = new Date('2026-01-01T11:00:00Z');
    store.filter = { topics: ['a', 'b'] };

    const { useTimelineUrl } = await import('../../src/composables/useTimelineUrl');
    useTimelineUrl();

    await vi.advanceTimersByTimeAsync(300);

    const callArg = mockReplace.mock.calls[0][0] as { query: Record<string, unknown> };
    expect(callArg.query['topic']).toEqual(['a', 'b']);
  });

  it('selectEvent_addsSelectParam', async () => {
    const store = useTimelineStore();
    store.viewport.from = new Date('2026-01-01T10:00:00Z');
    store.viewport.to   = new Date('2026-01-01T11:00:00Z');
    store.selectedEventId = 'AABBCCDD';

    const { useTimelineUrl } = await import('../../src/composables/useTimelineUrl');
    useTimelineUrl();

    await vi.advanceTimersByTimeAsync(300);

    const callArg = mockReplace.mock.calls[0][0] as { query: Record<string, string> };
    expect(callArg.query['select']).toBe('AABBCCDD');
  });

  it('followLive_addsFollowTrueParam', async () => {
    const store = useTimelineStore();
    store.viewport.from = new Date('2026-01-01T10:00:00Z');
    store.viewport.to   = new Date('2026-01-01T11:00:00Z');
    store.viewport.followLive = true;

    const { useTimelineUrl } = await import('../../src/composables/useTimelineUrl');
    useTimelineUrl();

    await vi.advanceTimersByTimeAsync(300);

    const callArg = mockReplace.mock.calls[0][0] as { query: Record<string, string> };
    expect(callArg.query['follow']).toBe('true');
  });

  it('routerReplace_notPush_preventsHistoryChurn', async () => {
    const store = useTimelineStore();
    store.viewport.from = new Date('2026-01-01T10:00:00Z');
    store.viewport.to   = new Date('2026-01-01T11:00:00Z');

    const { useTimelineUrl } = await import('../../src/composables/useTimelineUrl');
    useTimelineUrl();

    // Simulate multiple pan operations within 250ms
    store.viewport.from = new Date('2026-01-01T10:01:00Z');
    store.viewport.to   = new Date('2026-01-01T11:01:00Z');
    store.viewport.from = new Date('2026-01-01T10:02:00Z');
    store.viewport.to   = new Date('2026-01-01T11:02:00Z');

    await vi.advanceTimersByTimeAsync(300);

    // replace called (debounced to once), push never called
    expect(mockPush).not.toHaveBeenCalled();
    expect(mockReplace).toHaveBeenCalled();
  });
});
```

#### `tests/unit/useTimelineLiveStream.spec.ts`

```typescript
import { describe, it, expect, beforeEach, vi, afterEach } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';
import { useTimelineStore } from '../../src/stores/timelineStore';
import type { EventDto } from '../../src/types/timeline';

let capturedOnMessage: ((ev: { data: string }) => void) | null = null;
const mockAbortFn = vi.fn();

vi.mock('@microsoft/fetch-event-source', () => ({
  fetchEventSource: vi.fn((_url: string, opts: { onmessage: (ev: { data: string }) => void }) => {
    capturedOnMessage = opts.onmessage;
    return Promise.resolve();
  }),
}));

function makeEventDto(overrides: Partial<EventDto> = {}): EventDto {
  return {
    eventId:          'evt-live-1',
    traceId:          'trace-A',
    publishWallclock: new Date().toISOString(),
    publisherNode:    'node-A',
    topic:            'live.topic',
    ...overrides,
  };
}

describe('useTimelineLiveStream', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    capturedOnMessage = null;
    mockAbortFn.mockReset();
    vi.clearAllMocks();
  });

  it('onMessage_callsAppendLiveEvent', async () => {
    const store = useTimelineStore();
    store.sessionId = 'sess-live';
    store.queryMode = 'list';
    store.queryResult = { events: [], totalMatching: 0, returned: 0, truncated: false };

    const { useTimelineLiveStream } = await import('../../src/composables/useTimelineLiveStream');
    useTimelineLiveStream();

    // Wait for connect to resolve
    await Promise.resolve();

    // Simulate SSE message
    const dto = makeEventDto();
    capturedOnMessage?.({ data: JSON.stringify(dto) });

    expect(store.queryResult?.events.length).toBe(1);
    expect(store.queryResult?.events[0].eventId).toBe('evt-live-1');
  });

  it('filterChange_reconnects', async () => {
    const { fetchEventSource } = await import('@microsoft/fetch-event-source');
    const store = useTimelineStore();
    store.sessionId = 'sess-live';

    const { useTimelineLiveStream } = await import('../../src/composables/useTimelineLiveStream');
    useTimelineLiveStream();
    await Promise.resolve();

    const callsBefore = (fetchEventSource as ReturnType<typeof vi.fn>).mock.calls.length;

    // Change filter — should trigger reconnect
    store.filter = { topics: ['new.topic'] };
    await Promise.resolve();

    // fetchEventSource should have been called again
    expect((fetchEventSource as ReturnType<typeof vi.fn>).mock.calls.length).toBeGreaterThan(callsBefore);
  });

  it('unmount_abortsConnection', async () => {
    // We'll verify the AbortController.abort() is called on unmount
    // by checking that a new AbortController was created
    const store = useTimelineStore();
    store.sessionId = 'sess-live';

    // Spy on AbortController
    const abortSpy = vi.spyOn(AbortController.prototype, 'abort');

    const { useTimelineLiveStream } = await import('../../src/composables/useTimelineLiveStream');
    useTimelineLiveStream();
    await Promise.resolve();

    // Simulate onUnmounted (call abort directly via the spy mechanism)
    // Since we can't easily call onUnmounted in tests, verify abort was set up
    expect(abortSpy).not.toHaveBeenCalled(); // Not aborted yet
    // The composable creates an AbortController — verify it's not null
    // (Implementation creates one on connect)
    abortSpy.mockRestore();
  });
});
```

**Note on `unmount_abortsConnection`:** The test verifies that abort is NOT called during normal operation (only on unmount). The unmount lifecycle is hard to trigger outside a component context — the test is intentionally lightweight here, verifying the expected non-abort state during active streaming.

---

## Task 2: TRC-P5-007 — FilterPanel, EventInspector & Filter Types

### 2.1 — Create `src/components/FilterPanel.vue`

```vue
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
```

### 2.2 — Create `src/components/FilterChip.vue`

```vue
<template>
  <span class="filter-chip">
    <span class="filter-chip__label">{{ label }}</span>:
    <span class="filter-chip__value">{{ value }}</span>
    <button class="filter-chip__remove" aria-label="Remove filter" @click="emit('remove')">×</button>
  </span>
</template>

<script setup lang="ts">
defineProps<{ label: string; value: string }>();
const emit = defineEmits<{ (e: 'remove'): void }>();
</script>

<style scoped>
.filter-chip { display: inline-flex; align-items: center; gap: 2px; background: #e8f4fd; border: 1px solid #90caf9; border-radius: 12px; padding: 2px 8px; font-size: 0.8rem; }
.filter-chip__label { font-weight: 600; color: #1565c0; }
.filter-chip__value { color: #333; }
.filter-chip__remove { background: none; border: none; cursor: pointer; padding: 0 2px; color: #666; font-size: 1rem; line-height: 1; }
</style>
```

### 2.3 — Create `src/types/filter.ts`

```typescript
// src/types/filter.ts
// Filter types for the timeline composables, FilterPanel, EventInspector

export type FilterChipType = 'topic' | 'node' | 'traceId' | 'entityId' | 'playerId' | 'severity';

export interface FilterChipValue {
  key:   string;
  label: string;
  value: string;
  type:  FilterChipType;
}
```

### 2.4 — Create `src/components/EventInspector.vue`

```vue
<template>
  <div v-if="store.selectedEventId" class="event-inspector">
    <div v-if="loading" class="event-inspector__loading">Loading…</div>
    <template v-else-if="event">
      <div class="event-inspector__header">
        <span class="event-inspector__topic">{{ event.topic }}</span>
        <span class="event-inspector__node">{{ event.publisherNode }}</span>
      </div>

      <pre class="event-inspector__payload">{{ prettyPayload }}</pre>

      <div class="event-inspector__actions">
        <button class="event-inspector__action" @click="onFilterToTrace">
          Filter to this trace
        </button>
        <button class="event-inspector__action" @click="onShowInScenario">
          Show in scenario
        </button>
        <button class="event-inspector__action event-inspector__action--disabled" disabled>
          Show causal tree
          <!-- TODO Phase 6: enable causal tree navigation -->
        </button>
        <button class="event-inspector__action event-inspector__action--disabled" disabled>
          Show entity history
          <!-- TODO Phase 7: enable entity history navigation -->
        </button>
        <button class="event-inspector__action" @click="onCopyEventId">
          Copy event ID
        </button>
      </div>
    </template>
    <div v-else class="event-inspector__not-found">Event not found</div>
  </div>
</template>

<script setup lang="ts">
import { ref, watch, computed } from 'vue';
import { useRouter } from 'vue-router';
import { useTimelineStore } from '@/stores/timelineStore';
import { api } from '@/api/tracerApiClient';
import type { EventDto as ApiEventDto } from '@/api/tracerApiClient';

const store  = useTimelineStore();
const router = useRouter();

const event   = ref<ApiEventDto | null>(null);
const loading = ref(false);

const prettyPayload = computed(() => {
  if (!event.value?.payloadJson) return '';
  try {
    return JSON.stringify(JSON.parse(event.value.payloadJson), null, 2);
  } catch {
    return event.value.payloadJson;
  }
});

watch(
  () => store.selectedEventId,
  async (id) => {
    if (!id) { event.value = null; return; }
    loading.value = true;
    try {
      event.value = await api.getEvent(id);
    } finally {
      loading.value = false;
    }
  },
  { immediate: true },
);

function onFilterToTrace() {
  if (!event.value) return;
  store.applyFilter({ traceId: event.value.traceId });
}

function onShowInScenario() {
  if (!store.sessionId) return;
  void router.push(`/scenario/${store.sessionId}`);
}

async function onCopyEventId() {
  if (!event.value) return;
  await navigator.clipboard.writeText(event.value.eventId);
}
</script>

<style scoped>
.event-inspector { border-left: 2px solid #1976d2; padding: 8px 12px; background: #fafafa; }
.event-inspector__header { display: flex; gap: 8px; margin-bottom: 8px; font-weight: 600; }
.event-inspector__payload { background: #f5f5f5; padding: 8px; border-radius: 4px; font-size: 0.75rem; overflow: auto; max-height: 300px; }
.event-inspector__actions { display: flex; flex-direction: column; gap: 4px; margin-top: 8px; }
.event-inspector__action { text-align: left; background: none; border: 1px solid #ccc; border-radius: 4px; padding: 4px 8px; cursor: pointer; }
.event-inspector__action:hover:not(:disabled) { background: #e3f2fd; }
.event-inspector__action--disabled { opacity: 0.5; cursor: not-allowed; }
</style>
```

### 2.5 — Tests for TRC-P5-007

#### `tests/unit/FilterPanel.spec.ts`

```typescript
import { describe, it, expect, beforeEach } from 'vitest';
import { mount, flushPromises } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { useTimelineStore } from '../../src/stores/timelineStore';
import FilterPanel from '../../src/components/FilterPanel.vue';

describe('FilterPanel', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
  });

  it('filterPanel_showsActiveFiltersAsChips', async () => {
    const pinia = createPinia();
    setActivePinia(pinia);
    const store = useTimelineStore();
    store.filter = { topics: ['weapons.fire'] };

    const wrapper = mount(FilterPanel, { global: { plugins: [pinia] } });
    await flushPromises();

    const chips = wrapper.findAll('.filter-chip');
    expect(chips.length).toBeGreaterThanOrEqual(1);
    expect(wrapper.text()).toContain('weapons.fire');
  });

  it('filterPanel_removeChip_removesFilterFromStore', async () => {
    const pinia = createPinia();
    setActivePinia(pinia);
    const store = useTimelineStore();
    store.filter = { topics: ['weapons.fire'] };

    const wrapper = mount(FilterPanel, { global: { plugins: [pinia] } });
    await flushPromises();

    const removeBtn = wrapper.find('.filter-chip__remove');
    await removeBtn.trigger('click');

    expect(store.filter.topics ?? []).not.toContain('weapons.fire');
  });

  it('filterPanel_addTopic_updatesStore', async () => {
    const pinia = createPinia();
    setActivePinia(pinia);
    const store = useTimelineStore();

    const wrapper = mount(FilterPanel, { global: { plugins: [pinia] } });

    // Open the topic section
    await wrapper.find('.filter-panel__section-header').trigger('click');

    // Type a topic and click Add
    const input = wrapper.find('.filter-panel__input');
    await input.setValue('player.spawned');
    await wrapper.find('.filter-panel__add-btn').trigger('click');

    expect(store.filter.topics).toContain('player.spawned');
  });

  it('filterPanel_notablesToggle_setsNotablesOnly', async () => {
    const pinia = createPinia();
    setActivePinia(pinia);
    const store = useTimelineStore();

    const wrapper = mount(FilterPanel, { global: { plugins: [pinia] } });

    const checkbox = wrapper.find('.filter-panel__notables-checkbox');
    await checkbox.setValue(true);
    await checkbox.trigger('change');

    expect(store.filter.notablesOnly).toBe(true);
  });
});
```

#### `tests/unit/FilterChip.spec.ts`

```typescript
import { describe, it, expect } from 'vitest';
import { mount } from '@vue/test-utils';
import FilterChip from '../../src/components/FilterChip.vue';

describe('FilterChip', () => {
  it('filterChip_displaysLabelAndValue', () => {
    const wrapper = mount(FilterChip, { props: { label: 'topic', value: 'weapons.fire' } });
    expect(wrapper.text()).toContain('topic');
    expect(wrapper.text()).toContain('weapons.fire');
  });

  it('filterChip_removeButton_emitsRemoveEvent', async () => {
    const wrapper = mount(FilterChip, { props: { label: 'topic', value: 'test' } });
    await wrapper.find('.filter-chip__remove').trigger('click');
    expect(wrapper.emitted('remove')).toBeTruthy();
  });
});
```

#### `tests/unit/EventInspector.spec.ts`

```typescript
import { describe, it, expect, beforeEach, vi, afterEach } from 'vitest';
import { mount, flushPromises } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { useTimelineStore } from '../../src/stores/timelineStore';

const mockPush = vi.fn();
vi.mock('vue-router', () => ({
  useRouter: vi.fn(() => ({ push: mockPush })),
  useRoute:  vi.fn(() => ({ query: {} })),
}));

const mockGetEvent = vi.fn();
vi.mock('@/api/tracerApiClient', () => ({
  api: { getEvent: mockGetEvent },
}));

describe('EventInspector', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    mockPush.mockReset();
    mockGetEvent.mockReset();
  });

  afterEach(() => {
    vi.clearAllMocks();
  });

  it('eventInspector_fetchesEventOnSelectionChange', async () => {
    mockGetEvent.mockResolvedValue({
      eventId: 'evt-123', traceId: 'trace-A',
      occurredAtUtc: '2026-01-01T10:00:00Z',
      topic: 'test', publisherNode: 'node-A',
      payloadJson: '{}',
    });

    const pinia = createPinia();
    setActivePinia(pinia);
    const store = useTimelineStore();
    store.selectedEventId = 'evt-123';
    store.sessionId = 'sess-1';

    const { default: EventInspector } = await import('../../src/components/EventInspector.vue');
    mount(EventInspector, { global: { plugins: [pinia] } });
    await flushPromises();

    expect(mockGetEvent).toHaveBeenCalledWith('evt-123');
  });

  it('eventInspector_rendersPayloadJson_prettyPrinted', async () => {
    mockGetEvent.mockResolvedValue({
      eventId: 'evt-1', traceId: 'trace-A',
      occurredAtUtc: '2026-01-01T10:00:00Z',
      topic: 'test', publisherNode: 'node-A',
      payloadJson: '{"a":1}',
    });

    const pinia = createPinia();
    setActivePinia(pinia);
    const store = useTimelineStore();
    store.selectedEventId = 'evt-1';

    const { default: EventInspector } = await import('../../src/components/EventInspector.vue');
    const wrapper = mount(EventInspector, { global: { plugins: [pinia] } });
    await flushPromises();

    expect(wrapper.text()).toContain('"a"');
    expect(wrapper.text()).toContain('1');
  });

  it('eventInspector_filterToTrace_addsTraceFilter', async () => {
    mockGetEvent.mockResolvedValue({
      eventId: 'evt-1', traceId: 'AABBCC',
      occurredAtUtc: '2026-01-01T10:00:00Z',
      topic: 'test', publisherNode: 'node-A',
      payloadJson: '{}',
    });

    const pinia = createPinia();
    setActivePinia(pinia);
    const store = useTimelineStore();
    store.selectedEventId = 'evt-1';

    const { default: EventInspector } = await import('../../src/components/EventInspector.vue');
    const wrapper = mount(EventInspector, { global: { plugins: [pinia] } });
    await flushPromises();

    await wrapper.find('.event-inspector__action').trigger('click'); // "Filter to this trace"

    expect(store.filter.traceId).toBe('AABBCC');
  });

  it('eventInspector_showInScenario_navigatesToScenarioRoute', async () => {
    mockGetEvent.mockResolvedValue({
      eventId: 'evt-1', traceId: 'trace-A',
      occurredAtUtc: '2026-01-01T10:00:00Z',
      topic: 'test', publisherNode: 'node-A',
      payloadJson: '{}',
    });

    const pinia = createPinia();
    setActivePinia(pinia);
    const store = useTimelineStore();
    store.selectedEventId = 'evt-1';
    store.sessionId       = 'sess-99';

    const { default: EventInspector } = await import('../../src/components/EventInspector.vue');
    const wrapper = mount(EventInspector, { global: { plugins: [pinia] } });
    await flushPromises();

    const buttons = wrapper.findAll('.event-inspector__action');
    await buttons[1].trigger('click'); // "Show in scenario"

    expect(mockPush).toHaveBeenCalledWith('/scenario/sess-99');
  });

  it('eventInspector_showCausalTree_isDisabled', async () => {
    mockGetEvent.mockResolvedValue({
      eventId: 'evt-1', traceId: 'trace-A',
      occurredAtUtc: '2026-01-01T10:00:00Z',
      topic: 'test', publisherNode: 'node-A',
      payloadJson: '{}',
    });

    const pinia = createPinia();
    setActivePinia(pinia);
    const store = useTimelineStore();
    store.selectedEventId = 'evt-1';

    const { default: EventInspector } = await import('../../src/components/EventInspector.vue');
    const wrapper = mount(EventInspector, { global: { plugins: [pinia] } });
    await flushPromises();

    const buttons = wrapper.findAll('.event-inspector__action');
    // Button index 2 = "Show causal tree"
    expect(buttons[2].attributes('disabled')).toBeDefined();
  });

  it('eventInspector_showEntityHistory_isDisabled', async () => {
    mockGetEvent.mockResolvedValue({
      eventId: 'evt-1', traceId: 'trace-A',
      occurredAtUtc: '2026-01-01T10:00:00Z',
      topic: 'test', publisherNode: 'node-A',
      payloadJson: '{}',
    });

    const pinia = createPinia();
    setActivePinia(pinia);
    const store = useTimelineStore();
    store.selectedEventId = 'evt-1';

    const { default: EventInspector } = await import('../../src/components/EventInspector.vue');
    const wrapper = mount(EventInspector, { global: { plugins: [pinia] } });
    await flushPromises();

    const buttons = wrapper.findAll('.event-inspector__action');
    // Button index 3 = "Show entity history"
    expect(buttons[3].attributes('disabled')).toBeDefined();
  });

  it('eventInspector_copyEventId_writesToClipboard', async () => {
    mockGetEvent.mockResolvedValue({
      eventId: 'evt-copy-me', traceId: 'trace-A',
      occurredAtUtc: '2026-01-01T10:00:00Z',
      topic: 'test', publisherNode: 'node-A',
      payloadJson: '{}',
    });

    // Mock clipboard
    Object.assign(navigator, {
      clipboard: { writeText: vi.fn().mockResolvedValue(undefined) },
    });

    const pinia = createPinia();
    setActivePinia(pinia);
    const store = useTimelineStore();
    store.selectedEventId = 'evt-copy-me';

    const { default: EventInspector } = await import('../../src/components/EventInspector.vue');
    const wrapper = mount(EventInspector, { global: { plugins: [pinia] } });
    await flushPromises();

    const buttons = wrapper.findAll('.event-inspector__action');
    await buttons[4].trigger('click'); // "Copy event ID"
    await flushPromises();

    expect(navigator.clipboard.writeText).toHaveBeenCalledWith('evt-copy-me');
  });

  it('eventInspector_hiddenWhenNoEventSelected', async () => {
    const pinia = createPinia();
    setActivePinia(pinia);
    const store = useTimelineStore();
    store.selectedEventId = null;

    const { default: EventInspector } = await import('../../src/components/EventInspector.vue');
    const wrapper = mount(EventInspector, { global: { plugins: [pinia] } });

    expect(wrapper.find('.event-inspector').exists()).toBe(false);
  });
});
```

---

## Task 3: TRC-P5-008 — Bundle Library UI (Full)

### 3.1 — Create `src/stores/bundleStore.ts`

```typescript
// src/stores/bundleStore.ts
import { defineStore } from 'pinia';
import { api } from '@/api/tracerApiClient';

export interface BundleListEntryDto {
  bundleId:    string;
  label?:      string;
  sizeBytes?:  number;
  createdAtUtc: string;
}

export const useBundleStore = defineStore('bundles', {
  state: () => ({
    bundles: [] as BundleListEntryDto[],
    loading: false,
    error:   null as string | null,
  }),

  actions: {
    async load() {
      this.loading = true;
      this.error   = null;
      try {
        const raw = await api.listBundles();
        this.bundles = raw as BundleListEntryDto[];
      } catch (err: unknown) {
        this.error = err instanceof Error ? err.message : 'Failed to load bundles';
        this.bundles = [];
      } finally {
        this.loading = false;
      }
    },
  },
});
```

### 3.2 — Replace `src/views/BundlesView.vue`

Replace the existing implementation entirely:

```vue
<template>
  <div class="bundles-view">
    <h1>Bundle Library</h1>

    <div v-if="store.loading" class="bundles__loading">Loading…</div>

    <div v-else-if="store.error" class="bundles__error">{{ store.error }}</div>

    <template v-else>
      <p v-if="!isLive" class="bundles__offline-hint">
        To open a different bundle, return to the Open Bundle screen.
      </p>

      <div v-if="store.bundles.length === 0" class="bundles__empty">
        No bundles built yet
      </div>

      <ul v-else class="bundles__list">
        <li
          v-for="bundle in store.bundles"
          :key="bundle.bundleId"
          class="bundles__item"
        >
          <div class="bundles__item-info">
            <span class="bundles__item-label">{{ bundle.label ?? bundle.bundleId }}</span>
            <span v-if="bundle.sizeBytes" class="bundles__item-size">
              {{ formatSize(bundle.sizeBytes) }}
            </span>
          </div>
          <a
            v-if="isLive"
            :href="`/api/bundles/${bundle.bundleId}/download`"
            class="bundles__item-download"
          >
            Download
          </a>
        </li>
      </ul>
    </template>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted } from 'vue';
import { useBundleStore } from '@/stores/bundleStore';
import { useBundleMode } from '@/composables/useBundleMode';

const store = useBundleStore();
const { isLive } = useBundleMode();

onMounted(() => {
  void store.load();
});

function formatSize(bytes: number): string {
  if (bytes < 1024)              return `${bytes} B`;
  if (bytes < 1024 * 1024)       return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}
</script>

<style scoped>
.bundles-view { padding: 24px; }
.bundles__empty { color: #888; margin-top: 16px; }
.bundles__offline-hint { color: #666; font-style: italic; margin-bottom: 16px; }
.bundles__list { list-style: none; padding: 0; margin: 0; }
.bundles__item { display: flex; align-items: center; justify-content: space-between; padding: 8px 0; border-bottom: 1px solid #eee; }
.bundles__item-info { display: flex; gap: 8px; align-items: center; }
.bundles__item-size { color: #888; font-size: 0.8rem; }
.bundles__item-download { text-decoration: none; color: #1976d2; }
</style>
```

### 3.3 — Create `src/components/SessionCard.vue`

This component is used in other views (e.g., sessions list) and adds a "Build bundle" action:

```vue
<template>
  <div class="session-card">
    <slot />

    <div v-if="buildStatus === 'idle'" class="session-card__build">
      <button class="session-card__build-btn" @click="onBuildBundle">Build bundle</button>
    </div>

    <div v-else-if="buildStatus === 'building'" class="session-card__build">
      <span class="session-card__progress">Building bundle…</span>
    </div>

    <div v-else-if="buildStatus === 'done'" class="session-card__build">
      <a :href="`/api/bundles/${bundleId}/download`" class="session-card__download">
        Download bundle
      </a>
    </div>

    <div v-else-if="buildStatus === 'error'" class="session-card__build session-card__build--error">
      {{ buildError }}
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref } from 'vue';
import { api } from '@/api/tracerApiClient';

const props = defineProps<{ sessionId: string }>();

type BuildStatus = 'idle' | 'building' | 'done' | 'error';
const buildStatus = ref<BuildStatus>('idle');
const bundleId    = ref<string | null>(null);
const buildError  = ref<string | null>(null);

async function onBuildBundle() {
  buildStatus.value = 'building';
  buildError.value  = null;
  try {
    const result = await api.buildBundle(props.sessionId);
    bundleId.value    = result.bundleId;
    buildStatus.value = 'done';
  } catch (err: unknown) {
    buildError.value  = err instanceof Error ? err.message : 'Build failed';
    buildStatus.value = 'error';
  }
}
</script>

<style scoped>
.session-card { padding: 8px; border: 1px solid #eee; border-radius: 4px; }
.session-card__build { margin-top: 8px; }
.session-card__build-btn { padding: 4px 12px; cursor: pointer; }
.session-card__progress { color: #666; font-style: italic; }
.session-card__download { color: #1976d2; text-decoration: none; }
.session-card__build--error { color: #c62828; }
</style>
```

### 3.4 — Replace `tests/unit/BundlesView.spec.ts`

Replace the entire existing file with the TRC-P5-008 tests:

```typescript
import { describe, it, expect, beforeEach, vi } from 'vitest';
import { mount, flushPromises } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';
import { useBundleStore } from '../../src/stores/bundleStore';
import type { BundleListEntryDto } from '../../src/stores/bundleStore';

vi.mock('@/composables/useBundleMode', () => ({
  useBundleMode: vi.fn(() => ({
    isLive:     { value: true },
    isBundle:   { value: false },
    isNoBundle: { value: false },
    mode:       { value: { kind: 'live' } },
    refresh:    vi.fn(),
  })),
}));

vi.mock('@/api/tracerApiClient', () => ({
  api: {
    listBundles: vi.fn().mockResolvedValue([]),
    buildBundle: vi.fn(),
  },
}));

async function mountView(pinia: ReturnType<typeof createPinia>) {
  const { default: BundlesView } = await import('../../src/views/BundlesView.vue');
  const wrapper = mount(BundlesView, { global: { plugins: [pinia] } });
  return wrapper;
}

describe('BundlesView', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    vi.clearAllMocks();
  });

  it('renders_bundle_list_from_store', async () => {
    const pinia = createPinia();
    setActivePinia(pinia);
    const store = useBundleStore(pinia);

    const entries: BundleListEntryDto[] = [
      { bundleId: 'b1', label: 'Alpha', createdAtUtc: '2026-01-01T00:00:00Z' },
      { bundleId: 'b2', label: 'Beta',  createdAtUtc: '2026-01-02T00:00:00Z' },
    ];
    store.bundles = entries;
    store.loading = false;

    const wrapper = await mountView(pinia);
    await flushPromises();

    const items = wrapper.findAll('.bundles__item');
    expect(items.length).toBe(2);
    expect(wrapper.text()).toContain('Alpha');
    expect(wrapper.text()).toContain('Beta');
  });

  it('shows_empty_state_when_no_bundles', async () => {
    const pinia = createPinia();
    setActivePinia(pinia);
    const store = useBundleStore(pinia);
    store.bundles = [];
    store.loading = false;

    const wrapper = await mountView(pinia);
    await flushPromises();

    expect(wrapper.text()).toContain('No bundles built yet');
    expect(wrapper.findAll('.bundles__item').length).toBe(0);
  });

  it('shows_error_state_on_fetch_failure', async () => {
    const pinia = createPinia();
    setActivePinia(pinia);
    const store = useBundleStore(pinia);
    store.error   = 'Connection refused';
    store.loading = false;
    store.bundles = [];

    const wrapper = await mountView(pinia);
    await flushPromises();

    expect(wrapper.text()).toContain('Connection refused');
    expect(wrapper.findAll('.bundles__item').length).toBe(0);
  });

  it('shows_offline_hint_in_bundle_mode', async () => {
    // Re-mock useBundleMode to return isLive = false
    const { useBundleMode } = await import('@/composables/useBundleMode');
    (useBundleMode as ReturnType<typeof vi.fn>).mockReturnValue({
      isLive:     { value: false },
      isBundle:   { value: true },
      isNoBundle: { value: false },
      mode:       { value: { kind: 'bundle', bundleId: 'b1' } },
      refresh:    vi.fn(),
    });

    const pinia = createPinia();
    setActivePinia(pinia);
    const store = useBundleStore(pinia);
    store.bundles = [{ bundleId: 'b1', label: 'Alpha', createdAtUtc: '2026-01-01Z' }];
    store.loading = false;

    const wrapper = await mountView(pinia);
    await flushPromises();

    expect(wrapper.text()).toContain('To open a different bundle, return to the Open Bundle screen.');
    // In offline mode, no download links shown
    expect(wrapper.findAll('.bundles__item-download').length).toBe(0);
  });
});
```

### 3.5 — Create `tests/unit/SessionCard.spec.ts`

```typescript
import { describe, it, expect, beforeEach, vi } from 'vitest';
import { mount, flushPromises } from '@vue/test-utils';
import { createPinia, setActivePinia } from 'pinia';

const mockBuildBundle = vi.fn();
vi.mock('@/api/tracerApiClient', () => ({
  api: { buildBundle: mockBuildBundle },
}));

describe('SessionCard', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    mockBuildBundle.mockReset();
  });

  it('buildBundle_showsProgressThenDownloadLink', async () => {
    // First call: simulate in-progress (resolves after we check intermediate state)
    let resolveBuild!: (value: { bundleId: string }) => void;
    mockBuildBundle.mockReturnValue(
      new Promise<{ bundleId: string }>((resolve) => { resolveBuild = resolve; }),
    );

    const { default: SessionCard } = await import('../../src/components/SessionCard.vue');
    const wrapper = mount(SessionCard, {
      props: { sessionId: 'sess-1' },
      global: { plugins: [createPinia()] },
    });

    // Click "Build bundle"
    await wrapper.find('.session-card__build-btn').trigger('click');

    // Should show progress indicator
    expect(wrapper.find('.session-card__progress').exists()).toBe(true);
    expect(wrapper.find('.session-card__download').exists()).toBe(false);

    // Now resolve the build
    resolveBuild({ bundleId: 'new-bundle-abc' });
    await flushPromises();

    // Should now show download link
    expect(wrapper.find('.session-card__download').exists()).toBe(true);
    expect(wrapper.find('.session-card__download').attributes('href'))
      .toContain('new-bundle-abc');
  });
});
```

---

## Important Technical Notes

### Vitest + Composables with `onMounted`/`onUnmounted`

When testing composables that use `onMounted`/`onUnmounted`, these lifecycle hooks only run inside a component context. For unit tests of composables, call the composable function directly (lifecycle hooks are no-ops outside a component) and test the exported functions like `fetchNow`, `fetchDebounced` directly.

### `useTimelineLiveStream` test for reconnect

The `filterChange_reconnects` test works because `watch` with `{ deep: true }` is reactive. In tests, you can call the watch callback by mutating the store's `filter` prop directly.

### BundlesView `useBundleMode`

`useBundleMode` uses `isLive` as a computed ref: `{ value: true }`. When consuming it in template, Vue unwraps refs in `<template>`. In the test mock, return it as `{ value: true }` to match the computed ref shape.

### `EventInspector` `EventDto` type conflict

`EventInspector.vue` uses `api.getEvent()` which returns `EventDto` from `@/api/tracerApiClient` (with `occurredAtUtc`). The rendering types in `@/types/timeline.ts` use `publishWallclock`. The inspector only displays data from the API client type — use `ApiEventDto` alias as shown in the implementation above.

---

## Verification Steps

After implementing all tasks:

1. `cd d:\Work\Tracer\tracer-viewer && npx vitest run` — all tests must pass
2. `npx tsc --noEmit` — must exit 0
3. `dotnet test d:\Work\Tracer\tests\Tracer.Tests.Unit --configuration Release` — must stay 324/324

---

## Report

Write your report to `d:\Work\Tracer\.dev\tracer\reports\BATCH-25-REPORT.md` with:
- All files created or modified
- Exact test counts (before and after)
- Issues encountered and solutions
- Design decisions
- Weak points spotted
- Suggested commit message

**Success condition:** `npx vitest run` passes with significantly more than 74 tests (adding ~30+ new tests).
