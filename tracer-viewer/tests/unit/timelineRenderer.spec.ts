import { describe, it, expect, vi } from 'vitest';
import { render } from '../../src/rendering/timelineRenderer';
import type { TimelineRenderInput } from '../../src/rendering/timelineRenderer';
import type { EventDto } from '../../src/types/timeline';

function makeCanvasMock() {
  const ctx = {
    arc: vi.fn(),
    fill: vi.fn(),
    fillRect: vi.fn(),
    beginPath: vi.fn(),
    clearRect: vi.fn(),
    fillStyle: '',
    setTransform: vi.fn(),
  } as unknown as CanvasRenderingContext2D;
  return ctx;
}

function makeEvent(overrides: Partial<EventDto> = {}): EventDto {
  return {
    eventId: 'evt-' + Math.random().toString(36).slice(2),
    traceId: 'trace-1',
    publishWallclock: '2026-01-01T10:00:00.000Z',
    publisherNode: 'node-A',
    topic: 'test.topic',
    ...overrides,
  };
}

const BASE_INPUT: TimelineRenderInput = {
  width: 1000,
  height: 400,
  fromMs: new Date('2026-01-01T10:00:00Z').getTime(),
  toMs:   new Date('2026-01-01T11:00:00Z').getTime(),
  nodes: ['node-A', 'node-B'],
  swimlaneHeightPx: 80,
  markerRadiusPx: 4,
};

describe('timelineRenderer', () => {
  it('render_ListMode_DrawsOneArcPerNonNotableEvent', () => {
    const ctx = makeCanvasMock();
    const events: EventDto[] = [
      makeEvent({ publishWallclock: '2026-01-01T10:10:00.000Z', eventId: 'e1' }),
      makeEvent({ publishWallclock: '2026-01-01T10:20:00.000Z', eventId: 'e2' }),
      makeEvent({ publishWallclock: '2026-01-01T10:30:00.000Z', eventId: 'e3' }),
      makeEvent({ publishWallclock: '2026-01-01T10:40:00.000Z', eventId: 'e4' }),
      makeEvent({ publishWallclock: '2026-01-01T10:50:00.000Z', eventId: 'e5' }),
    ];

    render(ctx, { ...BASE_INPUT, events });

    expect((ctx.arc as ReturnType<typeof vi.fn>).mock.calls.length).toBe(5);
  });

  it('render_ListMode_DrawsOneRectPerNotableEvent', () => {
    const ctx = makeCanvasMock();
    const events: EventDto[] = [
      makeEvent({
        publishWallclock: '2026-01-01T10:10:00.000Z',
        eventId: 'notable-1',
        notableLabel: 'Game Started',
      }),
    ];

    render(ctx, { ...BASE_INPUT, events });

    // Notable events use fillRect, not arc
    expect((ctx.fillRect as ReturnType<typeof vi.fn>).mock.calls.length).toBeGreaterThan(0);
    expect((ctx.arc as ReturnType<typeof vi.fn>).mock.calls.length).toBe(0);
  });

  it('render_AggregateMode_DrawsFillRectPerBucketGroup', () => {
    const ctx = makeCanvasMock();

    const aggregate = {
      bucketDuration: '1s',
      buckets: [
        {
          bucketStartUtc: '2026-01-01T10:01:00.000Z',
          groups: [
            { groupKey: 'node-A', count: 5 },
            { groupKey: 'node-B', count: 3 },
          ],
          total: 8,
        },
        {
          bucketStartUtc: '2026-01-01T10:02:00.000Z',
          groups: [
            { groupKey: 'node-A', count: 2 },
            { groupKey: 'node-B', count: 4 },
          ],
          total: 6,
        },
        {
          bucketStartUtc: '2026-01-01T10:03:00.000Z',
          groups: [
            { groupKey: 'node-A', count: 1 },
            { groupKey: 'node-B', count: 7 },
          ],
          total: 8,
        },
      ],
    };

    render(ctx, { ...BASE_INPUT, aggregate, groupBy: 'node' });

    // 3 buckets × 2 nodes = 6 fillRect calls
    expect((ctx.fillRect as ReturnType<typeof vi.fn>).mock.calls.length).toBeGreaterThanOrEqual(6);
  });

  it('render_EventOutsideViewportBounds_SkippedDefensively', () => {
    const ctx = makeCanvasMock();
    const fromMs = new Date('2026-01-01T10:00:00Z').getTime();
    const toMs   = new Date('2026-01-01T11:00:00Z').getTime();

    const events: EventDto[] = [
      // Inside
      makeEvent({ publishWallclock: '2026-01-01T10:30:00.000Z', eventId: 'inside' }),
      // Before range
      makeEvent({ publishWallclock: '2026-01-01T09:59:59.000Z', eventId: 'before' }),
      // After range
      makeEvent({ publishWallclock: '2026-01-01T11:00:01.000Z', eventId: 'after' }),
    ];

    render(ctx, { ...BASE_INPUT, fromMs, toMs, events });

    // Only 1 arc call for the in-range event
    expect((ctx.arc as ReturnType<typeof vi.fn>).mock.calls.length).toBe(1);
  });

  it('render_EmptyEventList_NoArcOrRectCallsMade', () => {
    const ctx = makeCanvasMock();

    expect(() => {
      render(ctx, { ...BASE_INPUT, events: [] });
    }).not.toThrow();

    expect((ctx.arc as ReturnType<typeof vi.fn>).mock.calls.length).toBe(0);
    expect((ctx.fillRect as ReturnType<typeof vi.fn>).mock.calls.length).toBe(0);
  });

  it('render_ReturnsHitIndexWithEntryForEachDrawnMarker', () => {
    const ctx = makeCanvasMock();
    const fromMs = new Date('2026-01-01T10:00:00Z').getTime();
    const toMs   = new Date('2026-01-01T11:00:00Z').getTime();

    const times = [
      '2026-01-01T10:10:00.000Z',
      '2026-01-01T10:20:00.000Z',
      '2026-01-01T10:30:00.000Z',
    ];
    const events = times.map((t, i) =>
      makeEvent({ publishWallclock: t, eventId: `e${i}`, publisherNode: 'node-A' }),
    );

    const { hitIndex } = render(ctx, { ...BASE_INPUT, fromMs, toMs, events });

    const spanMs = toMs - fromMs;
    const width  = BASE_INPUT.width;

    for (const evt of events) {
      const evtMs = new Date(evt.publishWallclock).getTime();
      const cx = ((evtMs - fromMs) / spanMs) * width;
      const cy = BASE_INPUT.swimlaneHeightPx / 2; // node-A at index 0
      const hit = hitIndex.findMarkerAt(cx, cy);
      expect(hit).not.toBeNull();
    }
  });
});
