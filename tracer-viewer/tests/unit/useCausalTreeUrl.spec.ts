import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';
import { reactive, nextTick } from 'vue';
import { useCausalTreeStore } from '../../src/stores/causalTreeStore';

// Reactive mock route so watchers fire on changes
const mockRoute = reactive({
  name: '' as string | null,
  params: {} as Record<string, string>,
  query:  {} as Record<string, string>,
});

const mockReplace = vi.fn();
const mockPush    = vi.fn();

vi.mock('vue-router', () => ({
  useRoute:  vi.fn(() => mockRoute),
  useRouter: vi.fn(() => ({ replace: mockReplace, push: mockPush })),
}));

import { useCausalTreeUrl } from '../../src/composables/useCausalTreeUrl';

describe('useCausalTreeUrl', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    vi.useFakeTimers();
    mockReplace.mockReset();
    mockPush.mockReset();
    mockRoute.name   = null;
    mockRoute.params = {};
    mockRoute.query  = {};
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.clearAllMocks();
  });

  it('causalByEvent_NoMode_CallsOpenByEvent', () => {
    const store = useCausalTreeStore();
    const openByEventSpy = vi.spyOn(store, 'openByEvent');

    mockRoute.name = 'causal-by-event';
    mockRoute.params = { eventId: 'aabbccddeeff0011' };

    useCausalTreeUrl();

    expect(openByEventSpy).toHaveBeenCalledWith('aabbccddeeff0011', undefined);
  });

  it('causalByEvent_ModeAncestors_CallsOpenAncestorsWithMaxDepth', () => {
    const store = useCausalTreeStore();
    const openAncestorsSpy = vi.spyOn(store, 'openAncestors');

    mockRoute.name = 'causal-by-event';
    mockRoute.params = { eventId: 'aabbccddeeff0011' };
    mockRoute.query = { mode: 'ancestors', maxDepth: '20' };

    useCausalTreeUrl();

    expect(openAncestorsSpy).toHaveBeenCalledWith('aabbccddeeff0011', 20);
  });

  it('causalByEvent_ModeDescendants_CallsOpenDescendantsWithParsedParams', () => {
    const store = useCausalTreeStore();
    const openDescendantsSpy = vi.spyOn(store, 'openDescendants');

    mockRoute.name = 'causal-by-event';
    mockRoute.params = { eventId: 'aabbccddeeff0011' };
    mockRoute.query = { mode: 'descendants', maxDepth: '15', maxNodes: '300' };

    useCausalTreeUrl();

    expect(openDescendantsSpy).toHaveBeenCalledWith('aabbccddeeff0011', 15, 300);
  });

  it('causalByTrace_CallsOpenTrace', () => {
    const store = useCausalTreeStore();
    const openTraceSpy = vi.spyOn(store, 'openTrace');

    mockRoute.name = 'causal-by-trace';
    mockRoute.params = { traceId: '1122334455667788' };

    useCausalTreeUrl();

    expect(openTraceSpy).toHaveBeenCalledWith('1122334455667788', undefined);
  });

  it('causalByTrace_WithSelectParam_SetsSelectedEventId', async () => {
    const store = useCausalTreeStore();

    mockRoute.name = 'causal-by-trace';
    mockRoute.params = { traceId: '1122334455667788' };
    mockRoute.query = { select: 'ffff000011112222' };

    useCausalTreeUrl();
    await nextTick();

    expect(store.selectedEventId).toBe('ffff000011112222');
  });

  it('selectEventId_WritesSelectQueryParamViaRouterReplace', async () => {
    const store = useCausalTreeStore();
    mockRoute.name = null; // no route match yet

    useCausalTreeUrl();

    store.selectedEventId = 'ffff000011112222';
    await nextTick();

    // Before debounce: no call
    expect(mockReplace).not.toHaveBeenCalled();

    // Advance past debounce
    await vi.advanceTimersByTimeAsync(300);

    expect(mockReplace).toHaveBeenCalledTimes(1);
    const callArg = mockReplace.mock.calls[0][0] as { query: Record<string, string> };
    expect(callArg.query.select).toBe('ffff000011112222');
  });
});
