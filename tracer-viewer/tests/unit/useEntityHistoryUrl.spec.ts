import { describe, it, expect, beforeEach, vi, afterEach } from 'vitest';
import { createPinia, setActivePinia } from 'pinia';
import { useEntityHistoryStore } from '../../src/stores/entityHistoryStore';

const mockReplace = vi.fn();
const mockPush = vi.fn();
const mockRouteQuery: Record<string, unknown> = {};
const mockRouteParams: Record<string, unknown> = {};

vi.mock('vue-router', () => ({
  useRoute: vi.fn(() => ({ query: mockRouteQuery, params: mockRouteParams })),
  useRouter: vi.fn(() => ({ replace: mockReplace, push: mockPush })),
}));

describe('useEntityHistoryUrl', () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    vi.useFakeTimers();
    mockReplace.mockReset();
    mockPush.mockReset();
    for (const key of Object.keys(mockRouteQuery)) delete mockRouteQuery[key];
    for (const key of Object.keys(mockRouteParams)) delete mockRouteParams[key];
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.clearAllMocks();
  });

  it('urlToStore_EntityIdAndSessionId', async () => {
    mockRouteParams['entityId'] = 'ent-42';
    mockRouteQuery['session'] = 'sess-abc';

    const store = useEntityHistoryStore();
    const { useEntityHistoryUrl } = await import('../../src/composables/useEntityHistoryUrl');
    useEntityHistoryUrl();

    expect(store.entityId).toBe('ent-42');
    expect(store.sessionId).toBe('sess-abc');
  });

  it('urlToStore_FromToAsDates', async () => {
    mockRouteParams['entityId'] = 'ent-1';
    mockRouteQuery['session'] = 'sess-1';
    mockRouteQuery['from'] = '2026-01-01T10:00:00.000Z';
    mockRouteQuery['to'] = '2026-01-01T11:00:00.000Z';

    const store = useEntityHistoryStore();
    const { useEntityHistoryUrl } = await import('../../src/composables/useEntityHistoryUrl');
    useEntityHistoryUrl();

    expect(store.timeRange.from).toBeInstanceOf(Date);
    expect(store.timeRange.from.toISOString()).toBe('2026-01-01T10:00:00.000Z');
    expect(store.timeRange.to.toISOString()).toBe('2026-01-01T11:00:00.000Z');
  });

  it('urlToStore_SelectSetsSelectedEventId', async () => {
    mockRouteParams['entityId'] = 'ent-1';
    mockRouteQuery['session'] = 'sess-1';
    mockRouteQuery['select'] = 'evt-999';

    const store = useEntityHistoryStore();
    const { useEntityHistoryUrl } = await import('../../src/composables/useEntityHistoryUrl');
    useEntityHistoryUrl();

    expect(store.selectedEventId).toBe('evt-999');
  });

  it('urlToStore_MissingFromTo_LeavesTimeRangeUnchanged', async () => {
    mockRouteParams['entityId'] = 'ent-1';
    mockRouteQuery['session'] = 'sess-1';
    // No from/to in URL

    const store = useEntityHistoryStore();
    const initialFrom = store.timeRange.from.getTime();
    const initialTo = store.timeRange.to.getTime();

    const { useEntityHistoryUrl } = await import('../../src/composables/useEntityHistoryUrl');
    useEntityHistoryUrl();

    expect(store.timeRange.from.getTime()).toBe(initialFrom);
    expect(store.timeRange.to.getTime()).toBe(initialTo);
  });

  it('storeToUrl_TimeRangeChange_TriggersDebounced', async () => {
    mockRouteParams['entityId'] = 'ent-1';
    mockRouteQuery['session'] = 'sess-1';

    const store = useEntityHistoryStore();
    store.entityId = 'ent-1';
    store.sessionId = 'sess-1';
    store.timeRange.from = new Date('2026-01-01T10:00:00Z');
    store.timeRange.to = new Date('2026-01-01T11:00:00Z');

    const { useEntityHistoryUrl } = await import('../../src/composables/useEntityHistoryUrl');
    useEntityHistoryUrl();

    expect(mockReplace).not.toHaveBeenCalled();

    // Trigger store change
    store.timeRange = { from: new Date('2026-01-02T10:00:00Z'), to: new Date('2026-01-02T11:00:00Z') };
    await vi.advanceTimersByTimeAsync(300);

    expect(mockReplace).toHaveBeenCalled();
    const callArg = mockReplace.mock.calls[0][0] as { query: Record<string, string> };
    expect(callArg.query['from']).toBeTruthy();
    expect(callArg.query['to']).toBeTruthy();
  });

  it('storeToUrl_SelectedEventId_InUrl', async () => {
    mockRouteParams['entityId'] = 'ent-1';
    mockRouteQuery['session'] = 'sess-1';

    const store = useEntityHistoryStore();
    store.entityId = 'ent-1';
    store.sessionId = 'sess-1';
    store.timeRange.from = new Date('2026-01-01T10:00:00Z');
    store.timeRange.to = new Date('2026-01-01T11:00:00Z');

    const { useEntityHistoryUrl } = await import('../../src/composables/useEntityHistoryUrl');
    useEntityHistoryUrl();

    store.selectedEventId = 'evt-777';
    await vi.advanceTimersByTimeAsync(300);

    expect(mockReplace).toHaveBeenCalled();
    const callArg = mockReplace.mock.calls[0][0] as { query: Record<string, string> };
    expect(callArg.query['select']).toBe('evt-777');
  });

  it('roundTrip_AllParams_StoreReflectsThem', async () => {
    vi.resetModules();
    const { createPinia: cp, setActivePinia: sap } = await import('pinia');
    const freshPinia = cp();
    sap(freshPinia);

    for (const key of Object.keys(mockRouteQuery)) delete mockRouteQuery[key];
    for (const key of Object.keys(mockRouteParams)) delete mockRouteParams[key];
    mockRouteParams['entityId'] = 'ent-99';
    mockRouteQuery['session'] = 'sess-99';
    mockRouteQuery['from'] = '2026-06-01T08:00:00.000Z';
    mockRouteQuery['to'] = '2026-06-01T09:00:00.000Z';
    mockRouteQuery['select'] = 'evt-roundtrip';

    const { useEntityHistoryStore: freshStore } = await import('../../src/stores/entityHistoryStore');
    const { useEntityHistoryUrl: freshUrl } = await import('../../src/composables/useEntityHistoryUrl');

    const store = freshStore();
    freshUrl();

    expect(store.entityId).toBe('ent-99');
    expect(store.sessionId).toBe('sess-99');
    expect(store.timeRange.from.toISOString()).toBe('2026-06-01T08:00:00.000Z');
    expect(store.timeRange.to.toISOString()).toBe('2026-06-01T09:00:00.000Z');
    expect(store.selectedEventId).toBe('evt-roundtrip');
  });
});
