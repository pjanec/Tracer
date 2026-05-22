// tracer-viewer/src/rendering/eventStripRenderer.ts
// Pure canvas rendering for the entity event strip — no Vue dependency.

import type { EntityEventDto } from '@/api/tracerApiClient';
import { buildNodeColorMap } from './colorScheme';

export interface EventStripRenderInput {
  width: number;
  height: number;
  fromMs: number;
  toMs: number;
  events: EntityEventDto[];
  selectedEventId: string | null;
  /** Marker radius in pixels. Defaults to 4. */
  markerRadiusPx?: number;
}

export interface EventStripHitEntry {
  eventId: string;
  x: number;
}

/**
 * Renders event markers onto the canvas and returns hit-test entries for click handling.
 * Pure function — no side effects beyond drawing.
 */
export function renderEventStrip(
  ctx: CanvasRenderingContext2D,
  input: EventStripRenderInput,
): EventStripHitEntry[] {
  const { width, height, fromMs, toMs, events, selectedEventId } = input;
  const r = input.markerRadiusPx ?? 4;

  ctx.clearRect(0, 0, width, height);

  if (events.length === 0) return [];

  const uniqueNodes = [...new Set(events.map(e => e.publisherNode))];
  const nodeColorMap = buildNodeColorMap(uniqueNodes);
  const hitEntries: EventStripHitEntry[] = [];
  const y = height / 2;
  const range = toMs - fromMs;

  for (const event of events) {
    const t = new Date(event.occurredAtUtc).getTime();
    const x = range === 0 ? 0 : ((t - fromMs) / range) * width;
    if (x < 0 || x > width) continue;

    ctx.beginPath();
    ctx.arc(x, y, r, 0, 2 * Math.PI);
    ctx.fillStyle = nodeColorMap.get(event.publisherNode) ?? '#888';
    ctx.fill();

    if (event.eventId === selectedEventId) {
      ctx.beginPath();
      ctx.arc(x, y, r + 3, 0, 2 * Math.PI);
      ctx.strokeStyle = '#ffffff';
      ctx.lineWidth = 2;
      ctx.stroke();
    }

    hitEntries.push({ eventId: event.eventId, x });
  }

  return hitEntries;
}
