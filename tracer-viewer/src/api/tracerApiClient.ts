// tracer-viewer/src/api/tracerApiClient.ts
// Hand-authored stub matching the backend DTOs

import type { EventListDto, EventAggregateDto } from '@/types/timeline';

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
}

export const api = new TracerApiClient();
