import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';
import { defineComponent, nextTick } from 'vue';
import { mount, flushPromises } from '@vue/test-utils';
import { useCausalTreeStore } from '../../src/stores/causalTreeStore';
import type { TraceTreeDto } from '../../src/types/causalTree';

vi.mock('@/api/tracerApiClient', () => ({
  api: {
    getTraceTree:        vi.fn(),
    getTraceByEvent:     vi.fn(),
    getEventAncestors:   vi.fn(),
    getEventDescendants: vi.fn(),
  },
}));

import { useCausalTreeQuery } from '../../src/composables/useCausalTreeQuery';

function makeMinimalTree(): TraceTreeDto {
  return {
    traceId: 'trace-1',
    nodes: [{ eventId: 'e1', traceId: 'trace-1', publishWallclock: '2026-01-01T10:00:00.000Z', publisherNode: 'n', topic: 't' }],
    edges: [],
    rootEventIds: ['e1'],
    leafEventIds: ['e1'],
    summary: { traceId: 'trace-1', totalEvents: 1, truncated: false, totalSpanMs: 0, participatingNodes: ['n'], rootCount: 1, leafCount: 1 },
  };
}

describe('useCausalTreeQuery', () => {
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
        useCausalTreeQuery();
        return {};
      },
      template: '<div/>',
    }), { global: { plugins: [pinia] } });
  }

  it('requestKindTrace_CallsGetTraceTree', async () => {
    const { api } = await import('@/api/tracerApiClient');
    (api.getTraceTree as ReturnType<typeof vi.fn>).mockResolvedValue(makeMinimalTree());

    const store = useCausalTreeStore();
    mountWithQuery();

    store.request = { kind: 'trace', id: 'abc1234567890def', maxEvents: 1000 };
    await flushPromises();

    expect(api.getTraceTree).toHaveBeenCalledWith('abc1234567890def', 1000, expect.any(Object));
  });

  it('requestKindAncestors_CallsGetEventAncestors', async () => {
    const { api } = await import('@/api/tracerApiClient');
    (api.getEventAncestors as ReturnType<typeof vi.fn>).mockResolvedValue(makeMinimalTree());

    const store = useCausalTreeStore();
    mountWithQuery();

    store.request = { kind: 'ancestors', id: 'def1234567890abc', maxDepth: 50 };
    await flushPromises();

    expect(api.getEventAncestors).toHaveBeenCalledWith('def1234567890abc', 50, expect.any(Object));
  });

  it('secondRequest_AbortsFirst_BeforeFirstResolves', async () => {
    const { api } = await import('@/api/tracerApiClient');

    // Intercept AbortController to spy on abort
    const OriginalAbortController = globalThis.AbortController;
    const controllers: { abort: ReturnType<typeof vi.fn>; signal: AbortSignal }[] = [];
    class MockAbortController {
      abort = vi.fn();
      signal: AbortSignal;
      constructor() {
        this.signal = new OriginalAbortController().signal;
        controllers.push(this as unknown as { abort: ReturnType<typeof vi.fn>; signal: AbortSignal });
      }
    }
    globalThis.AbortController = MockAbortController as unknown as typeof AbortController;

    let firstResolve!: (v: TraceTreeDto) => void;
    const firstPending = new Promise<TraceTreeDto>(r => { firstResolve = r; });
    (api.getTraceTree as ReturnType<typeof vi.fn>)
      .mockReturnValueOnce(firstPending)
      .mockResolvedValueOnce(makeMinimalTree());

    const store = useCausalTreeStore();
    mountWithQuery();

    store.request = { kind: 'trace', id: 'first1234567890aa' };
    await nextTick();
    await nextTick();

    expect(controllers.length).toBeGreaterThanOrEqual(1);

    store.request = { kind: 'trace', id: 'second123456789b' };
    await nextTick();
    await nextTick();

    expect(controllers[0].abort).toHaveBeenCalled();

    // Cleanup
    globalThis.AbortController = OriginalAbortController;
    firstResolve(makeMinimalTree());
    await flushPromises();
  });

  it('abortError_DoesNotSetStoreError', async () => {
    const { api } = await import('@/api/tracerApiClient');
    // Use a plain Error with name='AbortError' — DOMException may not be instanceof Error in jsdom
    const abortError = Object.assign(new Error('The operation was aborted'), { name: 'AbortError' });
    (api.getTraceTree as ReturnType<typeof vi.fn>).mockRejectedValue(abortError);

    const store = useCausalTreeStore();
    mountWithQuery();

    store.request = { kind: 'trace', id: 'abc1234567890def' };
    await flushPromises();

    expect(store.error).toBeNull();
  });

  it('requestKindEvent_CallsGetTraceByEvent', async () => {
    const { api } = await import('@/api/tracerApiClient');
    (api.getTraceByEvent as ReturnType<typeof vi.fn>).mockResolvedValue(makeMinimalTree());

    const store = useCausalTreeStore();
    mountWithQuery();

    store.request = { kind: 'event', id: 'aabbccddeeff0011', maxEvents: 500 };
    await nextTick();
    await flushPromises();

    expect(api.getTraceByEvent).toHaveBeenCalledOnce();
    expect(api.getTraceByEvent).toHaveBeenCalledWith(
      'aabbccddeeff0011', 500, expect.objectContaining({ signal: expect.any(AbortSignal) })
    );
    expect(api.getTraceTree).not.toHaveBeenCalled();
  });

  it('requestKindDescendants_CallsGetEventDescendants', async () => {
    const { api } = await import('@/api/tracerApiClient');
    (api.getEventDescendants as ReturnType<typeof vi.fn>).mockResolvedValue(makeMinimalTree());

    const store = useCausalTreeStore();
    mountWithQuery();

    store.request = { kind: 'descendants', id: 'aabbccddeeff0011', maxDepth: 20, maxNodes: 400 };
    await nextTick();
    await flushPromises();

    expect(api.getEventDescendants).toHaveBeenCalledOnce();
    expect(api.getEventDescendants).toHaveBeenCalledWith(
      'aabbccddeeff0011', 20, 400, expect.objectContaining({ signal: expect.any(AbortSignal) })
    );
    expect(api.getTraceTree).not.toHaveBeenCalled();
  });
});
