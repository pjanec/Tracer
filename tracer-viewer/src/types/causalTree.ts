// tracer-viewer/src/types/causalTree.ts

export interface TraceTreeDto {
  traceId: string;
  sessionId: string;              // resolved to empty string when unresolvable
  nodes: TraceNodeDto[];
  edges: TraceEdgeDto[];
  rootEventIds: string[];
  leafEventIds: string[];
  summary: TraceSummaryDto;
}

export interface TraceNodeDto {
  eventId: string;
  traceId: string;
  parentEventId?: string | null;
  publishWallclock: string;     // ISO 8601 date-time string
  publisherNode: string;
  topic: string;
  entityId?: string | null;
  severity?: string | null;     // 'info' | 'warning' | 'error' | null
  notableLabel?: string | null;
  payloadJson?: string | null;
}

export interface TraceEdgeDto {
  parentEventId: string;
  childEventId: string;
  latencyMs: number;
}

export interface TraceSummaryDto {
  traceId: string;
  totalEvents: number;
  totalEventsAvailable?: number | null;
  truncated: boolean;
  totalSpanMs: number;
  participatingNodes: string[];
  rootCount: number;
  leafCount: number;
  firstEventUtc?: string | null;
  lastEventUtc?: string | null;
}
