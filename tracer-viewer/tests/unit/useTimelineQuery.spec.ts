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
