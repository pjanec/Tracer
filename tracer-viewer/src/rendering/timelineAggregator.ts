// tracer-viewer/src/rendering/timelineAggregator.ts
// Client-side bucket merging for live mode.
// Used when new events arrive via SSE and the current view is in aggregate mode.

import type { EventDto, EventAggregateDto, EventAggregateBucketDto } from '@/types/timeline';

/**
 * Merges a new event into an existing aggregate result.
 * Finds the correct bucket for the event's timestamp and increments the matching group's count.
 * If no matching bucket/group exists, creates it.
 * Returns a new EventAggregateDto (immutable update).
 */
export function appendEventToAggregate(
  existing: EventAggregateDto,
  event: EventDto,
  groupBy: 'node' | 'topic' | 'severity' | 'none',
): EventAggregateDto {
  const evtMs = new Date(event.publishWallclock).getTime();
  const bucketDurMs = _parseDuration(existing.bucketDuration);
  const groupKey = _groupKeyFor(event, groupBy);

  // Find matching bucket
  const bucketIndex = existing.buckets.findIndex((b) => {
    const bStart = new Date(b.bucketStartUtc).getTime();
    return evtMs >= bStart && evtMs < bStart + bucketDurMs;
  });

  if (bucketIndex === -1) {
    // No matching bucket — create a new one
    const bucketStart = new Date(
      Math.floor(evtMs / bucketDurMs) * bucketDurMs,
    ).toISOString();

    const newBucket: EventAggregateBucketDto = {
      bucketStartUtc: bucketStart,
      groups: [{ groupKey, count: 1 }],
      total: 1,
    };

    // Insert in sorted order
    const newBuckets = [...existing.buckets, newBucket].sort(
      (a, b) =>
        new Date(a.bucketStartUtc).getTime() - new Date(b.bucketStartUtc).getTime(),
    );

    return { ...existing, buckets: newBuckets };
  }

  // Update existing bucket
  const bucket = existing.buckets[bucketIndex];
  const groupIndex = bucket.groups.findIndex((g) => g.groupKey === groupKey);

  let newGroups;
  if (groupIndex === -1) {
    newGroups = [...bucket.groups, { groupKey, count: 1 }];
  } else {
    newGroups = bucket.groups.map((g, i) =>
      i === groupIndex ? { ...g, count: g.count + 1 } : g,
    );
  }

  const newBucket: EventAggregateBucketDto = {
    ...bucket,
    groups: newGroups,
    total: bucket.total + 1,
  };

  const newBuckets = existing.buckets.map((b, i) =>
    i === bucketIndex ? newBucket : b,
  );

  return { ...existing, buckets: newBuckets };
}

// --- Helpers ---

function _groupKeyFor(event: EventDto, groupBy: 'node' | 'topic' | 'severity' | 'none'): string | null {
  switch (groupBy) {
    case 'node':     return event.publisherNode ?? null;
    case 'topic':    return event.topic ?? null;
    case 'severity': return event.severity ?? null;
    case 'none':     return null;
  }
}

function _parseDuration(duration: string): number {
  const map: Record<string, number> = {
    '100ms': 100,
    '1s':    1_000,
    '5s':    5_000,
    '30s':   30_000,
    '1m':    60_000,
    '5m':    5 * 60_000,
    '30m':   30 * 60_000,
    '1h':    60 * 60_000,
  };
  return map[duration] ?? 1_000;
}
