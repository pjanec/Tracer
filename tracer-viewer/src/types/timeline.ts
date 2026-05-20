// tracer-viewer/src/types/timeline.ts
// Shared TypeScript interfaces for timeline rendering and components

export interface TimeRange {
  from: Date;
  to:   Date;
}

export interface TimelineFilter {
  topics?: string[];
  nodes?: string[];
  traceId?: string;
  entityIds?: string[];
  playerIds?: string[];
  severities?: string[];
  notablesOnly?: boolean;
}

// DTOs mirroring backend /api/events response
export interface EventDto {
  eventId: string;
  traceId: string;
  publishWallclock: string;  // ISO 8601
  publisherNode: string;
  topic: string;
  severity?: string;
  notableLabel?: string;
  payloadJson?: string;
}

export interface EventListDto {
  events: EventDto[];
  totalMatching: number;
  returned: number;
  truncated: boolean;
}

export interface EventAggregateBucketGroupDto {
  groupKey: string | null;
  count: number;
}

export interface EventAggregateBucketDto {
  bucketStartUtc: string;
  groups: EventAggregateBucketGroupDto[];
  total: number;
}

export interface EventAggregateDto {
  bucketDuration: string;
  buckets: EventAggregateBucketDto[];
}
