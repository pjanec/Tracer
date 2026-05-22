// tracer-viewer/src/api/tracerApiClient.ts
// Hand-authored stub matching the backend DTOs

import type { EventListDto, EventAggregateDto } from '@/types/timeline';
import type { TraceTreeDto } from '@/types/causalTree';
import type { SqlExecuteRequestDto, SqlExecuteResultDto, SqlExplainRequestDto, SqlExplainResultDto, SqlSchemaDto, ViewSqlTemplateResultDto } from '@/types/sql';
import type { SavedQueryDto, SavedQueryListDto, CreateSavedQueryDto, UpdateSavedQueryDto } from '@/types/savedQuery';
import type { BundleLibraryListDto, UpdateBundleMetadataDto } from '@/types/bundle';

export interface CurrentBundleDto {
  bundleId: string;
  label?: string;
  timeRange: { startUtc: string; endUtc: string };
}

export interface OpenBundleRequestDto {
  path: string;
}

export interface OpenBundleResponseDto {
  bundleId: string;
}

export interface SessionDto {
  sessionId: string;
  scenarioId: string;
  label?: string;
  startUtc: string;
  endUtc?: string;
  status: string;
  participatingNodes: string[];
  eventCount: number;
}

export interface ScenarioPhaseDto {
  phaseName: string;
  startedAtUtc: string;
  endedAtUtc?: string;
  status: string;
}

export interface NotableEventDto {
  eventId: string;
  traceId: string;
  occurredAtUtc: string;
  topic: string;
  notableLabel: string;
  severity?: string;
  entityId?: string;
  scenarioPhase?: string;
  payloadJson?: string;
}

export interface EventDto {
  eventId: string;
  traceId: string;
  occurredAtUtc: string;
  topic: string;
  severity?: string;
  notableLabel?: string;
  payloadJson?: string;
  publisherNode: string;
}

export interface ScenarioStateDto {
  currentPhase: string;
  sessionElapsed: string;
  totalEvents: number;
  totalNotables: number;
}

export interface NodeInfoDto {
  nodeId: string;
  eventsPublished: number;
  firstSeenUtc: string;
  lastSeenUtc: string;
}

export interface TopologyDto {
  nodes: NodeInfoDto[];
  asOfUtc: string;
}

export interface LiveStatusDto {
  ingestedTotal: number;
  connectedSources: number;
  currentInterval: string;
}

export interface EventListRequestDto {
  sessionId: string;
  from?: Date;
  to?: Date;
  topics?: string[];
  nodes?: string[];
  traceId?: string;
  entityIds?: string[];
  playerIds?: string[];
  severities?: string[];
  notablesOnly?: boolean;
  limit?: number;
}

export interface EventAggregateRequestDto {
  sessionId: string;
  from: Date;
  to:   Date;
  bucketDuration: string;  // '100ms' | '1s' | '5s' | '30s' | '1m' | '5m' | '30m' | '1h'
  groupBy?: 'node' | 'topic' | 'severity' | 'none';
  topics?: string[];
  nodes?: string[];
  traceId?: string;
  entityIds?: string[];
  playerIds?: string[];
  severities?: string[];
  notablesOnly?: boolean;
}

export interface EntitySummaryDto {
  entityId: string;
  firstSeenUtc: string;
  lastSeenUtc: string;
  eventCount: number;
  samplePlayerId?: string;
  topics: string[];
}

export interface EntityListDto {
  entities: EntitySummaryDto[];
  count: number;
}

export interface EntityEventDto {
  eventId: string;
  traceId: string;
  occurredAtUtc: string;
  topic: string;
  severity?: string;
  notableLabel?: string;
  payloadJson?: string;
  publisherNode: string;
}

export interface EntityEventsDto {
  entityId: string;
  events: EntityEventDto[];
  truncated: boolean;
}

export interface SlowStateSampleDto {
  topic: string;
  occurredAtUtc: string;
  payloadJson: string;
  traceId?: string;
}

export interface EntitySlowStateDto {
  entityId: string;
  byTopic: Record<string, SlowStateSampleDto[]>;
}

export interface FastStateColumnDto {
  name: string;
  isNumeric: boolean;
}

export interface FastStateTopicSchemaDto {
  entityId: string;
  topic: string;
  columns: FastStateColumnDto[];
}

export interface FastStateSampleDto {
  ts: string;
  values: Record<string, number | null>;
}

export interface EntityFastStateDto {
  entityId: string;
  topic: string;
  columns: string[];
  samples: FastStateSampleDto[];
  totalSamples: number;
  downsampled: boolean;
}

export interface AnnotationDto {
  annotationId: string;
  sessionId: string;
  kind: string;          // "Event" | "Entity" | "Trace" | "TimePoint"
  eventId?: string;
  entityId?: string;
  traceId?: string;
  wallclockTimestamp?: string;  // ISO-8601 for TimePoint kind
  body: string;
  title?: string;
  tags: string[];
  author?: string;
  createdAtUtc: string;
  modifiedAtUtc?: string;
}

export interface CreateAnnotationDto {
  sessionId: string;
  kind: string;
  eventId?: string;
  entityId?: string;
  traceId?: string;
  wallclockTimestamp?: string;
  body: string;
  title?: string;
  tags?: string[];
  author?: string;
}

export interface UpdateAnnotationDto {
  body?: string;
  title?: string;
  tags?: string[];
}

export interface AnnotationListDto {
  annotations: AnnotationDto[];
}

export type SavedViewKind = 'SavedView' | 'Bookmark';

export interface SavedViewDto {
  savedViewId: string;
  sessionId: string;
  kind: SavedViewKind;
  viewType: string;
  url: string;
  label: string;
  description?: string;
  persona: string;
  author?: string;
  createdAtUtc: string;
  lastOpenedAtUtc?: string;
  openCount: number;
}

export interface CreateSavedViewDto {
  sessionId: string;
  kind: SavedViewKind;
  viewType: string;
  url: string;
  label: string;
  description?: string;
  persona: string;
  author?: string;
}

export interface UpdateSavedViewDto {
  label?: string;
  description?: string;
}

export interface TriggerEvaluationDto {
  eventId: string;
  evaluatedAtUtc: string;
  publisherNode: string;
  traceId: string;
  triggerId: string;
  triggerLabel?: string;
  inputs: string;
  result: string;
  nextEventId?: string;
  reason?: string;
}

export interface TriggerEvaluationListDto {
  evaluations: TriggerEvaluationDto[];
}

// ── Phase 9: Latency DTOs ─────────────────────────────────────────────────────

export interface HistogramBucketDto {
  index: number;
  lowMs: number;
  highMs: number;
  count: number;
}

export interface LatencyDistributionDto {
  sampleCount: number;
  p50Ms: number;
  p90Ms: number;
  p99Ms: number;
  p999Ms: number;
  maxMs: number;
  minMs: number;
  meanMs: number;
  stddevMs: number;
  buckets: HistogramBucketDto[];
}

export interface LatencyPairSummaryDto {
  topic: string;
  publisherNode: string;
  subscriberNode: string;
  sampleCount: number;
  p50Ms: number;
  p99Ms: number;
  maxMs: number;
}

export interface LatencyTimeSeriesPointDto {
  bucketStartUtc: string;
  p50Ms: number;
  p99Ms: number;
  sampleCount: number;
}

export interface LatencyTimeSeriesDto {
  bucketSize: string;
  points: LatencyTimeSeriesPointDto[];
}

export interface LatencyOutlierDto {
  eventId: string;
  topic: string;
  publisherNode: string;
  subscriberNode: string;
  publishWallclockUtc: string;
  receiveWallclockUtc: string;
  latencyMs: number;
  thresholdMs: number;
  budgetSource: string; // "budget" | "top-0.1%"
}

export interface LatencyOutlierListDto {
  outliers: LatencyOutlierDto[];
  budgetsUsed: LatencyBudgetDto[];
}

// ── Phase 9: Gap DTOs ────────────────────────────────────────────────────────

export interface GapDto {
  topic: string;
  publisherNode: string;
  subscriberNode: string;
  previousSequence: number;
  resumedAtSequence: number;
  missingCount: number;
  resumedAtWallclockUtc: string;
}

export interface GapResultDto {
  gaps: GapDto[];
  totalGaps: number;
}

// ── Phase 9: Network Topology DTOs ──────────────────────────────────────────

export interface NetworkTopologyEdgeDto {
  topic: string;
  publisherNode: string;
  subscriberNode: string;
  messageCount: number;
  firstSeenUtc: string;
  lastSeenUtc: string;
}

export interface NetworkTopologyDto {
  nodes: string[];
  edges: NetworkTopologyEdgeDto[];
}

// ── Phase 9: Budget DTOs ─────────────────────────────────────────────────────

export interface LatencyBudgetDto {
  topic: string;
  p99BudgetMs?: number;
  absoluteMaxMs?: number;
}

export interface LatencyBudgetListDto {
  budgets: LatencyBudgetDto[];
}

export class TracerApiClient {
  async listSessions(from?: string, to?: string): Promise<SessionDto[]> {
    const params = new URLSearchParams();
    if (from) params.set('from', from);
    if (to) params.set('to', to);
    const query = params.toString() ? `?${params.toString()}` : '';
    const res = await fetch(`/api/sessions${query}`);
    if (!res.ok) throw new Error(`listSessions: ${res.status}`);
    return res.json() as Promise<SessionDto[]>;
  }

  async getSession(sessionId: string): Promise<SessionDto | null> {
    const res = await fetch(`/api/sessions/${encodeURIComponent(sessionId)}`);
    if (res.status === 404) return null;
    if (!res.ok) throw new Error(`getSession: ${res.status}`);
    return res.json() as Promise<SessionDto>;
  }

  async getScenarioPhases(sessionId: string): Promise<ScenarioPhaseDto[]> {
    const res = await fetch(`/api/scenario/phases?sessionId=${encodeURIComponent(sessionId)}`);
    if (!res.ok) throw new Error(`getScenarioPhases: ${res.status}`);
    return res.json() as Promise<ScenarioPhaseDto[]>;
  }

  async getScenarioNotables(
    sessionId: string,
    limit?: number,
    before?: string,
  ): Promise<NotableEventDto[]> {
    const params = new URLSearchParams({ sessionId });
    if (limit !== undefined) params.set('limit', String(limit));
    if (before) params.set('before', before);
    const res = await fetch(`/api/scenario/notables?${params.toString()}`);
    if (!res.ok) throw new Error(`getScenarioNotables: ${res.status}`);
    return res.json() as Promise<NotableEventDto[]>;
  }

  async getScenarioState(sessionId: string): Promise<ScenarioStateDto> {
    const res = await fetch(`/api/scenario/state?sessionId=${encodeURIComponent(sessionId)}`);
    if (!res.ok) throw new Error(`getScenarioState: ${res.status}`);
    return res.json() as Promise<ScenarioStateDto>;
  }

  async getEvent(eventId: string): Promise<EventDto | null> {
    const res = await fetch(`/api/events/${encodeURIComponent(eventId)}`);
    if (res.status === 404) return null;
    if (!res.ok) throw new Error(`getEvent: ${res.status}`);
    return res.json() as Promise<EventDto>;
  }

  async getTopology(): Promise<TopologyDto> {
    const res = await fetch('/api/topology');
    if (!res.ok) throw new Error(`getTopology: ${res.status}`);
    return res.json() as Promise<TopologyDto>;
  }

  async getLiveStatus(): Promise<LiveStatusDto> {
    const res = await fetch('/api/live/status');
    if (!res.ok) throw new Error(`getLiveStatus: ${res.status}`);
    return res.json() as Promise<LiveStatusDto>;
  }

  async getCurrentBundle(): Promise<CurrentBundleDto | null> {
    const res = await fetch('/api/bundle/current');
    if (!res.ok) throw new Error(`getCurrentBundle: ${res.status}`);
    return res.json() as Promise<CurrentBundleDto | null>;
  }

  async openBundle(request: OpenBundleRequestDto): Promise<OpenBundleResponseDto> {
    const res = await fetch('/api/bundle/open', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(request),
    });
    if (!res.ok) throw new Error(`openBundle: ${res.status}`);
    return res.json() as Promise<OpenBundleResponseDto>;
  }

  async closeBundle(): Promise<void> {
    const res = await fetch('/api/bundle/close', { method: 'POST' });
    if (!res.ok) throw new Error(`closeBundle: ${res.status}`);
  }

  async listEvents(req: EventListRequestDto, opts?: { signal?: AbortSignal }): Promise<EventListDto> {
    const params = new URLSearchParams({ sessionId: req.sessionId });
    if (req.from)          params.set('from',  req.from.toISOString());
    if (req.to)            params.set('to',    req.to.toISOString());
    if (req.traceId)       params.set('traceId', req.traceId);
    if (req.notablesOnly)  params.set('notablesOnly', 'true');
    if (req.limit != null) params.set('limit', String(req.limit));
    req.topics?.forEach((t) => params.append('topic', t));
    req.nodes?.forEach((n) => params.append('node', n));
    req.severities?.forEach((s) => params.append('severity', s));
    req.entityIds?.forEach((e) => params.append('entityId', e));
    req.playerIds?.forEach((p) => params.append('playerId', p));

    const res = await fetch(`/api/events?${params.toString()}`, { signal: opts?.signal });
    if (!res.ok) throw new Error(`listEvents: ${res.status}`);
    return res.json() as Promise<EventListDto>;
  }

  async aggregateEvents(req: EventAggregateRequestDto, opts?: { signal?: AbortSignal }): Promise<EventAggregateDto> {
    const params = new URLSearchParams({
      sessionId:      req.sessionId,
      from:           req.from.toISOString(),
      to:             req.to.toISOString(),
      bucketDuration: req.bucketDuration,
    });
    if (req.groupBy)       params.set('groupBy', req.groupBy);
    if (req.traceId)       params.set('traceId', req.traceId);
    if (req.notablesOnly)  params.set('notablesOnly', 'true');
    req.topics?.forEach((t) => params.append('topic', t));
    req.nodes?.forEach((n) => params.append('node', n));
    req.severities?.forEach((s) => params.append('severity', s));
    req.entityIds?.forEach((e) => params.append('entityId', e));
    req.playerIds?.forEach((p) => params.append('playerId', p));

    const res = await fetch(`/api/events/aggregate?${params.toString()}`, { signal: opts?.signal });
    if (!res.ok) throw new Error(`aggregateEvents: ${res.status}`);
    return res.json() as Promise<EventAggregateDto>;
  }

  async listBundles(): Promise<{ bundleId: string; label?: string; createdAtUtc: string }[]> {
    const res = await fetch('/api/bundle/list');
    if (res.status === 404) return [];
    if (!res.ok) throw new Error(`listBundles: ${res.status}`);
    return res.json() as Promise<{ bundleId: string; label?: string; createdAtUtc: string }[]>;
  }

  async buildBundle(sessionId: string): Promise<{ bundleId: string }> {
    const res = await fetch('/api/bundle/build', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ sessionId }),
    });
    if (!res.ok) throw new Error(`buildBundle: ${res.status}`);
    return res.json() as Promise<{ bundleId: string }>;
  }

  async getTraceTree(
    traceId: string,
    maxEvents = 1000,
    opts?: { signal?: AbortSignal },
  ): Promise<TraceTreeDto> {
    const params = new URLSearchParams({ maxEvents: String(maxEvents) });
    const res = await fetch(`/api/traces/${traceId}/tree?${params}`, { signal: opts?.signal });
    if (!res.ok) throw new Error(`getTraceTree: ${res.status}`);
    return res.json() as Promise<TraceTreeDto>;
  }

  async getTraceByEvent(
    eventId: string,
    maxEvents = 1000,
    opts?: { signal?: AbortSignal },
  ): Promise<TraceTreeDto> {
    const params = new URLSearchParams({ maxEvents: String(maxEvents) });
    const res = await fetch(`/api/events/${eventId}/trace?${params}`, { signal: opts?.signal });
    if (!res.ok) throw new Error(`getTraceByEvent: ${res.status}`);
    return res.json() as Promise<TraceTreeDto>;
  }

  async getEventAncestors(
    eventId: string,
    maxDepth = 50,
    opts?: { signal?: AbortSignal },
  ): Promise<TraceTreeDto> {
    const params = new URLSearchParams({ maxDepth: String(maxDepth) });
    const res = await fetch(`/api/events/${eventId}/ancestors?${params}`, { signal: opts?.signal });
    if (!res.ok) throw new Error(`getEventAncestors: ${res.status}`);
    return res.json() as Promise<TraceTreeDto>;
  }

  async getEventDescendants(
    eventId: string,
    maxDepth = 30,
    maxNodes = 1000,
    opts?: { signal?: AbortSignal },
  ): Promise<TraceTreeDto> {
    const params = new URLSearchParams({ maxDepth: String(maxDepth), maxNodes: String(maxNodes) });
    const res = await fetch(`/api/events/${eventId}/descendants?${params}`, { signal: opts?.signal });
    if (!res.ok) throw new Error(`getEventDescendants: ${res.status}`);
    return res.json() as Promise<TraceTreeDto>;
  }

  async listEntities(
    sessionId: string,
    opts?: { topic?: string; playerId?: string; limit?: number; signal?: AbortSignal },
  ): Promise<EntityListDto> {
    const params = new URLSearchParams({ sessionId });
    if (opts?.topic) params.set('topic', opts.topic);
    if (opts?.playerId) params.set('playerId', opts.playerId);
    if (opts?.limit != null) params.set('limit', String(opts.limit));
    const res = await fetch(`/api/entities?${params}`, { signal: opts?.signal });
    if (!res.ok) throw new Error(`listEntities: ${res.status}`);
    return res.json() as Promise<EntityListDto>;
  }

  async getEntitySummary(
    entityId: string,
    sessionId: string,
    opts?: { signal?: AbortSignal },
  ): Promise<EntitySummaryDto | null> {
    const params = new URLSearchParams({ sessionId });
    const res = await fetch(`/api/entities/${encodeURIComponent(entityId)}/summary?${params}`, { signal: opts?.signal });
    if (res.status === 404) return null;
    if (!res.ok) throw new Error(`getEntitySummary: ${res.status}`);
    return res.json() as Promise<EntitySummaryDto>;
  }

  async getEntityEvents(
    entityId: string,
    sessionId: string,
    from: Date,
    to: Date,
    opts?: { limit?: number; signal?: AbortSignal },
  ): Promise<EntityEventsDto> {
    const params = new URLSearchParams({ sessionId, from: from.toISOString(), to: to.toISOString() });
    if (opts?.limit != null) params.set('limit', String(opts.limit));
    const res = await fetch(`/api/entities/${encodeURIComponent(entityId)}/events?${params}`, { signal: opts?.signal });
    if (!res.ok) throw new Error(`getEntityEvents: ${res.status}`);
    return res.json() as Promise<EntityEventsDto>;
  }

  async getEntitySlowState(
    entityId: string,
    sessionId: string,
    from: Date,
    to: Date,
    opts?: { topics?: string[]; signal?: AbortSignal },
  ): Promise<EntitySlowStateDto> {
    const params = new URLSearchParams({ sessionId, from: from.toISOString(), to: to.toISOString() });
    opts?.topics?.forEach(t => params.append('topic', t));
    const res = await fetch(`/api/entities/${encodeURIComponent(entityId)}/slow-state?${params}`, { signal: opts?.signal });
    if (!res.ok) throw new Error(`getEntitySlowState: ${res.status}`);
    return res.json() as Promise<EntitySlowStateDto>;
  }

  async getEntityFastStateTopics(
    entityId: string,
    sessionId: string,
    opts?: { signal?: AbortSignal },
  ): Promise<string[]> {
    const params = new URLSearchParams({ sessionId });
    const res = await fetch(`/api/entities/${encodeURIComponent(entityId)}/fast-state/topics?${params}`, { signal: opts?.signal });
    if (!res.ok) throw new Error(`getEntityFastStateTopics: ${res.status}`);
    return res.json() as Promise<string[]>;
  }

  async getEntityFastStateSchema(
    entityId: string,
    topic: string,
    sessionId: string,
    opts?: { signal?: AbortSignal },
  ): Promise<FastStateTopicSchemaDto | null> {
    const params = new URLSearchParams({ sessionId });
    const res = await fetch(`/api/entities/${encodeURIComponent(entityId)}/fast-state/${encodeURIComponent(topic)}/schema?${params}`, { signal: opts?.signal });
    if (res.status === 404) return null;
    if (!res.ok) throw new Error(`getEntityFastStateSchema: ${res.status}`);
    return res.json() as Promise<FastStateTopicSchemaDto>;
  }

  async getEntityFastState(
    entityId: string,
    topic: string,
    sessionId: string,
    from: Date,
    to: Date,
    columns: string[],
    opts?: { maxSamples?: number; signal?: AbortSignal },
  ): Promise<EntityFastStateDto> {
    const params = new URLSearchParams({ sessionId, from: from.toISOString(), to: to.toISOString() });
    columns.forEach(c => params.append('column', c));
    if (opts?.maxSamples != null) params.set('maxSamples', String(opts.maxSamples));
    const res = await fetch(`/api/entities/${encodeURIComponent(entityId)}/fast-state/${encodeURIComponent(topic)}?${params}`, { signal: opts?.signal });
    if (!res.ok) throw new Error(`getEntityFastState: ${res.status}`);
    return res.json() as Promise<EntityFastStateDto>;
  }
  async listAnnotations(sessionId: string, opts?: { signal?: AbortSignal }): Promise<AnnotationDto[]> {
    const params = new URLSearchParams({ sessionId });
    const res = await fetch(`/api/annotations?${params}`, { signal: opts?.signal });
    if (!res.ok) throw new Error(`listAnnotations: ${res.status}`);
    const data = await res.json() as AnnotationListDto;
    return data.annotations;
  }

  async getAnnotation(annotationId: string): Promise<AnnotationDto | null> {
    const res = await fetch(`/api/annotations/${encodeURIComponent(annotationId)}`);
    if (res.status === 404) return null;
    if (!res.ok) throw new Error(`getAnnotation: ${res.status}`);
    return res.json() as Promise<AnnotationDto>;
  }

  async createAnnotation(dto: CreateAnnotationDto): Promise<AnnotationDto> {
    const res = await fetch('/api/annotations', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(dto),
    });
    if (!res.ok) throw new Error(`createAnnotation: ${res.status}`);
    return res.json() as Promise<AnnotationDto>;
  }

  async updateAnnotation(annotationId: string, dto: UpdateAnnotationDto): Promise<AnnotationDto | null> {
    const res = await fetch(`/api/annotations/${encodeURIComponent(annotationId)}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(dto),
    });
    if (res.status === 404) return null;
    if (!res.ok) throw new Error(`updateAnnotation: ${res.status}`);
    return res.json() as Promise<AnnotationDto>;
  }

  async deleteAnnotation(annotationId: string): Promise<void> {
    const res = await fetch(`/api/annotations/${encodeURIComponent(annotationId)}`, {
      method: 'DELETE',
    });
    if (!res.ok && res.status !== 404) throw new Error(`deleteAnnotation: ${res.status}`);
  }

  async listSavedViews(params: {
    sessionId?: string;
    kind?: SavedViewKind;
    viewType?: string;
    persona?: string;
    orderBy?: string;
    limit?: number;
  }): Promise<SavedViewDto[]> {
    const qs = new URLSearchParams();
    if (params.sessionId) qs.set('sessionId', params.sessionId);
    if (params.kind) qs.set('kind', params.kind);
    if (params.viewType) qs.set('viewType', params.viewType);
    if (params.persona) qs.set('persona', params.persona);
    if (params.orderBy) qs.set('orderBy', params.orderBy);
    if (params.limit != null) qs.set('limit', String(params.limit));
    const res = await fetch(`/api/saved-views?${qs}`);
    if (!res.ok) throw new Error(`listSavedViews: ${res.status}`);
    return res.json() as Promise<SavedViewDto[]>;
  }

  async createSavedView(dto: CreateSavedViewDto): Promise<SavedViewDto> {
    const res = await fetch('/api/saved-views', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(dto),
    });
    if (!res.ok) throw new Error(`createSavedView: ${res.status}`);
    return res.json() as Promise<SavedViewDto>;
  }

  async deleteSavedView(savedViewId: string): Promise<void> {
    const res = await fetch(`/api/saved-views/${encodeURIComponent(savedViewId)}`, { method: 'DELETE' });
    if (!res.ok && res.status !== 404) throw new Error(`deleteSavedView: ${res.status}`);
  }

  async recordSavedViewOpened(savedViewId: string): Promise<void> {
    const res = await fetch(`/api/saved-views/${encodeURIComponent(savedViewId)}/opened`, { method: 'POST' });
    if (!res.ok) throw new Error(`recordSavedViewOpened: ${res.status}`);
  }

  async listTriggerEvaluations(params: {
    sessionId: string;
    from?: string;
    to?: string;
    triggerId?: string;
    result?: string;
    limit?: number;
  }): Promise<TriggerEvaluationDto[]> {
    const qs = new URLSearchParams({ sessionId: params.sessionId });
    if (params.from) qs.set('from', params.from);
    if (params.to) qs.set('to', params.to);
    if (params.triggerId) qs.set('triggerId', params.triggerId);
    if (params.result) qs.set('result', params.result);
    if (params.limit != null) qs.set('limit', String(params.limit));
    const res = await fetch(`/api/scenario/triggers?${qs}`);
    if (!res.ok) throw new Error(`listTriggerEvaluations: ${res.status}`);
    const data = await res.json() as TriggerEvaluationListDto;
    return data.evaluations;
  }

  // ── Phase 9: Latency / Gap / Topology / Budget API ────────────────────────

  private static apiError(method: string, status: number): never {
    const err = new Error(`${method}: ${status}`) as Error & { status: number };
    err.status = status;
    throw err;
  }

  async getLatencyDistribution(
    params: {
      from: string; to: string;
      topic?: string; publisherNode?: string; subscriberNode?: string;
      excludeSelf?: boolean;
    },
    signal?: AbortSignal,
  ): Promise<LatencyDistributionDto> {
    const qs = new URLSearchParams({ from: params.from, to: params.to });
    if (params.topic) qs.set('topic', params.topic);
    if (params.publisherNode) qs.set('publisherNode', params.publisherNode);
    if (params.subscriberNode) qs.set('subscriberNode', params.subscriberNode);
    if (params.excludeSelf !== undefined) qs.set('excludeSelf', String(params.excludeSelf));
    const res = await fetch(`/api/latency/distribution?${qs}`, { signal });
    if (!res.ok) TracerApiClient.apiError('getLatencyDistribution', res.status);
    return res.json() as Promise<LatencyDistributionDto>;
  }

  async getLatencyPairs(
    params: { from: string; to: string; minSamples?: number; limit?: number },
    signal?: AbortSignal,
  ): Promise<LatencyPairSummaryDto[]> {
    const qs = new URLSearchParams({ from: params.from, to: params.to });
    if (params.minSamples != null) qs.set('minSamples', String(params.minSamples));
    if (params.limit != null) qs.set('limit', String(params.limit));
    const res = await fetch(`/api/latency/pairs?${qs}`, { signal });
    if (!res.ok) TracerApiClient.apiError('getLatencyPairs', res.status);
    return res.json() as Promise<LatencyPairSummaryDto[]>;
  }

  async getLatencyTimeSeries(
    params: {
      from: string; to: string;
      topic?: string; publisherNode?: string; subscriberNode?: string;
    },
    signal?: AbortSignal,
  ): Promise<LatencyTimeSeriesDto> {
    const qs = new URLSearchParams({ from: params.from, to: params.to });
    if (params.topic) qs.set('topic', params.topic);
    if (params.publisherNode) qs.set('publisherNode', params.publisherNode);
    if (params.subscriberNode) qs.set('subscriberNode', params.subscriberNode);
    const res = await fetch(`/api/latency/timeseries?${qs}`, { signal });
    if (!res.ok) TracerApiClient.apiError('getLatencyTimeSeries', res.status);
    return res.json() as Promise<LatencyTimeSeriesDto>;
  }

  async getLatencyOutliers(
    params: {
      from: string; to: string;
      topic?: string; publisherNode?: string; subscriberNode?: string;
    },
    signal?: AbortSignal,
  ): Promise<LatencyOutlierListDto> {
    const qs = new URLSearchParams({ from: params.from, to: params.to });
    if (params.topic) qs.set('topic', params.topic);
    if (params.publisherNode) qs.set('publisherNode', params.publisherNode);
    if (params.subscriberNode) qs.set('subscriberNode', params.subscriberNode);
    const res = await fetch(`/api/latency/outliers?${qs}`, { signal });
    if (!res.ok) TracerApiClient.apiError('getLatencyOutliers', res.status);
    return res.json() as Promise<LatencyOutlierListDto>;
  }

  async getGaps(
    params: {
      from: string; to: string;
      topic?: string; publisherNode?: string; subscriberNode?: string;
    },
    signal?: AbortSignal,
  ): Promise<GapResultDto> {
    const qs = new URLSearchParams({ from: params.from, to: params.to });
    if (params.topic) qs.set('topic', params.topic);
    if (params.publisherNode) qs.set('publisherNode', params.publisherNode);
    if (params.subscriberNode) qs.set('subscriberNode', params.subscriberNode);
    const res = await fetch(`/api/gaps?${qs}`, { signal });
    if (!res.ok) TracerApiClient.apiError('getGaps', res.status);
    return res.json() as Promise<GapResultDto>;
  }

  async getNetworkTopology(
    params: { from: string; to: string },
    signal?: AbortSignal,
  ): Promise<NetworkTopologyDto> {
    const qs = new URLSearchParams({ from: params.from, to: params.to });
    const res = await fetch(`/api/topology/network?${qs}`, { signal });
    if (!res.ok) TracerApiClient.apiError('getNetworkTopology', res.status);
    return res.json() as Promise<NetworkTopologyDto>;
  }

  async getLatencyBudgets(sessionId: string, signal?: AbortSignal): Promise<LatencyBudgetListDto> {
    const qs = new URLSearchParams({ sessionId });
    const res = await fetch(`/api/scenario/budgets?${qs}`, { signal });
    if (!res.ok) TracerApiClient.apiError('getLatencyBudgets', res.status);
    const data = await res.json() as { budgets: LatencyBudgetDto[] };
    return { budgets: data.budgets };
  }

  // ── Phase 10: SQL Console ──────────────────────────────────────────────────

  async executeSql(req: SqlExecuteRequestDto, signal?: AbortSignal): Promise<SqlExecuteResultDto> {
    const res = await fetch('/api/sql/execute', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(req),
      signal,
    });
    if (!res.ok) throw new Error(`executeSql: ${res.status}`);
    return res.json() as Promise<SqlExecuteResultDto>;
  }

  async getSqlSchema(signal?: AbortSignal): Promise<SqlSchemaDto> {
    const res = await fetch('/api/sql/schema', { signal });
    if (!res.ok) throw new Error(`getSqlSchema: ${res.status}`);
    return res.json() as Promise<SqlSchemaDto>;
  }

  async explainSql(req: SqlExplainRequestDto, signal?: AbortSignal): Promise<SqlExplainResultDto> {
    const res = await fetch('/api/sql/explain', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(req),
      signal,
    });
    if (!res.ok) throw new Error(`explainSql: ${res.status}`);
    return res.json() as Promise<SqlExplainResultDto>;
  }

  async getViewSqlTemplate(viewType: string, params: Record<string, string> = {}): Promise<ViewSqlTemplateResultDto> {
    const qs = new URLSearchParams({ viewType, ...params });
    const res = await fetch(`/api/sql/view-template?${qs}`);
    if (!res.ok) throw new Error(`getViewSqlTemplate: ${res.status}`);
    return res.json() as Promise<ViewSqlTemplateResultDto>;
  }

  // ── Phase 10: Saved Queries ────────────────────────────────────────────────

  async listSavedQueries(opts?: {
    tag?: string; author?: string; favorite?: boolean; builtIn?: boolean; signal?: AbortSignal;
  }): Promise<SavedQueryDto[]> {
    const qs = new URLSearchParams();
    if (opts?.tag) qs.set('tag', opts.tag);
    if (opts?.author) qs.set('author', opts.author);
    if (opts?.favorite !== undefined) qs.set('favorite', String(opts.favorite));
    if (opts?.builtIn !== undefined) qs.set('builtIn', String(opts.builtIn));
    const res = await fetch(`/api/saved-queries?${qs}`, { signal: opts?.signal });
    if (!res.ok) throw new Error(`listSavedQueries: ${res.status}`);
    const data = await res.json() as SavedQueryListDto;
    return data.queries;
  }

  async getSavedQuery(id: string): Promise<SavedQueryDto | null> {
    const res = await fetch(`/api/saved-queries/${encodeURIComponent(id)}`);
    if (res.status === 404) return null;
    if (!res.ok) throw new Error(`getSavedQuery: ${res.status}`);
    return res.json() as Promise<SavedQueryDto>;
  }

  async createSavedQuery(dto: CreateSavedQueryDto): Promise<SavedQueryDto> {
    const res = await fetch('/api/saved-queries', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(dto),
    });
    if (!res.ok) throw new Error(`createSavedQuery: ${res.status}`);
    return res.json() as Promise<SavedQueryDto>;
  }

  async updateSavedQuery(id: string, dto: UpdateSavedQueryDto): Promise<SavedQueryDto | null> {
    const res = await fetch(`/api/saved-queries/${encodeURIComponent(id)}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(dto),
    });
    if (res.status === 404 || res.status === 405) return null;
    if (!res.ok) throw new Error(`updateSavedQuery: ${res.status}`);
    return res.json() as Promise<SavedQueryDto>;
  }

  async deleteSavedQuery(id: string): Promise<void> {
    const res = await fetch(`/api/saved-queries/${encodeURIComponent(id)}`, { method: 'DELETE' });
    if (!res.ok && res.status !== 404) throw new Error(`deleteSavedQuery: ${res.status}`);
  }

  async toggleSavedQueryFavorite(id: string): Promise<SavedQueryDto | null> {
    const res = await fetch(`/api/saved-queries/${encodeURIComponent(id)}/favorite`, { method: 'POST' });
    if (res.status === 404) return null;
    if (!res.ok) throw new Error(`toggleSavedQueryFavorite: ${res.status}`);
    return res.json() as Promise<SavedQueryDto>;
  }

  async cloneSavedQuery(id: string, label: string): Promise<SavedQueryDto> {
    const res = await fetch(`/api/saved-queries/${encodeURIComponent(id)}/clone`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ label }),
    });
    if (!res.ok) throw new Error(`cloneSavedQuery: ${res.status}`);
    return res.json() as Promise<SavedQueryDto>;
  }

  async recordSavedQueryRun(id: string): Promise<void> {
    const res = await fetch(`/api/saved-queries/${encodeURIComponent(id)}/run`, { method: 'POST' });
    if (!res.ok) throw new Error(`recordSavedQueryRun: ${res.status}`);
  }

  // ── Phase 10: Bundle Library ───────────────────────────────────────────────

  async listBundleLibrary(opts?: {
    showArchived?: boolean; tag?: string; sortBy?: string; sortDesc?: boolean; signal?: AbortSignal;
  }): Promise<BundleLibraryListDto> {
    const qs = new URLSearchParams();
    if (opts?.showArchived !== undefined) qs.set('showArchived', String(opts.showArchived));
    if (opts?.tag) qs.set('tag', opts.tag);
    if (opts?.sortBy) qs.set('sortBy', opts.sortBy);
    if (opts?.sortDesc !== undefined) qs.set('sortDesc', String(opts.sortDesc));
    const res = await fetch(`/api/bundles/library?${qs}`, { signal: opts?.signal });
    if (!res.ok) throw new Error(`listBundleLibrary: ${res.status}`);
    return res.json() as Promise<BundleLibraryListDto>;
  }

  async updateBundleMetadata(bundleId: string, dto: UpdateBundleMetadataDto): Promise<void> {
    const res = await fetch(`/api/bundles/${encodeURIComponent(bundleId)}/metadata`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(dto),
    });
    if (!res.ok) throw new Error(`updateBundleMetadata: ${res.status}`);
  }

  async recordBundleOpened(bundleId: string): Promise<void> {
    const res = await fetch(`/api/bundles/${encodeURIComponent(bundleId)}/opened`, { method: 'POST' });
    if (!res.ok) throw new Error(`recordBundleOpened: ${res.status}`);
  }

  async deleteBundle(bundleId: string): Promise<void> {
    const res = await fetch(`/api/bundles/${encodeURIComponent(bundleId)}`, { method: 'DELETE' });
    if (!res.ok && res.status !== 404) throw new Error(`deleteBundle: ${res.status}`);
  }

  getBundleDownloadUrl(bundleId: string): string {
    return `/api/bundles/${encodeURIComponent(bundleId)}/download`;
  }
}

export const api = new TracerApiClient();
