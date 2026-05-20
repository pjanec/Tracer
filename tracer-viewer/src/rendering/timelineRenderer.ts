// tracer-viewer/src/rendering/timelineRenderer.ts
// Pure draw logic — no Vue reactivity, no DOM. Fully unit-testable.

import type { EventDto, EventAggregateDto } from '@/types/timeline';
import { msToPixel, swimlaneY } from './timelineLayout';
import { getNodeColor, SEVERITY_COLORS } from './colorScheme';
import { HitIndex } from './timelineHitTest';
import type { MarkerHitEntry, BucketHitEntry } from './timelineHitTest';

// --- Input / Output types ---

export interface TimelineRenderInput {
  /** Canvas pixel dimensions. */
  width: number;
  height: number;

  /** Visible time range (milliseconds since epoch). */
  fromMs: number;
  toMs: number;

  /** Ordered list of node names (determines swimlane assignment). */
  nodes: string[];

  /** Height of a single swimlane in pixels. */
  swimlaneHeightPx: number;

  /** Marker radius in list mode. */
  markerRadiusPx: number;

  // === List-mode fields ===
  /** Raw events to render (list mode). When non-null the renderer is in list mode. */
  events?: EventDto[] | null;

  // === Aggregate-mode fields ===
  /** Aggregated data (aggregate mode). When non-null the renderer is in aggregate mode. */
  aggregate?: EventAggregateDto | null;

  /** Group-by key used when rendering aggregate bars. */
  groupBy?: 'node' | 'topic' | 'severity' | 'none';
}

export interface TimelineRenderOutput {
  /** Spatial index built during this render pass. */
  hitIndex: HitIndex;
}

// Notable marker dimensions
const NOTABLE_W = 10;
const NOTABLE_H = 10;

// -------------------------------------------------------------------

/**
 * Render the timeline onto `ctx`.  Pure function — no side effects beyond drawing.
 */
export function render(
  ctx: CanvasRenderingContext2D,
  input: TimelineRenderInput,
): TimelineRenderOutput {
  const { width, height, fromMs, toMs, nodes, swimlaneHeightPx, markerRadiusPx } = input;

  const hitIndex = new HitIndex(width, height);

  ctx.clearRect(0, 0, width, height);

  if (input.events != null) {
    _renderListMode(ctx, input, fromMs, toMs, nodes, swimlaneHeightPx, markerRadiusPx, width, hitIndex);
  } else if (input.aggregate != null) {
    _renderAggregateMode(ctx, input, fromMs, toMs, nodes, swimlaneHeightPx, width, hitIndex);
  }

  return { hitIndex };
}

// -------------------------------------------------------------------
// List mode
// -------------------------------------------------------------------

function _renderListMode(
  ctx: CanvasRenderingContext2D,
  input: TimelineRenderInput,
  fromMs: number,
  toMs: number,
  nodes: string[],
  swimlaneHeightPx: number,
  markerRadiusPx: number,
  width: number,
  hitIndex: HitIndex,
): void {
  const events = input.events!;

  for (const evt of events) {
    const evtMs = new Date(evt.publishWallclock).getTime();

    // Skip events outside the visible range
    if (evtMs < fromMs || evtMs > toMs) continue;

    const nodeIndex = nodes.indexOf(evt.publisherNode);
    const cy = swimlaneY(nodeIndex >= 0 ? nodeIndex : 0, swimlaneHeightPx);
    const cx = msToPixel(evtMs, width, fromMs, toMs);

    const isNotable = evt.notableLabel != null && evt.notableLabel !== '';

    if (isNotable) {
      // Notable: filled rectangle
      const hx = cx - NOTABLE_W / 2;
      const hy = cy - NOTABLE_H / 2;
      ctx.fillStyle = _evtColor(evt);
      ctx.fillRect(hx, hy, NOTABLE_W, NOTABLE_H);

      const entry: MarkerHitEntry = {
        x: cx,
        y: cy,
        w: NOTABLE_W,
        h: NOTABLE_H,
        eventId: evt.eventId,
      };
      hitIndex.add(entry);
    } else {
      // Standard event: circle
      const r = markerRadiusPx;
      ctx.beginPath();
      ctx.fillStyle = _evtColor(evt);
      ctx.arc(cx, cy, r, 0, 2 * Math.PI);
      ctx.fill();

      const entry: MarkerHitEntry = {
        x: cx,
        y: cy,
        w: r * 2,
        h: r * 2,
        eventId: evt.eventId,
      };
      hitIndex.add(entry);
    }
  }
}

// -------------------------------------------------------------------
// Aggregate mode
// -------------------------------------------------------------------

function _renderAggregateMode(
  ctx: CanvasRenderingContext2D,
  input: TimelineRenderInput,
  fromMs: number,
  toMs: number,
  nodes: string[],
  swimlaneHeightPx: number,
  width: number,
  hitIndex: HitIndex,
): void {
  const { aggregate, groupBy } = input;
  const buckets = aggregate!.buckets;

  for (const bucket of buckets) {
    const bucketMs = new Date(bucket.bucketStartUtc).getTime();
    if (bucketMs < fromMs || bucketMs > toMs) continue;

    const bx = msToPixel(bucketMs, width, fromMs, toMs);

    // Determine bar width from the next bucket or a fixed minimum
    const barW = Math.max(2, width / Math.max(buckets.length, 1));

    for (const group of bucket.groups) {
      let nodeIndex = 0;
      if (groupBy === 'node' && group.groupKey != null) {
        const idx = nodes.indexOf(group.groupKey);
        nodeIndex = idx >= 0 ? idx : 0;
      }
      const cy = swimlaneY(nodeIndex, swimlaneHeightPx);
      const barH = Math.min(swimlaneHeightPx * 0.8, group.count * 2);
      const by = cy - barH / 2;

      ctx.fillStyle = groupBy === 'node' && group.groupKey != null
        ? getNodeColor(group.groupKey)
        : '#5b9dff';
      ctx.fillRect(bx, by, barW, barH);

      const entry: BucketHitEntry = {
        x: bx,
        y: by,
        w: barW,
        h: barH,
        bucketStartUtc: bucket.bucketStartUtc,
        nodeId: group.groupKey ?? '',
        count: group.count,
      };
      hitIndex.addBucket(entry);
    }
  }
}

// -------------------------------------------------------------------
// Helpers
// -------------------------------------------------------------------

function _evtColor(evt: EventDto): string {
  const sev = (evt.severity ?? '').toLowerCase();
  if (sev === 'error') return SEVERITY_COLORS.error;
  if (sev === 'warning') return SEVERITY_COLORS.warning;
  return getNodeColor(evt.publisherNode);
}
