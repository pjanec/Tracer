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
