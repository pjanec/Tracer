import { describe, it, expect } from 'vitest';
import { HitIndex } from '../../src/rendering/timelineHitTest';
import type { MarkerHitEntry, BucketHitEntry } from '../../src/rendering/timelineHitTest';

const W = 1000;
const H = 400;

describe('timelineHitTest', () => {
  it('findMarkerAt_ExactPosition_ReturnsMarker', () => {
    const index = new HitIndex(W, H);
    const entry: MarkerHitEntry = { x: 200, y: 100, w: 8, h: 8, eventId: 'e1' };
    index.add(entry);

    const result = index.findMarkerAt(200, 100);
    expect(result).not.toBeNull();
    expect(result?.eventId).toBe('e1');
  });

  it('findMarkerAt_WithinMarkerRadius_ReturnsMarker', () => {
    const index = new HitIndex(W, H);
    const entry: MarkerHitEntry = { x: 200, y: 100, w: 8, h: 8, eventId: 'e2' };
    index.add(entry);

    // Query at (203, 103) — within ±4px radius
    const result = index.findMarkerAt(203, 103);
    expect(result).not.toBeNull();
    expect(result?.eventId).toBe('e2');
  });

  it('findMarkerAt_BeyondMarkerRadius_ReturnsNull', () => {
    const index = new HitIndex(W, H);
    const entry: MarkerHitEntry = { x: 200, y: 100, w: 8, h: 8, eventId: 'e3' };
    index.add(entry);

    // Query at (220, 100) — beyond w/2 = 4 from center
    const result = index.findMarkerAt(220, 100);
    expect(result).toBeNull();
  });

  it('findMarkerAt_TwoMarkersInSameCell_ReturnsCloserOne', () => {
    const index = new HitIndex(W, H);
    const near: MarkerHitEntry = { x: 200, y: 100, w: 12, h: 12, eventId: 'near' };
    const far:  MarkerHitEntry = { x: 204, y: 100, w: 12, h: 12, eventId: 'far' };
    index.add(near);
    index.add(far);

    // Query at (200, 100) — exactly on 'near'
    const result = index.findMarkerAt(200, 100);
    expect(result?.eventId).toBe('near');
  });

  it('findBucketAt_PointInsideBucket_ReturnsBucket', () => {
    const index = new HitIndex(W, H);
    const bucket: BucketHitEntry = {
      x: 100, y: 50, w: 40, h: 30,
      bucketStartUtc: '2026-01-01T10:00:00.000Z',
      nodeId: 'node-A',
      count: 5,
    };
    index.addBucket(bucket);

    // Point inside the rect
    const result = index.findBucketAt(120, 65);
    expect(result).not.toBeNull();
    expect(result?.nodeId).toBe('node-A');
  });

  it('findBucketAt_PointOutsideBucket_ReturnsNull', () => {
    const index = new HitIndex(W, H);
    const bucket: BucketHitEntry = {
      x: 100, y: 50, w: 40, h: 30,
      bucketStartUtc: '2026-01-01T10:00:00.000Z',
      nodeId: 'node-B',
      count: 3,
    };
    index.addBucket(bucket);

    // Point outside the rect
    const result = index.findBucketAt(200, 65);
    expect(result).toBeNull();
  });

  it('findMarkerAt_1000Markers_CompletesUnder1ms', () => {
    const index = new HitIndex(W, H);

    // Insert 1000 markers spread across the canvas
    for (let i = 0; i < 1000; i++) {
      const entry: MarkerHitEntry = {
        x: (i % 100) * 10 + 5,
        y: Math.floor(i / 100) * 40 + 20,
        w: 8,
        h: 8,
        eventId: `marker-${i}`,
      };
      index.add(entry);
    }

    // Run 100 random lookups and verify each takes < 1ms
    for (let q = 0; q < 100; q++) {
      const qx = Math.random() * W;
      const qy = Math.random() * H;
      const t0 = performance.now();
      index.findMarkerAt(qx, qy);
      const elapsed = performance.now() - t0;
      expect(elapsed).toBeLessThan(1);
    }
  });
});
