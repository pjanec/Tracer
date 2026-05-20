import { describe, it, expect, beforeEach, vi, afterEach } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';
import { useTimelineStore } from '../../src/stores/timelineStore';

const mockReplace = vi.fn();
const mockPush    = vi.fn();
const mockRouteQuery: Record<string, unknown> = {};

vi.mock('vue-router', () => ({
  useRoute:  vi.fn(() => ({ query: mockRouteQuery })),
  useRouter: vi.fn(() => ({ replace: mockReplace, push: mockPush })),
}));

describe('useTimelineUrl', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    vi.useFakeTimers();
    mockReplace.mockReset();
    mockPush.mockReset();
    for (const key of Object.keys(mockRouteQuery)) {
      delete mockRouteQuery[key];
    }
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.clearAllMocks();
  });

  it('urlParams_AppliedToStoreOnMount', async () => {
    mockRouteQuery['from']  = '2026-01-01T14:00:00.000Z';
    mockRouteQuery['to']    = '2026-01-01T14:30:00.000Z';
    mockRouteQuery['topic'] = 'weapons.fire';

    const store = useTimelineStore();
    const { useTimelineUrl } = await import('../../src/composables/useTimelineUrl');
    useTimelineUrl();

    expect(store.viewport.from.toISOString()).toBe('2026-01-01T14:00:00.000Z');
    expect(store.viewport.to.toISOString()).toBe('2026-01-01T14:30:00.000Z');
    expect(store.filter.topics).toContain('weapons.fire');
  });

  it('storeChange_UpdatesUrlDebounced', async () => {
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

  it('multipleFilterValues_EncodedAsRepeatedParams', async () => {
    // Part A: Store → URL encoding
    {
      const store = useTimelineStore();
      store.viewport.from = new Date('2026-01-01T10:00:00Z');
      store.viewport.to   = new Date('2026-01-01T11:00:00Z');
      store.filter = { topics: ['a', 'b'] };

      const { useTimelineUrl } = await import('../../src/composables/useTimelineUrl');
      useTimelineUrl();
      await vi.advanceTimersByTimeAsync(300);

      const callArg = mockReplace.mock.calls[0][0] as { query: Record<string, unknown> };
      expect(callArg.query['topic']).toEqual(['a', 'b']);
    }

    // Part B: URL → Store decoding (fresh pinia + fresh module imports)
    {
      vi.resetModules();
      const { createPinia: freshCreatePinia, setActivePinia: freshSetActivePinia } = await import('pinia');
      const freshPinia = freshCreatePinia();
      freshSetActivePinia(freshPinia);

      mockReplace.mockReset();
      for (const key of Object.keys(mockRouteQuery)) delete mockRouteQuery[key];
      mockRouteQuery['topic'] = ['a', 'b'];
      mockRouteQuery['from']  = '2026-01-01T10:00:00.000Z';
      mockRouteQuery['to']    = '2026-01-01T11:00:00.000Z';

      const { useTimelineStore: freshStore } = await import('../../src/stores/timelineStore');
      const { useTimelineUrl: freshUrl }     = await import('../../src/composables/useTimelineUrl');

      const store2 = freshStore();
      freshUrl();
      expect(store2.filter.topics).toEqual(['a', 'b']);
    }
  });

  it('selectedEvent_RoundTripsViaUrl', async () => {
    // Part A: Store → URL (selectedEventId encoded as ?select=)
    {
      const store = useTimelineStore();
      store.viewport.from = new Date('2026-01-01T10:00:00Z');
      store.viewport.to   = new Date('2026-01-01T11:00:00Z');
      store.selectedEventId = 'AABBCCDD11223344';

      const { useTimelineUrl } = await import('../../src/composables/useTimelineUrl');
      useTimelineUrl();
      await vi.advanceTimersByTimeAsync(300);

      const callArg = mockReplace.mock.calls[0][0] as { query: Record<string, string> };
      expect(callArg.query['select']).toBe('AABBCCDD11223344');
    }

    // Part B: URL → Store (restoring selectedEventId from ?select=)
    {
      vi.resetModules();
      const { createPinia: freshCreatePinia, setActivePinia: freshSetActivePinia } = await import('pinia');
      const freshPinia = freshCreatePinia();
      freshSetActivePinia(freshPinia);

      mockReplace.mockReset();
      for (const key of Object.keys(mockRouteQuery)) delete mockRouteQuery[key];
      mockRouteQuery['select'] = 'AABBCCDD11223344';
      mockRouteQuery['from']   = '2026-01-01T10:00:00.000Z';
      mockRouteQuery['to']     = '2026-01-01T11:00:00.000Z';

      const { useTimelineStore: freshStore } = await import('../../src/stores/timelineStore');
      const { useTimelineUrl: freshUrl }     = await import('../../src/composables/useTimelineUrl');

      const store2 = freshStore();
      freshUrl();
      expect(store2.selectedEventId).toBe('AABBCCDD11223344');
    }
  });

  it('panGesture_UsesReplaceNotPush', async () => {
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

    // router.replace should have been called, push must never be called
    expect(mockReplace).toHaveBeenCalled();
    expect(mockPush).not.toHaveBeenCalled();
  });
});
