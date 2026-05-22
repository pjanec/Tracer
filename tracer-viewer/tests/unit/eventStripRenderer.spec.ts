import { describe, it, expect, vi } from 'vitest';
import { renderEventStrip } from '../../src/rendering/eventStripRenderer';
import type { EventStripRenderInput } from '../../src/rendering/eventStripRenderer';
import type { EntityEventDto } from '../../src/api/tracerApiClient';

function makeCanvasMock() {
  return {
    clearRect: vi.fn(),
    beginPath: vi.fn(),
    arc: vi.fn(),
    fill: vi.fn(),
    stroke: vi.fn(),
    fillStyle: '' as string,
    strokeStyle: '' as string,
    lineWidth: 0,
  } as unknown as CanvasRenderingContext2D;
}

function makeEvent(overrides: Partial<EntityEventDto> = {}): EntityEventDto {
  return {
    eventId: 'evt-1',
    traceId: '0000000000000000',
    occurredAtUtc: '2026-01-01T10:00:00.250Z',
    topic: 'entity.pos',
    publisherNode: 'node-A',
    ...overrides,
  };
}

const FROM_MS = new Date('2026-01-01T10:00:00.000Z').getTime();
const TO_MS   = new Date('2026-01-01T10:00:01.000Z').getTime();

describe('eventStripRenderer', () => {
  it('markerAtCorrectXPosition', () => {
    // timeRange 0–1000ms, canvas 1000px wide, event at 250ms → x ≈ 250
    const ctx = makeCanvasMock();
    const event = makeEvent({ occurredAtUtc: '2026-01-01T10:00:00.250Z' });

    const input: EventStripRenderInput = {
      width: 1000,
      height: 40,
      fromMs: FROM_MS,
      toMs: TO_MS,
      events: [event],
      selectedEventId: null,
    };

    const entries = renderEventStrip(ctx, input);

    // arc called once for the marker
    const arcCalls = (ctx.arc as ReturnType<typeof vi.fn>).mock.calls;
    expect(arcCalls.length).toBeGreaterThan(0);
    // First arc x-coordinate should be ≈ 250
    const [x] = arcCalls[0] as [number, number, number, number, number];
    expect(x).toBeCloseTo(250, 0);

    // Hit entry has matching x
    expect(entries.length).toBe(1);
    expect(entries[0].x).toBeCloseTo(250, 0);
    expect(entries[0].eventId).toBe('evt-1');
  });

  it('selectedEventHasRing', () => {
    const ctx = makeCanvasMock();
    const event1 = makeEvent({ eventId: 'evt-1', occurredAtUtc: '2026-01-01T10:00:00.250Z' });
    const event2 = makeEvent({ eventId: 'evt-2', occurredAtUtc: '2026-01-01T10:00:00.750Z' });

    renderEventStrip(ctx, {
      width: 1000,
      height: 40,
      fromMs: FROM_MS,
      toMs: TO_MS,
      events: [event1, event2],
      selectedEventId: 'evt-1',
    });

    // stroke() called once for the ring around the selected event
    expect((ctx.stroke as ReturnType<typeof vi.fn>).mock.calls.length).toBeGreaterThan(0);
  });

  it('zeroEventsDoesNotThrow_ClearRectCalled', () => {
    const ctx = makeCanvasMock();

    expect(() => {
      renderEventStrip(ctx, {
        width: 1000,
        height: 40,
        fromMs: FROM_MS,
        toMs: TO_MS,
        events: [],
        selectedEventId: null,
      });
    }).not.toThrow();

    expect((ctx.clearRect as ReturnType<typeof vi.fn>).mock.calls.length).toBe(1);
  });
});
