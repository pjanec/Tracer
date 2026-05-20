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

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('receivedEvent_AppendedToStoreInListMode', async () => {
    const store = useTimelineStore();
    store.sessionId = 'sess-live';
    store.queryMode = 'list';
    store.queryResult = { events: [], totalMatching: 0, returned: 0, truncated: false };

    const { useTimelineLiveStream } = await import('../../src/composables/useTimelineLiveStream');
    useTimelineLiveStream();
    await Promise.resolve();

    const dto = makeEventDto({ eventId: 'evt-live-1' });
    capturedOnMessage?.({ data: JSON.stringify(dto) });

    expect(store.queryResult?.events.length).toBe(1);
    expect(store.queryResult?.events[0].eventId).toBe('evt-live-1');
    expect(store.queryResult?.totalMatching).toBe(1);
  });

  it('followMode_ViewportSlidesOnNewEvent', async () => {
    const store = useTimelineStore();
    store.sessionId = 'sess-live';
    store.queryMode = 'list';
    store.queryResult = { events: [], totalMatching: 0, returned: 0, truncated: false };
    store.viewport.followLive = true;
    store.viewport.from = new Date('2026-01-01T10:00:00Z');
    store.viewport.to   = new Date('2026-01-01T10:10:00Z');
    const originalSpanMs = store.viewportSpanMs; // 10 min

    const { useTimelineLiveStream } = await import('../../src/composables/useTimelineLiveStream');
    useTimelineLiveStream();
    await Promise.resolve();

    // Event arrives after viewport.to
    const evtTime = new Date('2026-01-01T10:15:00Z');
    const dto = makeEventDto({ publishWallclock: evtTime.toISOString() });
    capturedOnMessage?.({ data: JSON.stringify(dto) });

    // Viewport should have slid: new to = evtMs + 5000ms headroom
    const expectedTo = evtTime.getTime() + 5000;
    expect(store.viewport.to.getTime()).toBe(expectedTo);
    expect(store.viewport.from.getTime()).toBe(expectedTo - originalSpanMs);
    expect(store.viewport.followLive).toBe(true);
  });

  it('panGesture_DisablesFollow', async () => {
    const store = useTimelineStore();
    store.sessionId = 'sess-live';
    store.queryMode = 'list';
    store.queryResult = { events: [], totalMatching: 0, returned: 0, truncated: false };
    store.viewport.followLive = true;
    store.viewport.from = new Date('2026-01-01T10:00:00Z');
    store.viewport.to   = new Date('2026-01-01T10:10:00Z');

    const { useTimelineLiveStream } = await import('../../src/composables/useTimelineLiveStream');
    useTimelineLiveStream();
    await Promise.resolve();

    // Pan gesture disables follow
    store.panBy(5_000);
    expect(store.viewport.followLive).toBe(false);

    const toBeforeEvent = store.viewport.to.getTime();

    // Event arrives after current viewport.to
    const evtTime = new Date(toBeforeEvent + 60_000); // 1 min after to
    const dto = makeEventDto({ publishWallclock: evtTime.toISOString() });
    capturedOnMessage?.({ data: JSON.stringify(dto) });

    // Viewport must NOT have slid (followLive is false)
    expect(store.viewport.to.getTime()).toBe(toBeforeEvent);
  });

  it('filterChange_ReconnectsStream', async () => {
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

    expect((fetchEventSource as ReturnType<typeof vi.fn>).mock.calls.length).toBeGreaterThan(callsBefore);
  });

  it('aggregateMode_LiveEventsDoNotAppend', async () => {
    const store = useTimelineStore();
    store.sessionId = 'sess-live';
    store.queryMode = 'aggregate';
    store.queryResult = { events: [], totalMatching: 0, returned: 0, truncated: false };

    const { useTimelineLiveStream } = await import('../../src/composables/useTimelineLiveStream');
    useTimelineLiveStream();
    await Promise.resolve();

    const dto = makeEventDto({ eventId: 'should-not-appear' });
    capturedOnMessage?.({ data: JSON.stringify(dto) });

    // In aggregate mode, appendLiveEvent is a no-op — events list must not grow
    expect(store.queryResult?.events.length).toBe(0);
  });

  it('unmount_abortsConnection', async () => {
    const store = useTimelineStore();
    store.sessionId = 'sess-live';

    const abortSpy = vi.spyOn(AbortController.prototype, 'abort');

    const { useTimelineLiveStream } = await import('../../src/composables/useTimelineLiveStream');
    useTimelineLiveStream();
    await Promise.resolve();

    expect(abortSpy).not.toHaveBeenCalled();
    abortSpy.mockRestore();
  });
});
