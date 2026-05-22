import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';
import { defineComponent, nextTick } from 'vue';
import { mount, flushPromises } from '@vue/test-utils';
import { useEntityHistoryStore } from '../../src/stores/entityHistoryStore';
import type { EntitySummaryDto, EntityEventsDto, EntitySlowStateDto } from '../../src/api/tracerApiClient';

vi.mock('@/api/tracerApiClient', () => ({
  api: {
    getEntitySummary:        vi.fn(),
    getEntityEvents:         vi.fn(),
    getEntitySlowState:      vi.fn(),
    getEntityFastStateTopics: vi.fn(),
  },
}));

import { useEntityHistoryQuery } from '../../src/composables/useEntityHistoryQuery';

function makeSummary(override?: Partial<EntitySummaryDto>): EntitySummaryDto {
  return {
    entityId: 'ent-1',
    firstSeenUtc: '2026-01-01T10:00:00.000Z',
    lastSeenUtc: '2026-01-01T11:00:00.000Z',
    eventCount: 5,
    topics: [],
    ...override,
  };
}

function makeEvents(): EntityEventsDto {
  return { entityId: 'ent-1', events: [], truncated: false };
}

function makeSlowState(): EntitySlowStateDto {
  return { entityId: 'ent-1', byTopic: {} };
}

describe('useEntityHistoryQuery', () => {
  let pinia: ReturnType<typeof createPinia>;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
    vi.clearAllMocks();
  });

  afterEach(() => {
    vi.clearAllMocks();
  });

  function mountWithQuery() {
    return mount(defineComponent({
      setup() {
        useEntityHistoryQuery();
        return {};
      },
      template: '<div/>',
    }), { global: { plugins: [pinia] } });
  }

  it('sequentialThenParallel_SummaryBeforeEvents', async () => {
    const { api } = await import('@/api/tracerApiClient');
    const callOrder: string[] = [];

    (api.getEntitySummary as ReturnType<typeof vi.fn>).mockImplementation(() => {
      callOrder.push('summary');
      return Promise.resolve(makeSummary());
    });
    (api.getEntityEvents as ReturnType<typeof vi.fn>).mockImplementation(() => {
      callOrder.push('events');
      return Promise.resolve(makeEvents());
    });
    (api.getEntitySlowState as ReturnType<typeof vi.fn>).mockImplementation(() => {
      callOrder.push('slowState');
      return Promise.resolve(makeSlowState());
    });
    (api.getEntityFastStateTopics as ReturnType<typeof vi.fn>).mockImplementation(() => {
      callOrder.push('topics');
      return Promise.resolve([]);
    });

    const store = useEntityHistoryStore();
    mountWithQuery();

    store.setEntity('ent-1', 'sess-1');
    await flushPromises();

    // Summary must be called first, before events/slowState/topics
    expect(callOrder[0]).toBe('summary');
    expect(callOrder.slice(1)).toContain('events');
    expect(callOrder.slice(1)).toContain('slowState');
    expect(callOrder.slice(1)).toContain('topics');
  });

  it('parallelFetch_AllThreeInFlight', async () => {
    const { api } = await import('@/api/tracerApiClient');
    let inFlight = 0;
    let maxInFlight = 0;

    const makeDeferred = <T>(value: T) => {
      let resolve!: (v: T) => void;
      const p = new Promise<T>(r => { resolve = r; });
      return { promise: p, resolve: () => resolve(value) };
    };

    const eventsD = makeDeferred(makeEvents());
    const slowD = makeDeferred(makeSlowState());
    const topicsD = makeDeferred([] as string[]);

    (api.getEntitySummary as ReturnType<typeof vi.fn>).mockResolvedValue(makeSummary());
    (api.getEntityEvents as ReturnType<typeof vi.fn>).mockImplementation(() => {
      inFlight++; maxInFlight = Math.max(maxInFlight, inFlight);
      return eventsD.promise.finally(() => { inFlight--; });
    });
    (api.getEntitySlowState as ReturnType<typeof vi.fn>).mockImplementation(() => {
      inFlight++; maxInFlight = Math.max(maxInFlight, inFlight);
      return slowD.promise.finally(() => { inFlight--; });
    });
    (api.getEntityFastStateTopics as ReturnType<typeof vi.fn>).mockImplementation(() => {
      inFlight++; maxInFlight = Math.max(maxInFlight, inFlight);
      return topicsD.promise.finally(() => { inFlight--; });
    });

    const store = useEntityHistoryStore();
    mountWithQuery();

    store.setEntity('ent-1', 'sess-1');
    await nextTick();
    await flushPromises(); // let summary resolve

    // Resolve all parallel calls
    eventsD.resolve();
    slowD.resolve();
    topicsD.resolve();
    await flushPromises();

    expect(maxInFlight).toBe(3);
  });

  it('switchingEntity_CancelsPriorFetch', async () => {
    const { api } = await import('@/api/tracerApiClient');

    let firstAborted = false;
    (api.getEntitySummary as ReturnType<typeof vi.fn>).mockImplementation(
      (_id: string, _sess: string, opts?: { signal?: AbortSignal }) => {
        if (opts?.signal) {
          opts.signal.addEventListener('abort', () => { firstAborted = true; });
        }
        return new Promise(() => { /* never resolves */ });
      },
    );

    const store = useEntityHistoryStore();
    mountWithQuery();

    store.setEntity('ent-1', 'sess-1');
    await nextTick();

    // Switch to a new entity before first resolves
    store.setEntity('ent-2', 'sess-1');
    await nextTick();

    expect(firstAborted).toBe(true);
    // Summary still not populated
    expect(store.summary).toBeNull();
  });

  it('errorHandling_SetsStoreError', async () => {
    const { api } = await import('@/api/tracerApiClient');
    (api.getEntitySummary as ReturnType<typeof vi.fn>).mockRejectedValue(new Error('Network error'));

    const store = useEntityHistoryStore();
    mountWithQuery();

    store.setEntity('ent-1', 'sess-1');
    await flushPromises();

    expect(store.error).toBe('Network error');
    expect(store.loading).toBe(false);
  });

  it('abortError_IsSwallowed', async () => {
    const { api } = await import('@/api/tracerApiClient');
    const abortError = new DOMException('Aborted', 'AbortError');
    (api.getEntitySummary as ReturnType<typeof vi.fn>).mockRejectedValue(abortError);

    const store = useEntityHistoryStore();
    mountWithQuery();

    store.setEntity('ent-1', 'sess-1');
    await flushPromises();

    expect(store.error).toBeNull();
  });

  it('loadingFlag_TrueDuringFetch_FalseAfterSettle', async () => {
    const { api } = await import('@/api/tracerApiClient');
    let resolveSum!: (v: EntitySummaryDto) => void;
    const sumP = new Promise<EntitySummaryDto>(r => { resolveSum = r; });

    (api.getEntitySummary as ReturnType<typeof vi.fn>).mockReturnValue(sumP);
    (api.getEntityEvents as ReturnType<typeof vi.fn>).mockResolvedValue(makeEvents());
    (api.getEntitySlowState as ReturnType<typeof vi.fn>).mockResolvedValue(makeSlowState());
    (api.getEntityFastStateTopics as ReturnType<typeof vi.fn>).mockResolvedValue([]);

    const store = useEntityHistoryStore();
    mountWithQuery();

    store.setEntity('ent-1', 'sess-1');
    await nextTick();

    expect(store.loading).toBe(true);

    resolveSum(makeSummary());
    await flushPromises();

    expect(store.loading).toBe(false);
  });

  it('timeRange_DefaultsToEntityLifespan', async () => {
    const { api } = await import('@/api/tracerApiClient');
    const summary = makeSummary({
      firstSeenUtc: '2026-01-01T10:00:00.000Z',
      lastSeenUtc: '2026-01-01T11:00:00.000Z',
    });
    (api.getEntitySummary as ReturnType<typeof vi.fn>).mockResolvedValue(summary);
    (api.getEntityEvents as ReturnType<typeof vi.fn>).mockResolvedValue(makeEvents());
    (api.getEntitySlowState as ReturnType<typeof vi.fn>).mockResolvedValue(makeSlowState());
    (api.getEntityFastStateTopics as ReturnType<typeof vi.fn>).mockResolvedValue([]);

    const store = useEntityHistoryStore();
    // timeRange starts as from===to (default "not set" state)
    mountWithQuery();

    store.setEntity('ent-1', 'sess-1');
    await flushPromises();

    expect(store.timeRange.from.toISOString()).toBe('2026-01-01T10:00:00.000Z');
    expect(store.timeRange.to.toISOString()).toBe('2026-01-01T11:00:00.000Z');
  });
});
